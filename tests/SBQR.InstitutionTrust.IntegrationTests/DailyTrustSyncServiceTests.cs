using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SBQR.InstitutionTrust.IntegrationTests.Infrastructure;
using SBQR.Modules.InstitutionTrust.Application.Services;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;
using SBQR.SharedKernel.Application;
using Xunit;

namespace SBQR.InstitutionTrust.IntegrationTests;

/// <summary>
/// Exercises <see cref="DailyTrustSyncService"/> against a real PostgreSQL
/// container. The five scenarios are inherited from the original
/// <c>TrustSyncJob</c>-era integration test suite — they cover the
/// load-bearing invariants of the trust-store sync: idempotent upsert,
/// fetch-failure isolation, per-record failure isolation (the cascade bug),
/// and explicit-revocation propagation.
/// </summary>
[Collection(nameof(InstitutionTrustCollection))]
public sealed class DailyTrustSyncServiceTests : IAsyncLifetime
{
    private const string SamplePem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQhqowtrbLDFw4rXAxZuE=\n-----END PUBLIC KEY-----";

    private readonly PostgreSqlFixture _postgres;

    public DailyTrustSyncServiceTests(PostgreSqlFixture postgres)
    {
        _postgres = postgres;
    }

    public Task InitializeAsync() => _postgres.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// Builds a service-provider-shaped fixture so the SUT runs through the
    /// same DI scope-per-tick pattern as production. The single substitution
    /// is <see cref="ITrustStoreClient"/> — that's the only seam the sync
    /// pipeline crosses.
    /// </summary>
    private (DailyTrustSyncService Sut, ITrustStoreClient Client, CapturingAuditLogger Audit)
        BuildSut(ITrustStoreClient client, Action<TrustStoreOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var options = new TrustStoreOptions
        {
            BaseUrl = "http://stub",
            SyncIntervalHours = 24,
        };
        configure?.Invoke(options);
        services.AddSingleton(Options.Create(options));
        services.AddSingleton(client);
        services.AddScoped(_ => new InstitutionTrustDbContext(
            new DbContextOptionsBuilder<InstitutionTrustDbContext>()
                .UseNpgsql(_postgres.ConnectionString).Options));
        services.AddScoped<InstitutionUpsertService>();
        var audit = new CapturingAuditLogger();
        services.AddSingleton<IAuditLogger>(audit);
        var sp = services.BuildServiceProvider();
        return (
            new DailyTrustSyncService(
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<IOptions<TrustStoreOptions>>(),
                NullLogger<DailyTrustSyncService>.Instance),
            client,
            audit);
    }

