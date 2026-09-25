// src/BB.TrustStoreMock/Program.cs
using BB.TrustStoreMock;
using BB.TrustStoreMock.Persistence;
using BB.TrustStoreMock.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Dev/test-only service: /admin/institutions is (by default) an
// unauthenticated write surface that can publish or revoke trusted
// institution keys. Refuse to run in Production rather than expose it
// (mirrors SBQR.Api's own Production guards around the dev-only PlainFile
// signing provider).
if (builder.Environment.IsProduction())
{
    throw new InvalidOperationException(
        "BB.TrustStoreMock is a dev/test-only mock of the Bangladesh Bank trust store " +
        "and exposes an admin surface (/admin/institutions) that can publish and revoke " +
        "trusted keys. It must never run in Production — point TrustStore:BaseUrl " +
        "at the real BB trust store instead.");
}

// The mock owns its own disposable database. C9: a REAL connection string
// never lives in tracked files — it arrives via docker-compose env vars or
// `dotnet user-secrets` (see scripts/dev-seed-user-secrets.*). In
// Development only, an unset value falls back to the documented disposable
// local-dev default (localhost:5432, postgres/postgres — the same value the
// tracked compose file and seed scripts carry), so plain `dotnet run` works
// against a running compose Postgres with zero setup. Any OTHER environment
// (Production already refused above) fails fast instead of guessing.
var connectionString = builder.Configuration.GetConnectionString("bb_trust_store_mock");
var usingDevFallback = false;
if (string.IsNullOrWhiteSpace(connectionString))
{
    if (builder.Environment.IsDevelopment())
    {
        connectionString =
            "Host=localhost;Port=5432;Database=bb_trust_store_mock;Username=postgres;Password=postgres";
        usingDevFallback = true;
    }
    else
    {
        throw new InvalidOperationException(
            "ConnectionStrings:bb_trust_store_mock is required (env ConnectionStrings__bb_trust_store_mock " +
            "or user-secrets — see scripts/dev-seed-user-secrets.ps1). The mock persists uploaded public " +
            "keys in its own disposable bb_trust_store_mock database.");
    }
}

builder.Services.AddControllers();
builder.Services.AddDbContext<TrustStoreDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddScoped<TrustStoreRepository>();
builder.Services.AddValidatorsFromAssemblyContaining<PublishPublicKeyRequest>();
builder.Services.AddEndpointsApiExplorer();
// House style (Directory.Packages.props): native Microsoft.AspNetCore.OpenApi
// + Scalar for the UI — never Swashbuckle. The mock has ONE anonymous
// document (no audience split); AV0029 fires on standalone AddOpenApi but
// the two-document workaround that justifies it elsewhere does not apply.
#pragma warning disable AV0029
builder.Services.AddOpenApi();
#pragma warning restore AV0029

var app = builder.Build();

// Schema bootstrap for the disposable dev database. Deliberate deviation
// from the platform's external-migrations rule (C20): this database is
// dev/test-only, lives nowhere but a developer machine, and is dropped
// together with the mock when the real BB trust store lands — migration
// ceremony would outlive the thing it migrates.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TrustStoreDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapOpenApi();
app.MapScalarApiReference(); // browsable contract doc at /scalar — the artifact to hand BB later

// Minimal liveness probe for platform health checks (Render, k8s, etc.).
// Intentionally NOT a readiness probe — the database EnsureCreated above
// means the service either runs (DB reachable) or it doesn't start.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers();

var connectionSource = usingDevFallback
    ? "Development fallback (localhost:5432, postgres/postgres — documented disposable default)"
    : "configured via env/user-secrets/appsettings.json";
MockLog.Started(app.Logger, connectionSource);

app.Run();

// Exposed for Microsoft.AspNetCore.Mvc.Testing's WebApplicationFactory<Program>.
public partial class Program
{
}

internal sealed partial class MockLog
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Information,
        Message = "BB.TrustStoreMock started. Trust store starts EMPTY — upload public keys via PUT /trust-store/institutions/{{id}}/public-key. " +
                  "Persistence: bb_trust_store_mock database. No auth (dev-only mock). " +
                  "Connection: {ConnectionSource}. Docs UI: /scalar.")]
    public static partial void Started(ILogger logger, string connectionSource);
}
