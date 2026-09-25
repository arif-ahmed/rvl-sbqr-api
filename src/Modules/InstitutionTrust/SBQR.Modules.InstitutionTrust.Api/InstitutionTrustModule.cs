using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SBQR.Modules.InstitutionTrust.Application.Services;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;
using SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.InstitutionTrust.Api;

/// <summary>
/// Composition root for the InstitutionTrust module: the manual trust
/// directory (admin upsert + cross-module public-key query) AND the BB
/// Trust-Sync pipeline (<c>DailyTrustSyncService</c> + the <c>ITrustStoreClient</c>
/// ACL). The sync does NOT run at startup — the host comes up silent, with
/// no DB writes, so an operator can apply the canonical schema and any
/// pre-loaded seed data independently of the running process. The first
/// sync tick lands one <c>TrustStoreOptions.SyncIntervalHours</c> after
/// boot (default 24); subsequent ticks follow the same cadence. Per-run
/// audit trail lives in <c>audit_logs</c> (action
/// <c>institution.trust.sync.completed</c>); per-institution changes emit
/// <c>institution.trust.key.published</c> / <c>.revoked</c> from
/// <see cref="InstitutionUpsertService"/>.
///
/// The manual upsert endpoint remains a permanent fallback alongside the
/// sync pipeline rather than being replaced by it.
///
/// <c>TrustStore:BaseUrl</c> is validated eagerly via <c>ValidateOnStart</c>,
/// so a misconfigured host fails to boot instead of logging one exception per
/// sync tick forever.
/// </summary>
public sealed class InstitutionTrustModule : IModule
{
    public string Name => "institution-trust";

    public Assembly ApplicationPartAssembly =>
        typeof(SBQR.Modules.InstitutionTrust.Application.Commands.UpsertInstitutionCommand).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<InstitutionTrustDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for InstitutionTrustDbContext.");
            options.UseNpgsql(connectionString);
        });

        services.AddScoped<InstitutionUpsertService>();

        // Cross-module publisher seam: KeyCustody calls this on a successful
        // POST /v1/crypto-keys so the freshly minted public key lands in the
        // trust directory without a separate POST /v1/admin/institutions
        // call. Delegates to the same InstitutionUpsertService above, so the
        // wire contract, audit row, and retire-previous-active semantics
        // stay in one place.
        services.AddScoped<IInstitutionTrustPublisher, InstitutionTrustPublisher>();

        // Fail at HOST STARTUP, not on first resolution of the typed client:
        // without ValidateOnStart a missing BaseUrl only surfaces once per
        // sync tick, forever, as a logged Error on a host that looks healthy.
        services.AddOptions<TrustStoreOptions>()
            .Bind(configuration.GetSection(TrustStoreOptions.SectionName))
            .Validate(
                o => !string.IsNullOrWhiteSpace(o.BaseUrl),
                "TrustStore:BaseUrl is required. Point it at the BB trust store, " +
                "or at a trust-store endpoint you control for local development.")
            .Validate(
                o => string.IsNullOrWhiteSpace(o.BaseUrl)
                    || Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _),
                "TrustStore:BaseUrl must be an absolute URI (e.g. http://localhost:8082).")
            .ValidateOnStart();

        services.AddHttpClient<ITrustStoreClient, HttpTrustStoreClient>((sp, client) =>
        {
            // Defence in depth — ValidateOnStart above is the primary guard.
            var options = sp.GetRequiredService<IOptions<TrustStoreOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.BaseUrl))
            {
                throw new InvalidOperationException("Missing configuration 'TrustStore:BaseUrl'.");
            }

            client.BaseAddress = new Uri(options.BaseUrl);
            if (!string.IsNullOrWhiteSpace(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("X-Api-Key", options.ApiKey);
            }
        });

        services.AddHostedService<DailyTrustSyncService>();
    }
}
