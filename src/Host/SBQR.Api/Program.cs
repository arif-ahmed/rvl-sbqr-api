using System.Reflection;
using System.Security.Cryptography;
using Asp.Versioning;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.OpenApi;
using SBQR.Modules.Audit.Api;
using SBQR.Modules.IdentityAccess.Api;
using SBQR.Modules.InstitutionTrust.Api;
using SBQR.Modules.QrGeneration.Api;
using SBQR.Modules.KeyCustody.Api;
using SBQR.Modules.Tenancy.Api;
using SBQR.Modules.IdentityAccess.Infrastructure.Cryptography;
using SBQR.Modules.Verification.Api;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.Storage;
using SBQR.SharedKernel.Storage.S3;
using SBQR.Api.Infrastructure.OpenApi;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

// ----------------------------------------------------------------------------
// 0a. CLI: --generate-bootstrap-secret.
//
// One-shot operator utility for the OAuth2 bootstrap credential ("client
// zero" — the credential the platform itself uses to obtain an admin-scoped
// token for POST /v1/admin/tenants). Generates a 32-byte secret, prints it
// EXACTLY ONCE together with its Argon2id PHC hash, and exits. Copy the
// secret into the operator vault; copy the hash into configuration
// (Auth:Bootstrap:ClientSecretHash — env var / Key Vault in production,
// never committed).
// ----------------------------------------------------------------------------
if (args.Contains("--generate-bootstrap-secret", StringComparer.OrdinalIgnoreCase))
{
    var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .Replace('+', '-')
        .Replace('/', '_')
        .TrimEnd('=');
    var hash = new Argon2idSecretHasher().Hash(secret);

    Console.WriteLine("SBQR platform bootstrap credential");
    Console.WriteLine("----------------------------------");
    Console.WriteLine();
    Console.WriteLine("  client_id      : platform-bootstrap   (Auth:Bootstrap:ClientId default)");
    Console.WriteLine($"  client_secret  : {secret}");
    Console.WriteLine();
    Console.WriteLine("  STORE THE SECRET IN THE OPERATOR VAULT NOW — it is never shown again.");
    Console.WriteLine();
    Console.WriteLine("  Auth:Bootstrap:ClientSecretHash (put this in Key Vault / env var");
    Console.WriteLine($"  Auth__Bootstrap__ClientSecretHash): {hash}");
    Console.WriteLine();
    Console.WriteLine("  Token exchange:");
    Console.WriteLine("    curl -X POST http://localhost:5080/v1/oauth/token \\");
    Console.WriteLine("      -d grant_type=client_credentials");
    Console.WriteLine($"      -d client_id=platform-bootstrap -d client_secret={secret}");
    return 0;
}

// ----------------------------------------------------------------------------
// 0. (no CLI flags here — database schema is owned by the external migration
// tool, not by this host. See docs/docker/DEPLOYMENT.md for how migrations
// are run in each environment.)
// ----------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// 1. Configuration: appsettings.json + env vars (+ .env / user-secrets in dev).
//
//    Single-file convention: appsettings.json is the ONLY settings file in
//    every environment. Everything environment-specific is injected:
//      - Development (`dotnet run`): the gitignored repo-root `.env` —
//        primary, loaded LAST so it wins over everything below — and/or
//        `dotnet user-secrets` (seed with scripts/dev-seed-user-secrets.ps1/.sh).
//      - Local docker-compose: docker/.env — compose substitutes the values
//        into container environment variables; the app never reads that file.
//      - Deployed (Render/ACA/AKS): environment variables injected by the
//        orchestrator (ConnectionStrings__sbqr_app, Jwt__SigningKey,
//        Crypto__VaultProvider, ... — the same canonical `__` keys).
//    Precedence: .env (dev) > user-secrets (dev) > env vars > appsettings.json.
//    appsettings.{Environment}.json files are NOT loaded — do not add one.
// ----------------------------------------------------------------------------
builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();

    // Repo-root .env (optional, gitignored; template .env.example). Loaded
    // LAST so the file is the single source of truth for local overrides.
    // Keys use the canonical `__` env-var form (translated to `:` sections
    // in the parser below) — one vocabulary across .env, docker/.env and
    // orchestrator injection. Never loaded outside Development: a stray
    // .env next to a deployed binary must be inert.
    var contentRoot = builder.Environment.ContentRootPath;
    foreach (var candidate in new[]
             {
                 Path.Combine(contentRoot, "..", "..", "..", ".env"), // repo root (dotnet run)
                 Path.Combine(contentRoot, ".env") // fallback (published layout)
             })
    {
        if (!File.Exists(candidate)) continue;
        Console.WriteLine($"[dev] loading .env: {Path.GetFullPath(candidate)}");
        builder.Configuration.AddInMemoryCollection(ParseDotEnv(candidate));
        break;
    }
}

