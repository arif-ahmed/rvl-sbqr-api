using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Api.Security;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;
using SBQR.Modules.Tenancy.Infrastructure.Persistence.Interceptors;
using SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;
using SBQR.Modules.Tenancy.Infrastructure.Persistence.UnitOfWork;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Api;

/// <summary>
/// Composition root for the Tenancy &amp; Access module. The host
/// (<c>SBQR.Api/Program.cs</c>) discovers this via reflection over the
/// <see cref="IModule"/> implementations and calls
/// <see cref="RegisterServices"/> at startup.
///
/// This module owns:
/// <list type="bullet">
///   <item>the <c>tenants</c> aggregate. The <c>tenant_configurations</c> aggregate
///         and all the OAuth2 client-credentials plumbing (<c>POST /v1/oauth/token</c>,
///         Argon2id hasher, JWT issuer, bootstrap options) live in the
///         IdentityAccess module; the <c>crypto_keys</c> aggregate and the
///         full signing-key lifecycle (mint/rotate/suspend/reinstate/retire)
///         relocated to the KeyCustody module;</item>
///   <item>the audit-column <see cref="TenancyAuditColumnInterceptor"/> that stamps
///         <c>created_by/at</c> + <c>modified_by/at</c> on every save;</item>
///   <item>the <see cref="TenantRepository"/> and the <see cref="TenancyUnitOfWork"/>;</item>
///   <item>the <see cref="HttpContextActorProvider"/> that resolves the audit actor
///         from the authenticated principal's <c>sub</c> claim
///         (<c>platform:…</c> / <c>client:…</c>, fallback <c>"system"</c>);</item>
///   <item>the cross-module tenant-configuration seam — this module's
///         <c>provision-tenant-configuration</c>, <c>suspend</c>, <c>reactivate</c>, and
///         <c>terminate</c> handlers call
///         <c>SBQR.Modules.IdentityAccess.Contracts.ITenantConfigurationProvisioner</c>
///         to drive the tenant's <c>tenant_configurations</c> rows (the
///         <c>register</c> handler alone stays off the seam: the tenant row
///         ships without configurations, and the dedicated
///         <c>POST /v1/admin/tenants/{id}/tenant-configuration</c> endpoint provisions on
///         demand);</item>
///   <item>the cross-module signing-key cascade seam — the same
///         <c>suspend</c>, <c>reactivate</c>, and <c>terminate</c> handlers also
///         call <c>SBQR.Modules.KeyCustody.Contracts.{Suspend,Reinstate}TenantSigningKeysCommand</c>
///         to mirror tenant state onto the tenant's signing key. The
///         <c>register</c> handler stays off this seam too — the tenant row
///         ships without a key, and a dedicated KeyCustody endpoint mints one
///         on demand;</item>
///   <item>MediatR handlers and FluentValidation validators, auto-discovered from
///         the Application assembly by the host.</item>
/// </list>
///
/// MediatR, FluentValidation, AddControllers ApplicationParts, and the global
/// <c>ValidationBehavior&lt;,&gt;</c> are all registered in
/// <c>SBQR.Api/Program.cs</c>; this module does NOT duplicate those registrations.
/// </summary>
public sealed class TenancyModule : IModule
{
    /// <summary>The module name used in logs / health responses.</summary>
    public string Name => "tenancy";

    /// <summary>
    /// The Application assembly — MediatR scans this for
    /// <c>IRequestHandler&lt;,&gt;</c> implementations (e.g.
    /// <c>CreateTenantCommandHandler</c>), and FluentValidation scans this for
    /// <c>IValidator&lt;T&gt&gt;</c> implementations (e.g.
    /// <c>CreateTenantValidator</c>). This deliberately points at the
    /// Application project (NOT this API assembly): handlers + validators live
    /// there. The host uses this property when wiring
    /// <c>AddMediatR</c> / <c>AddValidatorsFromAssembly</c>.
    /// </summary>
    public Assembly ApplicationPartAssembly { get; } =
        typeof(SBQR.Modules.Tenancy.Application.Commands.CreateTenant.CreateTenantCommandHandler).Assembly;

    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---------------------------------------------------------------------
        // 1. EF Core DbContext (Tenancy schema). The audit interceptor is
        //    resolved from the scope so it can pull IActorProvider + ICurrentTenant.
        //    Schema is owned by db/migrations/*.sql and applied by the
        //    external migration tool — this context NEVER calls
        //    Database.Migrate() (PERSISTENCE_DECISIONS.md §3).
        // ---------------------------------------------------------------------
        services.AddDbContext<TenancyDbContext>((sp, opts) =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for TenancyDbContext. " +
                    "Configure ConnectionStrings:sbqr_app in appsettings.json or environment variables.");

