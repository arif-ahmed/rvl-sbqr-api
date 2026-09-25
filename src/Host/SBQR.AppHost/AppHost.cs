var builder = DistributedApplication.CreateBuilder(args);

// ----------------------------------------------------------------------------
// Local-only secrets, read from THIS project's user-secrets (never checked
// into git). Set with, e.g.:
//   dotnet user-secrets set "Parameters:storage-access-key-id" "AKIA..." --project src/Host/SBQR.AppHost
// A missing value falls back to "" — the same behavior docker/docker-compose.yml
// uses via ${VAR:-}, so the API still boots without them; only S3-backed and
// the OAuth2 bootstrap-token endpoints need them filled in.
// See docs/aspire-local-dev-guide.md for the full list and docs/dev-s3-guide.md
// for where the S3 values come from.
// ----------------------------------------------------------------------------
string Secret(string key) => builder.Configuration[$"Parameters:{key}"] ?? "";

// ----------------------------------------------------------------------------
// PostgreSQL — one container, two logical databases, mirroring
// docker/postgres/init/00-create-databases.sql. Aspire resource names must
// be letters/digits/hyphens only, so the (hyphenated) resource name and the
// actual Postgres database name differ; ConnectionStrings__* env vars below
// are set explicitly to match what SBQR.Api expects, rather than relying on
// WithReference's default ConnectionStrings__<resource-name> naming
// (see https://aka.ms/aspire/diagnostics/ASPIRE006).
// ----------------------------------------------------------------------------
var postgres = builder.AddPostgres("sbqr-postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var appDb = postgres.AddDatabase("sbqr-app", databaseName: "sbqr_app");
var keyVaultDb = postgres.AddDatabase("sbqr-key-vault", databaseName: "sbqr_key_vault");

// The mock's OWN disposable database (see BB.TrustStoreMock/README.md).
// Dev/test-only; dropped together with the mock when the real BB endpoint lands.
// NOTE: the resource name must differ from the "bb-trust-store-mock" PROJECT
// below — Aspire requires unique names across ALL resource types.
var mockDb = postgres.AddDatabase("bb-trust-store-mock-db", databaseName: "bb_trust_store_mock");

// ----------------------------------------------------------------------------
// BB.TrustStoreMock — in-repo stand-in for the real Bangladesh Bank trust
// store (spec Annex B). Simulates an EXTERNAL system: never referenced by
// SBQR.Api, refuses to run in Production. TrustStore__BaseUrl below points
// at it; override with `Parameters:trust-store-base-url` user-secret to aim
// at the real BB endpoint instead.
// ----------------------------------------------------------------------------
var bbMock = builder.AddProject<Projects.BB_TrustStoreMock>("bb-trust-store-mock")
    .WithEnvironment("ConnectionStrings__bb_trust_store_mock", mockDb.Resource.ConnectionStringExpression)
    .WaitFor(mockDb)
    .WithHttpHealthCheck("/health");

// ----------------------------------------------------------------------------
// SBQR.Api — the host. Env vars below mirror docker/docker-compose.yml's
// sbqr.api service so the two orchestrators stay in parity. The non-secret
// dev-only defaults here (Jwt signing key, bootstrap client id, ...) are the
// SAME publicly-committed values docker-compose.yml already uses — not real
// secrets, and explicitly rejected outside Development by Program.cs.
//
// TrustStore__BaseUrl points at the in-repo mock above (Aspire resolves the
// endpoint at runtime). Set Parameters:trust-store-base-url user-secret to
// aim at the real BB endpoint instead.
// ----------------------------------------------------------------------------
var trustStoreOverride = builder.Configuration["Parameters:trust-store-base-url"];

var api = builder.AddProject<Projects.SBQR_Api>("sbqr-api")
    .WithEnvironment("ConnectionStrings__sbqr_app", appDb.Resource.ConnectionStringExpression)
    .WithEnvironment("ConnectionStrings__sbqr_key_vault", keyVaultDb.Resource.ConnectionStringExpression)
    .WaitFor(appDb)
    .WaitFor(keyVaultDb)
    .WaitFor(bbMock)
    .WithHttpHealthCheck("/health/live")
    .WithEnvironment("KeyCustody__ActiveProvider", "PlainFile");

if (string.IsNullOrWhiteSpace(trustStoreOverride))
    api.WithEnvironment("TrustStore__BaseUrl", bbMock.GetEndpoint("http"));
else
    api.WithEnvironment("TrustStore__BaseUrl", trustStoreOverride);

api.WithEnvironment("TrustStore__SyncOnStartup", "true")
    .WithEnvironment("TrustStore__SyncIntervalMinutes", "1")
    .WithEnvironment("Jwt__Issuer", "sbqr")
    .WithEnvironment("Jwt__Audience", "sbqr-api")
    .WithEnvironment("Jwt__AccessTokenTtlMinutes", "10")
    .WithEnvironment("Jwt__SigningKey", "dev-only-signing-key-change-me-0123456789abcdef")
    .WithEnvironment("Auth__Bootstrap__ClientId", "platform-bootstrap")
    .WithEnvironment("Auth__Bootstrap__ClientSecretHash", Secret("bootstrap-client-secret-hash"))
    .WithEnvironment("Storage__Provider", "S3")
    .WithEnvironment("Storage__Region", "us-east-1")
    .WithEnvironment("Storage__VaultFolder", "keycustody")
    .WithEnvironment("Storage__ServiceUrl", Secret("storage-service-url"))
    .WithEnvironment("Storage__AccessKeyId", Secret("storage-access-key-id"))
    .WithEnvironment("Storage__SecretAccessKey", Secret("storage-secret-access-key"))
    .WithEnvironment("Storage__BucketName", Secret("storage-bucket-name"));

builder.Build().Run();
