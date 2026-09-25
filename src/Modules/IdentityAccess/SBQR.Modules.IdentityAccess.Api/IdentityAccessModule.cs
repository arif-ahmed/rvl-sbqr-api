using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SBQR.Modules.IdentityAccess.Application.Abstractions;
using SBQR.Modules.IdentityAccess.Application;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.IdentityAccess.Domain.Interfaces;
using SBQR.Modules.IdentityAccess.Infrastructure.Authentication;
using SBQR.Modules.IdentityAccess.Infrastructure.Cryptography;
using SBQR.Modules.IdentityAccess.Infrastructure.Persistence;
using SBQR.Modules.IdentityAccess.Infrastructure.Persistence.Interceptors;
using SBQR.Modules.IdentityAccess.Infrastructure.Persistence.Repositories;
using SBQR.Modules.IdentityAccess.Infrastructure.Persistence.UnitOfWork;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Api;

/// <summary>
/// Composition root for the Identity &amp; Access module. The host
/// (<c>SBQR.Api/Program.cs</c>) discovers this via the moduleTypes array and
/// calls <see cref="RegisterServices"/> at startup.
///
/// This module owns:
/// <list type="bullet">
///   <item>the <c>public.tenant_configurations</c> aggregate (the modern
///         replacement for the legacy <c>api_credentials</c> table; see
///         migration 20260911120000_) and its <see cref="IdentityDbContext"/>;</item>
///   <item>the OAuth 2.1 client-credentials token endpoint
///         (<c>POST /v1/oauth/token</c>) + the HS256
///         <see cref="JwtAccessTokenIssuer"/>;</item>
///   <item>the platform bootstrap options
///         (<see cref="PlatformBootstrapOptions"/>) bound from
///         <c>Auth:Bootstrap</c>;</item>
///   <item>the audit interceptor
///         (<see cref="IdentityAuditColumnInterceptor"/>) that stamps
///         <c>created_by/at</c> + <c>modified_by/at</c> on every save;</item>
///   <item>the Argon2id secret hasher
///         (<see cref="Argon2idSecretHasher"/>) for <c>client_secret_hash</c>;</item>
///   <item>the tenant-configuration provisioner
///         (<see cref="TenantConfigurationProvisioner"/>) exposed through
///         <see cref="ITenantConfigurationProvisioner"/> for the Tenancy module's
///         register / suspend / reactivate cascades;</item>
///   <item>the JWT bearer validation scheme + the four named policies
///         consumed by every other module's controllers;</item>
///   <item>the AutoMapper profile and (via the host scan) MediatR handlers and
///         FluentValidation validators, auto-discovered from the Application
///         assembly.</item>
/// </list>
///
/// MediatR, FluentValidation, AddAutoMapper, AddControllers ApplicationParts, and
/// the global <c>ValidationBehavior&lt;,&gt;</c> are all registered in
/// <c>SBQR.Api/Program.cs</c>; this module does NOT duplicate those registrations.
/// </summary>
public sealed class IdentityAccessModule : IModule
{
    /// <summary>The module name used in logs / health responses / OpenAPI tags.</summary>
    public string Name => "identity-access";

    /// <summary>
    /// The Application assembly — MediatR scans this for
    /// <c>IRequestHandler&lt;,&gt;</c> implementations (e.g.
    /// <c>IssueClientCredentialsTokenCommandHandler</c>) and FluentValidation
    /// scans this for <c>IValidator&lt;T&gt;</c> implementations. Points at the
    /// Application project (NOT this API assembly).
    /// </summary>
    public Assembly ApplicationPartAssembly { get; } =
        typeof(SBQR.Modules.IdentityAccess.Application.Commands.IssueToken.IssueClientCredentialsTokenCommand).Assembly;