            opts.UseNpgsql(connectionString);
            opts.AddInterceptors(sp.GetRequiredService<TenancyAuditColumnInterceptor>());
        });

        // ---------------------------------------------------------------------
        // 2. Audit interceptor. Registered as a scoped service so its
        //    IActorProvider dependency stays request-scoped.
        // ---------------------------------------------------------------------
        services.AddScoped<TenancyAuditColumnInterceptor>();

        // ---------------------------------------------------------------------
        // 3. Repository + Unit-of-Work. ApiCredentialRepository relocated to
        //    IdentityAccess together with the api_credentials aggregate;
        //    CryptoKeyRepository relocated to KeyCustody together with the
        //    crypto_keys aggregate and its full lifecycle. The
        //    TenantApplicationRepository stays in Tenancy together with the
        //    tenant_configurations aggregate's physical table — both are
        //    tenant-owned rows.
        // ---------------------------------------------------------------------
        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<SBQR.Modules.Tenancy.Domain.Interfaces.ITenantApplicationRepository, TenantApplicationRepository>();
        services.AddScoped<IUnitOfWork, TenancyUnitOfWork>();

        // Cross-module read seam: the IdentityAccess token handler resolves
        // this at mint time to reject Suspended/Terminated tenants. Without
        // this registration POST /v1/oauth/token fails to resolve its handler.
        services.AddScoped<SBQR.Modules.Tenancy.Contracts.ITenantAdmissionDirectory, TenantAdmissionDirectory>();

        // Cross-module read seam (FR-AUTH-002): the IdentityAccess token
        // handler resolves this at mint time to reject token requests whose
        // claimed package_id is not on the tenant's allow-list. Same
        // pattern as ITenantAdmissionDirectory — published-language seam in
        // Tenancy.Contracts, implementation in Tenancy.Infrastructure.
        services.AddScoped<SBQR.Modules.Tenancy.Contracts.ITenantApplicationDirectory, TenantApplicationDirectory>();

        // Cross-module read seam for the QR flows: QrGeneration stamps Tag 26 from
        // the tenant's institution code; Verification decides own-custody vs
        // trust-directory key resolution from it. KeyCustody's
        // GenerateOrAdoptCryptoKeyCommandHandler also uses this to confirm a
        // tenant exists before minting a key.
        services.AddScoped<SBQR.Modules.Tenancy.Contracts.ITenantDirectory, TenantDirectory>();

        // ---------------------------------------------------------------------
        // 4. Actor provider. HttpContextActorProvider resolves the audit
        //    actor from the authenticated principal's `sub` claim
        //    (platform:… / client:…); "system" when no principal is present
        //    (background jobs, boot-time work). IHttpContextAccessor is
        //    registered globally by the host. KeyCustody's own cascade
        //    handlers (SuspendTenantSigningKeys / ReinstateTenantSigningKeys)
        //    also resolve IActorProvider from this same registration — one
        //    shared container, one actor grammar.
        // ---------------------------------------------------------------------
        services.AddScoped<IActorProvider, HttpContextActorProvider>();

        // The OAuth2 client-credentials surface (POST /v1/oauth/token, Jwt:SigningKey,
        // Auth:Bootstrap:ClientSecretHash, ISecretHasher, IAccessTokenIssuer,
        // the named authorization policies) all live in IdentityAccessModule
        // now. Tenancy only owns the tenant-configuration cascade trigger —
        // registering ITenantConfigurationProvisioner in
        // SBQR.Modules.IdentityAccess.Api. Wires the two modules together
        // without Tenancy ever touching IdentityAccess.Domain or
        // IdentityAccess.Infrastructure. The signing-key cascade trigger
        // works the same way against KeyCustody.Contracts.
    }
}