// Minimal .env parser: KEY=VALUE lines; blank lines and #-comments ignored;
// optional `export ` prefix; one matching pair of surrounding quotes is
// stripped; values are literal (no ${VAR} interpolation).
static IEnumerable<KeyValuePair<string, string?>> ParseDotEnv(string path)
{
    foreach (var rawLine in File.ReadAllLines(path))
    {
        var line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        if (line.StartsWith("export ", StringComparison.Ordinal))
        {
            line = line["export ".Length..].TrimStart();
        }

        var separator = line.IndexOf('=');
        if (separator <= 0) continue;

        var key = line[..separator].Trim();
        var value = line[(separator + 1)..].Trim();
        if (value.Length >= 2 &&
            (value.StartsWith('"') && value.EndsWith('"') ||
             value.StartsWith('\'') && value.EndsWith('\'')))
        {
            value = value[1..^1];
        }

        yield return new KeyValuePair<string, string?>(key.Replace("__", ":"), value);
    }
}

// ----------------------------------------------------------------------------
// 2. MediatR: register handlers from every module's handler assemblies.
//
//    Two assemblies matter per module: the assembly the module TYPE lives
//    in (single-project modules keep controllers + handlers together) and
//    its IModule.ApplicationPartAssembly — for the layered Tenancy module
//    that is SBQR.Modules.Tenancy.Application, where the command handlers,
//    validators, and mapping profile actually live (the module type and
//    controllers live in .Api). Scanning only the module-type assemblies
//    silently skipped them, blowing up at first Send() with "No service
//    for type IRequestHandler<...>".
// ----------------------------------------------------------------------------
var moduleTypes = new[]
{
    typeof(TenancyModule),
    typeof(InstitutionTrustModule),
    typeof(KeyCustodyModule),
    typeof(QrGenerationModule),
    typeof(VerificationModule),
    typeof(AuditModule),
    typeof(IdentityAccessModule),
};

var moduleInstances = moduleTypes
    .Where(t => typeof(IModule).IsAssignableFrom(t))
    .Select(t => (IModule)Activator.CreateInstance(t)!)
    .ToArray();

var handlerAssemblies = moduleTypes
    .Select(t => t.Assembly)
    .Concat(moduleInstances.Select(m => m.ApplicationPartAssembly))
    .Distinct()
    .ToArray();

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssemblies(handlerAssemblies);
});

// ----------------------------------------------------------------------------
// 3. FluentValidation: validators auto-discovered from the same assemblies
//    as the handlers.
// ----------------------------------------------------------------------------
foreach (var assembly in handlerAssemblies)
{
    builder.Services.AddValidatorsFromAssembly(assembly);
}

// ----------------------------------------------------------------------------
// 4. ValidationBehavior: the single MediatR pipeline behavior.
// ----------------------------------------------------------------------------
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// ----------------------------------------------------------------------------
// 5. AutoMapper: one profile per module, scanned across the same assemblies.
// ----------------------------------------------------------------------------
builder.Services.AddAutoMapper(
    cfg => cfg.AddMaps(handlerAssemblies));

// ----------------------------------------------------------------------------
// 5a. Object storage: one shared IObjectStorageFactory for every module.
//
//     Registered as a lazy singleton factory delegate — it is only built
//     (and only then validates Storage:Region/BucketName) the first time
//     some module actually resolves it, exactly like the module-local
//     provider pattern below (KeyCustody's S3KeyVaultProvider). A module
//     that never touches object storage never pays for or requires this
//     config block. Any module gets reuse the same way: resolve
//     IObjectStorageFactory from DI and call Create("<own-folder-name>").
// ----------------------------------------------------------------------------
builder.Services.AddSingleton<IObjectStorageFactory>(
    _ => new S3ObjectStorageFactory(builder.Configuration));

