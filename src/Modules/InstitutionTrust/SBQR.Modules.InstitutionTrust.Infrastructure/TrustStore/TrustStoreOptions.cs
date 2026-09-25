namespace SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;

/// <summary>Binds the <c>TrustStore</c> configuration section.</summary>
public sealed class TrustStoreOptions
{
    public const string SectionName = "TrustStore";

    /// <summary>Base URL of the trust store — the mock today, real BB later.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// How often <c>DailyTrustSyncService</c> ticks. Default 24 (daily).
    /// Clamped to a minimum of 1 hour in the background service.
    /// </summary>
    public int SyncIntervalHours { get; set; } = 24;

    /// <summary>
    /// Development override for the sync cadence: when set (> 0) it takes
    /// precedence over <see cref="SyncIntervalHours"/> and is clamped to a
    /// minimum of one minute. Binds the existing
    /// <c>TrustStore:SyncIntervalMinutes</c> key (seeded into dev
    /// user-secrets) so a dev loop sees mock-published keys within a
    /// minute instead of waiting a full day. Leave unset in
    /// staging/production — the hourly clamp on the hours setting still
    /// applies there.
    /// </summary>
    public int? SyncIntervalMinutes { get; set; }

    /// <summary>
    /// When true, <c>DailyTrustSyncService</c> runs one sync IMMEDIATELY at
    /// startup instead of waiting a full interval for the first tick.
    /// Default false: the host must come up stable and silent with no DB
    /// writes, so an operator can apply the canonical schema and seed data
    /// independently of the running process (see the service's class doc).
    /// Enabled only via dev user-secrets (or the compose env vars) to make
    /// the local "upload to the mock, then verify" loop work on a fresh
    /// boot. Never set this in staging/production.
    /// </summary>
    public bool SyncOnStartup { get; set; }

    /// <summary>Optional API key sent as <c>X-Api-Key</c>. No real BB auth scheme is published yet; this is a placeholder slot.</summary>
    public string? ApiKey { get; set; }
}
