using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Application.Services;

/// <summary>
/// Trust-store sync. By default waits one sync interval, then runs every
/// interval from then on — it does NOT run at startup, because the host
/// must come up stable and silent, with no DB writes, so an operator can
/// apply the canonical schema (db/migrations/*.sql) and any pre-loaded seed
/// data independently of the running process. The first tick lands one
/// interval after <see cref="StartAsync"/>; subsequent ticks follow the
/// same <see cref="PeriodicTimer"/> cadence.
///
/// Two DEVELOPMENT-ONLY opt-ins change that cadence (both bound from
/// <see cref="TrustStoreOptions"/>, both seeded into dev user-secrets,
/// neither ever set beyond dev):
/// <list type="bullet">
///   <item><c>SyncOnStartup</c> — run one sync immediately on boot, before
///   entering the timer loop, so a freshly booted dev stack picks up keys
///   published to the mock without waiting an interval. A failed startup
///   fetch is handled exactly like a failed tick (audited, non-fatal).</item>
///   <item><c>SyncIntervalMinutes</c> — a minutes-level interval that
///   overrides <see cref="TrustStoreOptions.SyncIntervalHours"/> (clamped
///   to a 1-minute floor; the hours setting keeps its 1-hour floor).</item>
/// </list>
///
/// Fetches every institution from <see cref="ITrustStoreClient"/> (the ACL
/// seam — DTOs are private to its <c>HttpTrustStoreClient</c> implementation)
/// and applies each through <see cref="InstitutionUpsertService"/>.
///
/// Never deletes: an institution absent from a given fetch is left untouched.
/// Revocation is only ever EXPLICIT — a record whose key arrives with a
/// non-ACTIVE status takes <see cref="InstitutionUpsertService.RetireKeyAsync"/>
/// instead of the upsert branch.
///
/// The per-institution audit trail is emitted by <see cref="InstitutionUpsertService"/>
/// to <c>audit_logs</c> (<c>institution.trust.key.published</c> / <c>.revoked</c>).
/// This service adds exactly ONE summary row per run via <see cref="IAuditLogger"/>
/// with action <c>institution.trust.sync.completed</c>, so operators can answer
/// "did today's sync run?" without a separate table.
///
/// To force an immediate sync without waiting one full period (operator use),
/// expose the singleton via a small CLI flag or admin endpoint that calls
/// <see cref="RunOnceAsync"/> directly — the loop above stays unchanged.
/// </summary>
public sealed partial class DailyTrustSyncService : BackgroundService
{
    private const string SyncActorId = "system:trust-sync";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TrustStoreOptions _options;
    private readonly ILogger<DailyTrustSyncService> _logger;