// ----------------------------------------------------------------------------
// 6. IModule composition: each module registers its own DbContext,
//    repositories, validators, etc. The QR payload codec is a pure library
//    inside the shared kernel (SBQR.SharedKernel.QrCodec) — no IModule, no
//    service registrations — so it is not part of this loop.
// ----------------------------------------------------------------------------
foreach (var module in moduleInstances)
{
    module.RegisterServices(builder.Services, builder.Configuration);
}

// ----------------------------------------------------------------------------
// 6a. Cryptographic-boundary guard.
//
// In Production, the only acceptable ISigningProvider implementations
// are PemVaultSigningProvider (Phase 1) and HsmSigningProvider (Phase 2).
// PlainFileSigningProvider is dev-only; the host refuses to start if it
// is the resolved provider in Production. See tactical-design.md §3 note
// on KeyCustody.
//
// Deferred until after app.Build() so we use the real service provider
// instead of calling BuildServiceProvider() on the builder (ASP0000).
// ----------------------------------------------------------------------------

// ----------------------------------------------------------------------------
// 7. Controllers: ApplicationParts from every module's assembly.
// ----------------------------------------------------------------------------
builder.Services
    .AddControllers(options =>
    {
        // Inject the request-scoped correlation_id into every ProblemDetails
        // response body (validation failures, 404, 415, etc.) so the value a
        // client quotes from a 4xx error joins back to audit_logs on
        // correlation_id. See ProblemDetailsCorrelationEnricher.
        options.Filters.Add<SBQR.Api.Infrastructure.ProblemDetailsCorrelationEnricher>();
    })
    .ConfigureApplicationPartManager(apm =>
    {
        foreach (var assembly in moduleTypes.Select(t => t.Assembly).Distinct())
        {
            apm.ApplicationParts.Add(new AssemblyPart(assembly));
        }
    })
    // Reject request bodies carrying unrecognized JSON properties (e.g. a
    // transactionAmount sent to /qr/generate/static) instead of silently
    // dropping them. Consistent with A6 — the client is untrusted, and a
    // field the DTO doesn't declare is a client error, not noise to ignore.
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.UnmappedMemberHandling =
            System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow;
    });

// ----------------------------------------------------------------------------
// 7c. API versioning: URL-segment only (/v1/...).
//
//     Every module controller route carries a leading v{version:apiVersion}
//     segment (e.g. /v1/oauth/token, /v1/qr/generate) and is decorated
//     [ApiVersion("1.0")]. Consequences (verified against a running
//     instance):
//       - A request WITHOUT a version segment (e.g. POST /v1/oauth/token) no
//         longer matches any controller route — plain 404.
//       - A request with an UNDECLARED segment (e.g. /v2/oauth/token) also
//         404s: the apiVersion route constraint only matches versions an
//         action declares, so no endpoint matches at all.
//       - ReportApiVersions stamps api-supported-versions on successful
//         versioned responses (e.g. 200 from /v1/oauth/token), so clients
//         can discover the supported set.
//     There is exactly one version today (1.0); a future v2 is additive —
//     new controllers/actions carry [ApiVersion("2.0")] and the v1 surface
//     stays where it is.
//
//     AddApiExplorer participates in ApiDescription generation so the two
//     OpenAPI documents (§8) render literal /v1/... paths instead of the raw
//     v{version} route token (SubstituteApiVersionInUrl). The public/internal
//     document split keyed on [ApiExplorerSettings(GroupName = "internal")]
//     is preserved — an explicit GroupName wins over the versioned format;
//     verified against a running instance (see §8 comments).
// ----------------------------------------------------------------------------
builder.Services
    .AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1, 0);
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
        options.ReportApiVersions = true;
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });

// 7a. IHttpContextAccessor — required by HttpContextActorProvider (Tenancy
//     module) and any other module that resolves the current request. The
//     TenancyModule doc says this is "registered globally by the host", so
//     we honor that contract here.
builder.Services.AddHttpContextAccessor();

// 7b. ICurrentTenant — the SharedKernel contract for "which tenant is this
//     request for?". Resolved per request from the JWT `tenant_id` claim
//     by JwtClaimCurrentTenant (see src/Host/SBQR.Api/Infrastructure/).
//     Bootstrap/admin tokens legitimately carry no `tenant_id` claim —
//     those resolve to Guid.Empty, which the QR-generation handler and
//     the signing provider both treat as "no tenant context, fail
//     closed". The legacy NoopCurrentTenant (always Guid.Empty) is kept
//     in the tree as a dev/test fallback; it is no longer wired here.
builder.Services.AddScoped<SBQR.SharedKernel.Application.ICurrentTenant,
    SBQR.Api.Infrastructure.JwtClaimCurrentTenant>();

