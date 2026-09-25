using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SBQR.InstitutionTrust.IntegrationTests.Infrastructure;
using SBQR.Modules.InstitutionTrust.Application.Services;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using Xunit;

namespace SBQR.InstitutionTrust.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="InstitutionTrustPublisher"/> — the
/// cross-module seam KeyCustody calls on a successful
/// <c>POST /v1/crypto-keys</c>. Uses the real <see cref="InstitutionUpsertService"/>
/// against the real Postgres container (the same code path the manual
/// <c>POST /v1/admin/institutions</c> endpoint runs) so the contract under
/// test is identical to production behaviour.
/// </summary>
[Collection(nameof(InstitutionTrustCollection))]
public sealed class InstitutionTrustPublisherIntegrationTests : IAsyncLifetime
{
    private const string SamplePem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQhqowtrbLDFw4rXAxZuE=\n-----END PUBLIC KEY-----";

    // A second, genuinely different Ed25519 key (openssl genpkey) — the
    // rotation path only bumps the version for NEW key material; re-publishing
    // the same PEM is an idempotent metadata update (see InstitutionUpsertService).
    private const string RotatedPem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAqMWif47sImAnahuBCpOSs8UKZDoYaBoBYq8V1DVkt6I=\n-----END PUBLIC KEY-----";

    private readonly PostgreSqlFixture _postgres;

    public InstitutionTrustPublisherIntegrationTests(PostgreSqlFixture postgres)
    {
        _postgres = postgres;
    }

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publisher_delegates_to_upsert_service_and_records_crypto_create_actor()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var upsert = new InstitutionUpsertService(db, audit);
        var publisher = new InstitutionTrustPublisher(upsert);

        var result = await publisher.PublishActivePublicKeyAsync(
            institutionCode: "031008",
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: SamplePem);

        result.ActiveKeyVersion.Should().Be(1);
        result.PublicKeySha256.Should().NotBeNullOrWhiteSpace();

        // The trust-store side-effect happened via the same upsert service
        // the manual admin path uses — assert via DB state, not by spying
        // on a mock. The merged institution_keys row carries the source
        // ("LOCAL" — this is one of OUR tenants' signing keys, not a
        // trust-store-registry row), the cryptographic publication, the
        // denormalised institution name + institute type, and its
        // validity window.
        var activeKey = await db.Keys
            .Where(k => k.InstitutionCode == "031008" && k.Status == "ACTIVE")
            .SingleAsync();
        activeKey.PublicKey.Should().Be(SamplePem);
        activeKey.InstituteType.Should().Be("03");
        activeKey.InstitutionName.Should().Be("Example Bank");
        activeKey.Source.Should().Be("LOCAL");
        activeKey.ValidTo.Should().BeNull();
        activeKey.RevokedAt.Should().BeNull();

        // Audit-row attribution: the publisher must use its crypto-create
        // actor tag (NOT "system" from the daily sync, NOT a tenant client
        // credential — those would be wrong for an auto-publish triggered
        // by KeyCustody).
        audit.Entries.Should().ContainSingle(e =>
            e.Action == "institution.trust.key.published"
            && e.ActorId == IInstitutionTrustPublisher.CryptoCreateActor
            && e.ResourceId == "031008");
    }

    [Fact]
    public async Task Publisher_republishes_a_new_key_retire_previous_active_and_bump_version()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var upsert = new InstitutionUpsertService(db, audit);
        var publisher = new InstitutionTrustPublisher(upsert);

        var first = await publisher.PublishActivePublicKeyAsync("020202", "02", "Example Bank", SamplePem);
        first.ActiveKeyVersion.Should().Be(1);

        // A rotation: genuinely new key material (NOT a re-publish of the
        // same PEM — that path is now an idempotent metadata update).
        var second = await publisher.PublishActivePublicKeyAsync("020202", "02", "Example Bank", RotatedPem);
        second.ActiveKeyVersion.Should().Be(2);

        // One row retired by supersession (status flipped, valid_to closed,
        // but NOT a revocation — RevokedAt stays null because retirement
        // by a newer key version is not a trust-store revocation; see
        // InstitutionUpsertService.UpsertAsync), one ACTIVE.
        var keys = await db.Keys.IgnoreQueryFilters()
            .Where(k => k.InstitutionCode == "020202")
            .OrderBy(k => k.KeyVersion)
            .ToListAsync();
        keys.Should().HaveCount(2);
        keys[0].Status.Should().Be("RETIRED");
        keys[0].RevokedAt.Should().BeNull("retirement-by-supersession is not a revocation");
        keys[0].ValidTo.Should().NotBeNull("supersession closes the validity window so the C4/C16 gate rejects the old version");
        keys[1].Status.Should().Be("ACTIVE");
        keys[1].RevokedAt.Should().BeNull();
        keys[1].ValidTo.Should().BeNull();
    }

    [Fact]
    public async Task Publisher_rejects_blank_inputs()
    {
        var options = new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;
        var audit = new CapturingAuditLogger();

        await using var db = new InstitutionTrustDbContext(options);
        var publisher = new InstitutionTrustPublisher(new InstitutionUpsertService(db, audit));

        var act = async () => await publisher.PublishActivePublicKeyAsync(
            institutionCode: string.Empty,
            instituteType: "03",
            institutionName: "Example Bank",
            publicKeyPem: SamplePem);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
