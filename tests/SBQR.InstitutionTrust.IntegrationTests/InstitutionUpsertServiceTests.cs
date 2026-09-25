using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SBQR.InstitutionTrust.IntegrationTests.Infrastructure;
using SBQR.Modules.InstitutionTrust.Application.Services;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using Xunit;

namespace SBQR.InstitutionTrust.IntegrationTests;

[Collection(nameof(InstitutionTrustCollection))]
public sealed class InstitutionUpsertServiceTests : IAsyncLifetime
{
    private const string SamplePem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQhqowtrbLDFw4rXAxZuE=\n-----END PUBLIC KEY-----";

    // A second, genuinely different Ed25519 key (openssl genpkey) — used to
    // exercise the real rotation path, where the previous version must be
    // retired and the version bumped.
    private const string RotatedPem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAqMWif47sImAnahuBCpOSs8UKZDoYaBoBYq8V1DVkt6I=\n-----END PUBLIC KEY-----";

    private readonly PostgreSqlFixture _postgres;

    public InstitutionUpsertServiceTests(PostgreSqlFixture postgres)
    {
        _postgres = postgres;
    }

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task New_institution_gets_key_version_1_and_an_audit_entry()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var service = new InstitutionUpsertService(db, audit);

        var result = await service.UpsertAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: SamplePem,
            actorId: "system:trust-sync",
            cancellationToken: CancellationToken.None);

        result.ActiveKeyVersion.Should().Be(1);
        result.InstitutionName.Should().Be("Example Bank");
        result.InstituteType.Should().Be("03");

        // The merged institution_keys row carries the cryptographic
        // publication AND the denormalised institution_name +
        // institute_type. Both also live in public.tenants joined on
        // institution_code, but the trust-directory row is the canonical
        // read for verifiers.
        var key = await db.Keys.IgnoreQueryFilters()
            .SingleAsync(k => k.InstitutionCode == "031008");
        key.PublicKey.Should().Be(SamplePem);
        key.InstituteType.Should().Be("03");
        key.InstitutionName.Should().Be("Example Bank");
        key.Source.Should().Be("LOCAL");
        key.Status.Should().Be("ACTIVE");
        key.IsActive.Should().BeTrue();
        key.ValidTo.Should().BeNull();
        key.RevokedAt.Should().BeNull();
        key.CreatedBy.Should().Be("system:trust-sync");

        audit.Entries.Should().ContainSingle(e =>
            e.Action == "institution.trust.key.published"
            && e.ActorId == "system:trust-sync"
            && e.ResourceId == "031008");
    }

    /// <summary>
    /// The unchanged-record short-circuit: re-publishing the SAME key
    /// material (the common case on every sync tick once the directory is
    /// current) must NOT retire-and-reinsert. Without this, a periodic sync
    /// would mint a new key version on every tick, forever.
    /// </summary>
    [Fact]
    public async Task Republishing_the_same_key_is_an_idempotent_metadata_update()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var service = new InstitutionUpsertService(db, audit);

        await service.UpsertAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: SamplePem,
            actorId: "system:trust-sync",
            cancellationToken: CancellationToken.None);

        var second = await service.UpsertAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank (renamed)",
            publicKeyPem: SamplePem,
            actorId: "system:trust-sync",
            cancellationToken: CancellationToken.None);

        second.ActiveKeyVersion.Should().Be(1, "no version is minted for identical key material");

        var keys = await db.Keys.IgnoreQueryFilters()
            .Where(k => k.InstitutionCode == "031008")
            .ToListAsync();
        keys.Should().ContainSingle("the previous ACTIVE row was reused, not replaced");
        keys[0].Status.Should().Be("ACTIVE");
        keys[0].ValidTo.Should().BeNull("an unchanged key was never superseded");
        keys[0].InstitutionName.Should().Be("Example Bank (renamed)", "identity metadata still refreshes");
        keys[0].SyncedAt.Should().BeAfter(keys[0].CreatedAt);

        // Exactly ONE publication audit — the no-op must not add churn.
        audit.Entries.Should().ContainSingle(e => e.Action == "institution.trust.key.published");
    }

    [Fact]
    public async Task Republishing_a_new_key_retires_the_previous_key_and_bumps_the_version()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var service = new InstitutionUpsertService(db, audit);

        await service.UpsertAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: SamplePem,
            actorId: "system:trust-sync",
            cancellationToken: CancellationToken.None);
        var second = await service.UpsertAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: RotatedPem,
            actorId: "system:trust-sync",
            cancellationToken: CancellationToken.None);

        second.ActiveKeyVersion.Should().Be(2);

        var retiredCount = await db.Keys.IgnoreQueryFilters()
            .CountAsync(k => k.InstitutionCode == "031008" && k.Status == "RETIRED");
        retiredCount.Should().Be(1);

        // Audit trail records the retired_versions metadata so an operator
        // can answer "what got rotated out?" without joining to history.
        var published = audit.Entries
            .Where(e => e.Action == "institution.trust.key.published")
            .ToList();
        published.Should().HaveCount(2);
        published[1].Metadata.Should().Contain("\"retired_versions\":[1]");
    }
}