// ----------------------------------------------------------------------------
// 8. OpenAPI: TWO documents.
//    Per docs/superpowers/specs/2026-09-04-public-vs-internal-api-docs-design.md,
//    controllers annotated [ApiExplorerSettings(GroupName = "internal")]
//    flow into v1.internal-admin; unannotated controllers (v1/oauth/token,
//    v1/qr/generate, v1/qr/validate, the two health endpoints) flow into
//    v1.public. The "v1" in the document names is the document series
//    version, independent of the URL-segment API versioning in §7c.
//
//    AV0029/AV0030 are suppressed deliberately. Asp.Versioning 10's own
//    OpenAPI bridge (Asp.Versioning.OpenApi: AddApiVersioning().AddOpenApi()
//    + MapOpenApi().WithDocumentPerVersion()) generates exactly ONE
//    auto-named document per API version (/openapi/v1.json) and has no
//    named-document overload, so it cannot express this repo's two-audience
//    split (public doc vs internal-admin doc). The named documents below
//    stay on the plain Microsoft.AspNetCore.OpenApi document services; the
//    versioned ApiExplorer (§7c) still feeds them version-substituted
//    ApiDescriptions, so paths render as literal /v1/... (verified against
//    a running instance). When a v2 API version lands, add v2.public /
//    v2.internal-admin documents here — the document name pins the version.
// ----------------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
#pragma warning disable AV0029 // Standalone AddOpenApi is intentional: named two-audience documents, not version-generated ones (see comment above).
builder.Services.AddOpenApi("v1.public", o =>
{
    o.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
    // ShouldInclude, not a post-hoc IOpenApiDocumentTransformer filtering
    // OpenApiOperation.Tags: Microsoft.AspNetCore.OpenApi's native
    // generator never turns ApiExplorerSettings.GroupName into an
    // operation tag, so a tag-based filter silently keeps/drops nothing
    // (verified against a running instance — see GroupNameDocumentFilter.cs
    // remarks). ApiDescription.GroupName is the one place GroupName is
    // reliably visible during document generation.
    o.ShouldInclude = apiDesc => apiDesc.GroupName != GroupNameDocumentFilter.InternalGroup;
});
builder.Services.AddOpenApi("v1.internal-admin", o =>
{
    o.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_0;
    o.ShouldInclude = apiDesc => apiDesc.GroupName == GroupNameDocumentFilter.InternalGroup;
});
#pragma warning restore AV0029

// ----------------------------------------------------------------------------
// 9. Authentication + Authorization: registered by IdentityAccessModule.
//    The host just adds the middleware here.
// ----------------------------------------------------------------------------

// ----------------------------------------------------------------------------
// 9a. Rate limiting: fixed window on the OAuth2 token endpoint
//     (POST /v1/oauth/token) — blunts client_secret brute force.
//
//     FR-AUTH-001 §6 BR5 / AC6 (security review B3): the partition key is
//     "{client_id}|{remoteIp}", NOT just {remoteIp}. A per-IP-only key
//     means an attacker with a /24 of IPs gets PermitLimit × 256
//     attempts per minute against a single client_id — Argon2id's
//     per-verify cost is the only floor. Partitioning on client_id
//     collapses that ceiling back to PermitLimit per client_id × per IP.
//
//     Because the rate limiter runs before the controller model binder,
//     we EnableBuffering() on the request and rewind the body after
//     reading the client_id, so the form binder still sees the raw
//     bytes. We synchronously block on a small async form read inside
//     the callback; for the 4 KiB max form size accepted by the
//     controller (FluentValidation caps ClientSecret at 512 chars) this
//     is negligible.
//
//     Partition key extraction MUST be safe under body tampering:
//     malformed body / missing client_id / over-long client_id falls
//     back to per-IP-only (the same protection the pre-fix version
//     offered) — never throws, never blocks legitimate clients.
//
//     Per remote IP, configurable requests-per-minute via
//     RateLimit:TokenEndpoint (PermitLimit, WindowSeconds). Defaults:
//     PermitLimit=10, WindowSeconds=60 — preserves the pre-configurability
//     behaviour.
//
//     Applied via [EnableRateLimiting(RateLimitPolicyNames.TokenEndpoint)] on
//     the controller.
// ----------------------------------------------------------------------------
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(RateLimitPolicyNames.TokenEndpoint, context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var clientId = SBQR.Api.Infrastructure.RateLimitClientIdExtractor.TryExtract(context);
        var partitionKey = string.IsNullOrEmpty(clientId)
            ? $"ip-only|{ip}"
            : $"client|{clientId}|{ip}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: partitionKey,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:TokenEndpoint:PermitLimit", 10),
                Window = TimeSpan.FromSeconds(
                    builder.Configuration.GetValue("RateLimit:TokenEndpoint:WindowSeconds", 60)),
                QueueLimit = 0,
            });
    });
});