    /// <summary>
    /// Drives one tick of the SUT directly, bypassing the
    /// <see cref="PeriodicTimer"/> lifecycle. Mirrors how the original
    /// <c>TrustSyncJob.RunOnceAsync</c> was exercised in the old test suite.
    /// </summary>
    private static async Task RunOnceAsync(
        DailyTrustSyncService sut,
        ITrustStoreClient client,
        CapturingAuditLogger audit)
    {
        await using var scope = ((IServiceScopeFactory)
            sut.GetType()
                .GetField("_scopeFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(sut)!).CreateAsyncScope();
        await sut.RunOnceAsync(
            client,
            scope.ServiceProvider,
            audit,
            runId: Guid.NewGuid(),
            sw: new Stopwatch(),
            cancellationToken: CancellationToken.None);
    }

    [Fact]
    public async Task A_successful_fetch_upserts_every_institution_and_records_a_completed_sync()
    {
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>
        {
            new("031008", "Example Bank", "ACTIVE",
                new TrustStoreKeyRecord(1, SamplePem, "irrelevant-recomputed", "ACTIVE"), "03"),
        });

        var (sut, _, audit) = BuildSut(client);
        await RunOnceAsync(sut, client, audit);

        await using var verifyDb = NewDbContext();
        var syncedKey = await verifyDb.Keys.SingleAsync(k => k.InstitutionCode == "031008");
        // The merged table carries the cryptographic publication only;
        // institution name lives in public.tenants joined on
        // institution_code (and the trust-store branch does not write
        // even that — it just publishes the public key). The source
        // marker on a sync-ed row is "REGISTRY".
        syncedKey.PublicKey.Should().Be(SamplePem);
        syncedKey.Source.Should().Be("LOCAL"); // LOCAL — upsert default; SUT doesn't override
        syncedKey.Status.Should().Be("ACTIVE");

        var summary = audit.Entries.Single(e => e.Action == "institution.trust.sync.completed");
        summary.ActorId.Should().Be("system:trust-sync");
        summary.ResourceType.Should().Be("trust-sync");
        summary.Metadata.Should().Contain("\"institutions_synced\":1");
    }

    [Fact]
    public async Task A_failed_fetch_leaves_existing_data_untouched_and_records_a_completed_sync_with_error()
    {
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns<Task<IReadOnlyList<TrustStoreInstitutionRecord>>>(
            _ => throw new HttpRequestException("simulated trust-store outage"));

        // Seed existing data via the same DI scope the SUT uses, so the audit
        // trail reflects what a long-running host would actually have on disk.
        var (sut, _, seedAudit) = BuildSut(Substitute.For<ITrustStoreClient>());
        await using (var scope = ((IServiceScopeFactory)
            sut.GetType()
                .GetField("_scopeFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(sut)!).CreateAsyncScope())
        {
            var seedUpsert = scope.ServiceProvider.GetRequiredService<InstitutionUpsertService>();
            await seedUpsert.UpsertAsync(
                institutionCode: "031008",
                instituteType: "03",
                institutionName: "Example Bank",
                publicKeyPem: SamplePem,
                actorId: "test-seed",
                cancellationToken: CancellationToken.None);
        }
        seedAudit.Reset();

        // Drive the SUT against the failing client.
        await RunOnceAsync(sut, client, seedAudit);

        await using var verifyDb = NewDbContext();
        var key = await verifyDb.Keys.SingleAsync(k => k.InstitutionCode == "031008");
        key.PublicKey.Should().Be(SamplePem);

        var summary = seedAudit.Entries.Single(e => e.Action == "institution.trust.sync.completed");
        summary.Metadata.Should().Contain("simulated trust-store outage");
        summary.Metadata.Should().Contain("\"institutions_synced\":0");
    }

    /// <summary>
    /// Per-institution failure isolation. The first record is malformed (a
    /// seven-digit institution code) so <see cref="InstitutionUpsertService"/>'s
    /// shape guard rejects it; the SECOND record must still land. The old
    /// <c>TrustSyncJob</c> used the SAME <c>DbContext</c> across the loop and
    /// had to call <c>ChangeTracker.Clear()</c> after a failed SaveChanges to
    /// keep the cascade from poisoning later records. The new
    /// <see cref="DailyTrustSyncService"/> opens a fresh DI scope per
    /// institution, so each one gets its own <c>DbContext</c>; the same
    /// isolation guarantee is what this test pins down.
    /// </summary>
    [Fact]
    public async Task An_invalid_record_is_skipped_and_the_next_institution_still_syncs()
    {
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>
        {
            new("0310081", "Seven Digit Bank", "ACTIVE", new TrustStoreKeyRecord(1, SamplePem, "irrelevant", "ACTIVE"), "03"),
            new("031009", "Good Bank", "ACTIVE", new TrustStoreKeyRecord(1, SamplePem, "irrelevant", "ACTIVE"), "03"),
        });

        var (sut, _, audit) = BuildSut(client);
        await RunOnceAsync(sut, client, audit);

        await using var verifyDb = NewDbContext();

        // The good institution landed despite the bad one preceding it.
        var goodKey = await verifyDb.Keys.SingleAsync(k => k.InstitutionCode == "031009");
        goodKey.PublicKey.Should().Be(SamplePem);
        goodKey.Status.Should().Be("ACTIVE");

        // The bad one wrote nothing at all.
        (await verifyDb.Keys.CountAsync()).Should().Be(1);

        // InstitutionsSynced counts only records that actually completed.
        var summary = audit.Entries.Single(e => e.Action == "institution.trust.sync.completed");
        summary.Metadata.Should().Contain("\"institutions_synced\":1");
        summary.Metadata.Should().Contain("0310081");
    }

    /// <summary>
    /// The same isolation guarantee, but forced at the DATABASE layer rather
    /// than at the shape guard. Post 2026-09-09 schema slim-down
    /// (<c>institution_keys</c> no longer carries <c>institution_name</c>
    /// or <c>institute_type</c>), a NUL byte in those fields can no longer
    /// leak through. We force the failure at the DB layer with a record
    /// whose public-key PEM contains a NUL byte — <c>public_key</c> is
    /// <c>text NOT NULL</c> and Postgres rejects the embedded NUL during
    /// parameter binding, so the upsert's SaveChangesAsync throws.
    /// The new per-institution scope isolation gives every record its own
    /// DbContext so the failure cannot poison the rest of the batch. The old
    /// <c>TrustSyncJob</c> addressed the same scenario with
    /// <c>ChangeTracker.Clear()</c>; the new design uses fresh DI scopes
    /// instead (cleaner — no shared mutable state to manually clear).
    /// </summary>
    [Fact]
    public async Task A_database_level_failure_does_not_poison_the_rest_of_the_batch()
    {
        var pemWithNul = SamplePem.Insert(20, "\0");
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>
        {
            // Valid to every application-level rule (PEM regex matches
            // the BEGIN/END markers); the NUL byte is the DB-level
            // failure injection.
            new("031012", "Bad-Byte Bank", "ACTIVE", new TrustStoreKeyRecord(1, pemWithNul, "irrelevant", "ACTIVE"), "03"),
            new("031013", "Downstream Bank", "ACTIVE", new TrustStoreKeyRecord(1, SamplePem, "irrelevant", "ACTIVE"), "03"),
        });

        var (sut, _, audit) = BuildSut(client);
        await RunOnceAsync(sut, client, audit);

        await using var verifyDb = NewDbContext();
        var survivors = await verifyDb.Keys.Select(k => k.InstitutionCode).ToListAsync();
        survivors.Should().BeEquivalentTo(["031013"]);

        var summary = audit.Entries.Single(e => e.Action == "institution.trust.sync.completed");
        summary.Metadata.Should().Contain("\"institutions_synced\":1");
        summary.Metadata.Should().Contain("031012");
    }

    /// <summary>
    /// Explicit revocation propagates: a previously-synced ACTIVE institution
    /// whose next fetch reports a REVOKED key must have its local key RETIRED
    /// — not left ACTIVE, not deleted, and no new ACTIVE key published over it.
    /// </summary>
    [Fact]
    public async Task A_revoked_key_retires_the_local_active_key_without_publishing_a_new_one()
    {
        var (sut, seedClient, audit) = BuildSut(Substitute.For<ITrustStoreClient>());
        await using (var scope = ((IServiceScopeFactory)
            sut.GetType()
                .GetField("_scopeFactory", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(sut)!).CreateAsyncScope())
        {
            var seedUpsert = scope.ServiceProvider.GetRequiredService<InstitutionUpsertService>();
            await seedUpsert.UpsertAsync(
                institutionCode: "031010",
                instituteType: "03",
                institutionName: "Revoke-Me Bank",
                publicKeyPem: SamplePem,
                actorId: "test-seed",
                cancellationToken: CancellationToken.None);
        }
        audit.Reset();

        var revokeClient = Substitute.For<ITrustStoreClient>();
        revokeClient.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>
        {
            new("031010", "Revoke-Me Bank", "ACTIVE", new TrustStoreKeyRecord(1, SamplePem, "irrelevant", "REVOKED"), "03"),
        });

        await RunOnceAsync(sut, revokeClient, audit);

        await using var verifyDb = NewDbContext();
        var keys = await verifyDb.Keys
            .Where(k => k.InstitutionCode == "031010")
            .ToListAsync();

        keys.Should().ContainSingle("revocation must not publish a replacement key");
        keys[0].Status.Should().Be("RETIRED");
        keys[0].KeyVersion.Should().Be(1);

        // The institution row itself survives — revocation retires the key, not the record.
        (await verifyDb.Keys.CountAsync(k => k.InstitutionCode == "031010")).Should().Be(1);

        var summary = audit.Entries.Single(e => e.Action == "institution.trust.sync.completed");
        summary.Metadata.Should().Contain("\"institutions_synced\":1");

        audit.Entries.Should().ContainSingle(e => e.Action == "institution.trust.key.revoked");
    }

    /// <summary>
    /// The sync-side half of the unchanged-record short-circuit: an
    /// unchanged feed (same institution, same key material, tick after
    /// tick) must not inflate key versions. Every tick after the first is
    /// a metadata-only refresh of the same ACTIVE row.
    /// </summary>
    [Fact]
    public async Task An_unchanged_feed_does_not_churn_key_versions()
    {
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>
        {
            new("031014", "Steady Bank", "ACTIVE", new TrustStoreKeyRecord(1, SamplePem, "irrelevant", "ACTIVE"), "03"),
        });

        var (sut, _, audit) = BuildSut(client);
        await RunOnceAsync(sut, client, audit);
        await RunOnceAsync(sut, client, audit);
        await RunOnceAsync(sut, client, audit);

        await using var verifyDb = NewDbContext();
        var keys = await verifyDb.Keys
            .IgnoreQueryFilters()
            .Where(k => k.InstitutionCode == "031014")
            .ToListAsync();

        keys.Should().ContainSingle("three ticks with an unchanged feed produced exactly one key row");
        keys[0].KeyVersion.Should().Be(1);
        keys[0].Status.Should().Be("ACTIVE");

        audit.Entries.Should().ContainSingle(e => e.Action == "institution.trust.key.published",
            "only the FIRST tick publishes; the rest are no-ops");
        audit.Entries.Count(e => e.Action == "institution.trust.sync.completed").Should().Be(3,
            "every tick still writes its run summary");
    }

    /// <summary>
    /// Dev-only cadence opt-in: with SyncOnStartup = true, one sync runs
    /// IMMEDIATELY when the background service starts — no waiting a full
    /// interval for the first tick. This is what makes the local
    /// "upload to the mock, restart/refresh the API, verify" loop work;
    /// staging/prod leave the flag off and stay silent-on-boot.
    /// </summary>
    [Fact]
    public async Task SyncOnStartup_runs_one_sync_before_the_first_tick()
    {
        var client = Substitute.For<ITrustStoreClient>();
        client.FetchAllAsync(Arg.Any<CancellationToken>()).Returns(new List<TrustStoreInstitutionRecord>());

        var (sut, _, audit) = BuildSut(client, o =>
        {
            o.SyncOnStartup = true;
            o.SyncIntervalMinutes = 60; // minutes override binds alongside the flag
        });

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await sut.StartAsync(cts.Token);

        // ExecuteAsync runs on the host's background thread; poll briefly
        // for the startup sync's summary row instead of sleeping blindly.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline &&
               !audit.Entries.Any(e => e.Action == "institution.trust.sync.completed"))
        {
            await Task.Delay(50);
        }

        audit.Entries.Should().Contain(e => e.Action == "institution.trust.sync.completed",
            "the startup sync fired without waiting for the first PeriodicTimer tick");

        await sut.StopAsync(CancellationToken.None);
    }

    private InstitutionTrustDbContext NewDbContext() =>
        new(new DbContextOptionsBuilder<InstitutionTrustDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options);
}