    public DailyTrustSyncService(
        IServiceScopeFactory scopeFactory,
        IOptions<TrustStoreOptions> options,
        ILogger<DailyTrustSyncService> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Minutes override (dev) wins over the hours setting; each keeps its
        // own floor — 1 minute for the dev override, 1 hour for the
        // production-shaped default.
        var period = _options.SyncIntervalMinutes is > 0
            ? TimeSpan.FromMinutes(Math.Max(1, _options.SyncIntervalMinutes.Value))
            : TimeSpan.FromHours(Math.Max(1, _options.SyncIntervalHours));

        var runId = Guid.NewGuid();
        var sw = new Stopwatch();

        // Dev-only opt-in (seeded user-secrets): one immediate sync
        // on boot so a fresh stack sees mock-published keys without waiting
        // an interval. Default (staging/prod) stays silent-on-startup.
        if (_options.SyncOnStartup)
        {
            await RunTickOnceAsync(runId, sw, stoppingToken).ConfigureAwait(false);
        }

        using var timer = new PeriodicTimer(period);

        // The host must come up stable — no DB writes until the first
        // scheduled tick (unless SyncOnStartup ran one above). We wait for
        // that tick BEFORE running the body, instead of running once
        // immediately and then waiting. That keeps boot-time behaviour
        // identical for callers of RunOnceAsync (tests, operator-triggered
        // invocations) while leaving the host silent on startup. Stopping
        // before the first tick returns cleanly.
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            await RunTickOnceAsync(runId, sw, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task RunTickOnceAsync(Guid runId, Stopwatch sw, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var client = scope.ServiceProvider.GetRequiredService<ITrustStoreClient>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogger>();

            await RunOnceAsync(client, scope.ServiceProvider, audit, runId, sw, stoppingToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogTickFailed(ex);
        }
    }

    /// <summary>
    /// One tick of the sync: fetch and upsert. Exposed publicly so the
    /// integration test suite can drive the behaviour without managing a
    /// <see cref="PeriodicTimer"/> / <see cref="CancellationTokenSource"/>
    /// lifecycle. Production drives it via <see cref="ExecuteAsync"/>.
    /// </summary>
    public async Task RunOnceAsync(
        ITrustStoreClient client,
        IServiceProvider outerScope,
        IAuditLogger audit,
        Guid runId,
        Stopwatch sw,
        CancellationToken cancellationToken)
    {
        sw.Restart();

        IReadOnlyList<TrustStoreInstitutionRecord> records;
        try
        {
            records = await client.FetchAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            LogFetchFailed(ex);
            await EmitRunAuditAsync(audit, runId, sw.ElapsedMilliseconds, 0, Array.Empty<string>(), ex.Message, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var synced = 0;
        var failures = new List<string>();
        foreach (var record in records)
        {
            // Fresh scope per institution: gives every upsert its own
            // InstitutionTrustDbContext so a SaveChanges failure on one
            // record (e.g. a DB-level constraint violation that slipped past
            // the shape guard) cannot leave half-attached entities in the
            // change tracker and poison every later institution in the
            // batch. This is the per-record failure isolation guarantee the
            // integration test `A_database_level_failure_does_not_poison...`
            // pins down.
            await using var perInstitutionScope = outerScope.CreateAsyncScope();
            var upsertService = perInstitutionScope.ServiceProvider.GetRequiredService<InstitutionUpsertService>();
            try
            {
                if (!string.Equals(record.ActiveKey.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                {
                    await upsertService.RetireKeyAsync(
                        record.InstitutionId,
                        record.ActiveKey.Status,
                        SyncActorId,
                        cancellationToken).ConfigureAwait(false);
                    LogInstitutionKeyRetired(record.InstitutionId, record.ActiveKey.Status);
                }
                else
                {
                    // Trust-store sync path. The merged table carries
                    // institution_name and institute_type denormalised
                    // alongside the cryptographic publication; we pass
                    // the trust-store record's identity attributes
                    // across to the upsert.
                    await upsertService.UpsertAsync(
                        institutionCode: record.InstitutionId,
                        instituteType: record.InstituteType ?? string.Empty,
                        institutionName: record.InstitutionName,
                        publicKeyPem: record.ActiveKey.PublicKeyPem,
                        actorId: SyncActorId,
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                synced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex is InvalidTrustStoreRecordException invalid)
                {
                    LogInstitutionRecordInvalid(record.InstitutionId, invalid.Reason);
                }
                else
                {
                    LogInstitutionSyncFailed(ex, record.InstitutionId);
                }

                failures.Add(record.InstitutionId);
            }
        }

        sw.Stop();
        LogRunFinished(synced, failures.Count);
        await EmitRunAuditAsync(audit, runId, sw.ElapsedMilliseconds, synced, failures, errorMessage: null, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task EmitRunAuditAsync(
        IAuditLogger audit,
        Guid runId,
        long durationMs,
        int synced,
        IReadOnlyList<string> failures,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["duration_ms"] = durationMs,
            ["institutions_synced"] = synced,
            ["failures"] = failures.ToList(),
        };
        if (errorMessage is not null)
        {
            metadata["error"] = errorMessage;
        }

        await audit.LogAsync(new AuditEntry(
            Action: "institution.trust.sync.completed",
            ActorId: SyncActorId,
            ResourceType: "trust-sync",
            ResourceId: runId.ToString(),
            Metadata: JsonSerializer.Serialize(metadata)),
            cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 6103, Level = LogLevel.Error,
        Message = "Unhandled error during trust-store sync tick.")]
    private partial void LogTickFailed(Exception ex);

    [LoggerMessage(EventId = 6101, Level = LogLevel.Warning,
        Message = "Trust store fetch failed; leaving existing directory data untouched.")]
    private partial void LogFetchFailed(Exception ex);

    [LoggerMessage(EventId = 6102, Level = LogLevel.Warning,
        Message = "Failed to sync institution {InstitutionCode} from trust store.")]
    private partial void LogInstitutionSyncFailed(Exception ex, string institutionCode);

    [LoggerMessage(EventId = 6104, Level = LogLevel.Warning,
        Message = "Skipped institution {InstitutionCode}: the trust store reported invalid data ({Reason}). Existing local data left untouched.")]
    private partial void LogInstitutionRecordInvalid(string institutionCode, string reason);

    [LoggerMessage(EventId = 6105, Level = LogLevel.Information,
        Message = "Retired the local ACTIVE key for institution {InstitutionCode}: the trust store reported status {ReportedStatus}.")]
    private partial void LogInstitutionKeyRetired(string institutionCode, string reportedStatus);

    [LoggerMessage(EventId = 6107, Level = LogLevel.Information,
        Message = "Trust-sync run finished. Synced: {Synced}. Failed: {Failed}.")]
    private partial void LogRunFinished(int synced, int failed);
}