// ----------------------------------------------------------------------------
// 10. Health endpoints (epic-1 Story 11).
// ----------------------------------------------------------------------------
var app = builder.Build();

// 10a. Cryptographic-boundary guards (deferred from startup composition).
//
//      (a) The dev-only PlainFile signing provider must not be the resolved
//          ISigningProvider in Production.
//      (b) Auth:Bootstrap:ClientSecretHash (Argon2id PHC) is the ONLY
//          acceptable bootstrap-secret shape across every environment
//          (Local / Development / Staging / RC / Production). C3/C9: a
//          plaintext credential cannot reach the process — the property
//          was removed from PlatformBootstrapOptions.
//      (c) Jwt:SigningKey must be present in Production (IdentityAccessModule
//          already refuses to boot without it in ANY environment, so this is
//          defense in depth with a production-specific message).
    //      (d) Crypto:VaultProvider must resolve to a known IKeyVaultProvider in
    //          Production — unrecognised names refuse to boot rather than
    //          silently fall through to a soft default. Keep this list in
    //          sync with the provider switch in KeyCustodyModule
    //          (LocalKeyVaultProvider / S3KeyVaultProvider).
    // ----------------------------------------------------------------------------
    if (app.Environment.IsProduction())
    {
        var provider = app.Services.GetRequiredService<ISigningProvider>();
        if (provider.ProviderId.StartsWith("plain-file", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"ISigningProvider '{provider.ProviderId}' is not permitted in Production. " +
                "Set KeyCustody:ActiveProvider to PemVault or Hsm.");
        }

        if (string.IsNullOrWhiteSpace(app.Configuration["Jwt:SigningKey"]))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is required in Production (32+ chars, from Key Vault).");
        }

        // B4 — explicit denylist of the literal dev-only signing key. The
        // presence check above would happily boot if a copy/paste mistake
        // pulled the dev key out of scripts/dev-seed-user-secrets.sh or
        // docker-compose.yml into Production config. Refuse to boot so the
        // mistake surfaces at startup, not at the first forged token.
        if (DevSigningKeyGuard.IsDevOnlyKey(app.Configuration["Jwt:SigningKey"]))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is set to the literal dev-only value. Production must use a " +
                "unique signing key from Key Vault, never the committed dev key.");
        }

        var vaultProviderName = app.Configuration["Crypto:VaultProvider"];
        var isKnownVaultProvider =
            string.Equals(vaultProviderName, "Local", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(vaultProviderName, "S3", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(vaultProviderName) || !isKnownVaultProvider)
        {
            throw new InvalidOperationException(
                $"Crypto:VaultProvider '{vaultProviderName ?? "<unset>"}' is not permitted in Production " +
                "(or is unrecognised). Supported values: Local, S3. Additional providers (Azure, AWS, HSM) " +
                "ship as additional IKeyVaultProvider implementations.");
        }
    }

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
   .AllowAnonymous();

app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }))
   .AllowAnonymous();

app.MapGet("/", () => Results.Redirect("/openapi/v1.public.json"))
   .AllowAnonymous();