    /// <inheritdoc/>
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---------------------------------------------------------------------
        // 1. EF Core DbContext (public schema). Schema is created/migrated by
        //    db/migrations/*.sql via the external migration tool — this
        //    context NEVER calls Database.Migrate() (PERSISTENCE_DECISIONS.md §3).
        // ---------------------------------------------------------------------
        services.AddDbContext<IdentityDbContext>((sp, opts) =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for IdentityDbContext.");
            opts.UseNpgsql(connectionString);
            opts.AddInterceptors(sp.GetRequiredService<IdentityAuditColumnInterceptor>());
        });

        // 2. Audit interceptor. Scoped so its IActorProvider dependency stays
        //    request-scoped.
        services.AddScoped<IdentityAuditColumnInterceptor>();

        // 3. Repositories + module-local unit of work. Scoped — they share the
        //    IdentityDbContext's scope for one HTTP request. IIdentityUnitOfWork
        //    (NOT the shared IUnitOfWork) so we never collide with Tenancy's
        //    registration in the same DI container.
        services.AddScoped<ITenantConfigurationRepository, TenantConfigurationRepository>();
        services.AddScoped<IIdentityUnitOfWork, IdentityUnitOfWork>();

        // 4. The cross-module seam: Tenancy's cascades call into this.
        services.AddScoped<ITenantConfigurationProvisioner, TenantConfigurationProvisioner>();

        // 5. Cryptography ports. The hasher is request-scoped (cheap, no shared
        //    state); the issuer is a singleton (stateless, keyed by config).
        services.AddScoped<ISecretHasher, Argon2idSecretHasher>();
        services.AddSingleton<IAccessTokenIssuer>(sp =>
        {
            var signingKey = configuration["Jwt:SigningKey"];
            if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
            {
                return new UnconfiguredAccessTokenIssuer();
            }

            var issuer = configuration["Jwt:Issuer"] ?? "sbqr";
            var audience = configuration["Jwt:Audience"] ?? "sbqr-api";
            var ttlMinutes = configuration.GetValue("Jwt:AccessTokenTtlMinutes", 10);
            return new JwtAccessTokenIssuer(signingKey, issuer, audience, ttlMinutes);
        });

        // 6. OAuth2 client-credentials bootstrap options.
        //    Required form across every environment (Local / Development /
        //    Staging / RC / Production) is ClientSecretHash (Argon2id PHC).
        //    No plaintext alternative exists — the property was removed from
        //    PlatformBootstrapOptions so a plaintext credential cannot reach
        //    the process by any code path.
        //
        //    Configuration is intentionally SOFT-validated: missing or
        //    malformed PHC surfaces a single warning at first resolution
        //    (FR-AUTH-001 §5 BR6 — every auth failure returns the same
        //    401 invalid_client; we must not leak config state via a 500).
        //    The token handler at
        //    IssueClientCredentialsTokenCommandHandler.IssueBootstrapTokenAsync
        //    fails closed via InvalidClientFailure() when
        //    !bootstrap.IsConfigured or when the stored hash is not a PHC
        //    string; Argon2idSecretHasher.Verify() then re-checks the PHC
        //    prefix as a defensive precondition.
        services
            .AddOptions<PlatformBootstrapOptions>()
            .Bind(configuration.GetSection("Auth:Bootstrap"))
            .PostConfigure<IServiceProvider>((options, sp) =>
            {
                var logger = sp.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("SBQR.IdentityAccess.BootstrapOptions");

                if (string.IsNullOrWhiteSpace(options.ClientSecretHash))
                {
                    BootstrapOptionsLog.ClientSecretHashUnset(logger);
                }
                else if (!options.ClientSecretHash!.StartsWith("$argon2id$", StringComparison.Ordinal))
                {
                    BootstrapOptionsLog.ClientSecretHashNotArgon2id(logger);
                }
            });

        // 7. JWT bearer validation + the four cross-cutting authorization policies.
        //    Scope claims arrive as individual "scope" claim values (the issuer
        //    writes the scopes array as multiple claim values), so RequireClaim
        //    matches a single granted value.
        //
        //    Bearer validation is only wired when Jwt:SigningKey is configured:
        //    the token endpoint can still boot (and resolve UnconfiguredAccessTokenIssuer)
        //    without it, so dev / integration-test hosts that don't exercise
        //    authenticated paths are not blocked. In Production the host's
        //    guard in Program.cs §10c refuses to start without the key.
        var signingKey = configuration["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(signingKey) && signingKey.Length >= 32)
        {
            services
                .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    // Keep the short inbound claim names ("sub", "scope",
                    // "tenant_id") — the legacy claim-type mapping would rename
                    // "sub" to the long WS-* name and break RequireClaim/actor
                    // lookups.
                    options.MapInboundClaims = false;

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = configuration["Jwt:Issuer"] ?? "sbqr",
                        ValidAudience = configuration["Jwt:Audience"] ?? "sbqr-api",
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(signingKey)),
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(1),
                    };
                });

            services.AddAuthorization(options =>
            {
                options.AddPolicy(PolicyNames.MobileJwt, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser());

                options.AddPolicy(PolicyNames.AdminCredentialTree, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser()
                     .RequireClaim("scope", "admin"));

                options.AddPolicy(PolicyNames.KeyAdmin, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser()
                     .RequireClaim("scope", "admin", "key-admin"));

                options.AddPolicy(PolicyNames.Signer, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser()
                     .RequireClaim("scope", "admin", "signer"));

                // Tenant FI client credentials carry these scopes from
                // token mint time — the server-to-server plane for the
                // QR endpoints (Sep-17 scope; mobile plane per blockers
                // B1/B2 lands post-deadline). The strings are platform
                // literals because both ends must agree on the spelling;
                // whether a tenant gets either scope is gated by its
                // TenantConfiguration.IsQrGenerationAllowed /
                // IsQrValidationAllowed flags, evaluated by the handler
                // at mint time (see
                // IssueClientCredentialsTokenCommandHandler).
                options.AddPolicy(PolicyNames.QrGenerate, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser()
                     .RequireClaim("scope", "qr:generate"));

                options.AddPolicy(PolicyNames.QrValidate, p =>
                    p.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
                     .RequireAuthenticatedUser()
                     .RequireClaim("scope", "qr:validate"));
            });
        }
    }
}