// ----------------------------------------------------------------------------
// 10b. Dev-only route manifest.
//
// A ground-truth complement to the two OpenAPI documents: dumps the live
// `EndpointDataSource.Endpoints` (controllers included) exactly as MVC
// actually registered them, independent of ApiExplorer/OpenAPI grouping —
// useful for confirming a route is really live when its OpenAPI document
// placement is in question. See GroupNameDocumentFilter.cs remarks for the
// GroupName-vs-ShouldInclude pitfall this caught during development.
//
// DEV-ONLY: gated by `app.Environment.IsDevelopment()` AND `.AllowAnonymous()`,
// so it can never leak a route inventory past the auth pipeline in
// non-development environments. Removed from the production payload entirely.
// ----------------------------------------------------------------------------
if (app.Environment.IsDevelopment())
{
    app.MapGet("/admin/_routes", (EndpointDataSource dataSource) =>
    {
        var routes = dataSource.Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<MethodInfo>() is not null
                        || e.Metadata.GetMetadata<HttpMethodMetadata>() is not null)
            .Select(e => new
            {
                Pattern = e.RoutePattern.RawText,
                HttpMethods = e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods
                    ?? (e.Metadata.GetMetadata<MethodInfo>() is { } mi
                        ? new[] { mi.Name }
                        : Array.Empty<string>()),
                Controller = e.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>()?.ControllerName,
                EndpointDisplayName = e.DisplayName,
            })
            .OrderBy(r => r.Pattern)
            .ThenBy(r => string.Join(",", r.HttpMethods))
            .ToArray();

        return Results.Ok(new
        {
            environment = app.Environment.EnvironmentName,
            total = routes.Length,
            routes,
        });
    })
    .AllowAnonymous()
    .ExcludeFromDescription();
}

// ----------------------------------------------------------------------------
// 11. OpenAPI endpoints + Scalar UI.
//
//     Both documents and their Scalar UI pages are anonymous in every
//     environment (the Basic Auth docs gate was deprecated and removed).
//
//     Each route keeps the "{documentName}.json" shape (rather than a
//     fixed literal path) because MapOpenApi resolves the document name
//     from the ROUTE PARAMETER capture, not by re-parsing the matched
//     URL string — a literal "/openapi/v1.public.json" route makes it
//     look for a document literally named "v1" (split on the first '.')
//     and 404. A regex route constraint pins each route to exactly one
//     document name while still using parameter capture.
//
//     AV0030 (WithDocumentPerVersion) is suppressed for the same reason as
//     AV0029 in §8: these endpoints serve the two hand-named audience
//     documents, not Asp.Versioning's auto-generated one-document-per-
//     version set.
// ----------------------------------------------------------------------------
#pragma warning disable AV0030 // Hand-named audience documents, not auto-generated per-version ones (see §8 comment).
app.MapOpenApi("/openapi/{documentName:regex(^v1\\.public$)}.json").AllowAnonymous();
app.MapOpenApi("/openapi/{documentName:regex(^v1\\.internal-admin$)}.json").AllowAnonymous();
#pragma warning restore AV0030

app.MapScalarApiReference("/docs/public", options =>
    options.WithOpenApiRoutePattern("/openapi/v1.public.json"))
   .AllowAnonymous();

app.MapScalarApiReference("/docs/internal-admin", options =>
    options.WithOpenApiRoutePattern("/openapi/v1.internal-admin.json"))
   .AllowAnonymous();

// ----------------------------------------------------------------------------
// 11b. Correlation id — first middleware so every response (including 401 /
//      400 / 500) carries an X-Correlation-Id header, and every audit row
//      written downstream shares the same id for the lifetime of this HTTP
//      request. Inbound header is honoured when the caller supplies a GUID,
//      otherwise one is minted. See CorrelationIdMiddleware.
// ----------------------------------------------------------------------------
app.UseMiddleware<SBQR.Api.Infrastructure.CorrelationIdMiddleware>();

// ----------------------------------------------------------------------------
// 12. Authentication / Authorization / Rate-limiting middleware (in this order).
// ----------------------------------------------------------------------------
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// ----------------------------------------------------------------------------
// 13. Controllers.
// ----------------------------------------------------------------------------
app.MapControllers();

app.Run();

return 0;

// ----------------------------------------------------------------------------
// Placeholder anchor removed: the QR codec now lives inside the shared
// kernel (SBQR.SharedKernel.QrCodec), which every module already loads;
// it has no IModule, handlers, or validators, so no assembly anchor is
// needed in the scan lists above.
// ----------------------------------------------------------------------------

/// <summary>
/// Marker partial so WebApplicationFactory<Program> in integration tests
/// can pick up the entry point assembly.
/// </summary>
public partial class Program
{
}
