// SBQR.PostgresMcp — local-only stdio MCP server for the SBQR PostgreSQL
// databases (sbqr_app + sbqr_key_vault). Dev tool only: never deployed,
// never pointed at non-local hosts (startup gate below).
//
// Security model (see docs/dev-mcp-postgres.md):
//   - Local-host enforcement: hard-fail at startup on any non-local host.
//   - Read-only by default: pg_query runs in a READ ONLY transaction;
//     pg_execute refuses unless POSTGRES_MCP_ALLOW_WRITES=true.
//   - Write gate: ALLOW_WRITES=true combined with a prod/stage/release
//     environment name hard-fails at startup (C20 least privilege).
//   - Hygiene: connection strings never reach tool output or logs (C19);
//     errors carry SqlState + message only, no stack traces (C13).

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SBQR.PostgresMcp;

var options = PostgresMcpOptions.Load();

if (options.Connections.Count == 0)
{
    Console.Error.WriteLine(
        "SBQR.PostgresMcp: no connections configured. Set ConnectionStrings__<name> " +
        "or DATABASE_URL in .env (see tools/postgres-mcp/env.example) or the environment.");
    return 1;
}

var registry = new PostgresRegistry(options);
try
{
    registry.EnsureLocalHostsOnly();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"SBQR.PostgresMcp: refusing to start: {ex.Message}");
    return 1;
}

if (options.AllowWrites && IsProtectedEnvironment(options.EnvironmentName))
{
    Console.Error.WriteLine(
        $"SBQR.PostgresMcp: refusing to start: POSTGRES_MCP_ALLOW_WRITES=true is forbidden " +
        $"when the environment name is '{options.EnvironmentName}' (contains prod/stage/release, C20).");
    return 1;
}

var builder = Host.CreateApplicationBuilder(args);
// Quiet stderr logging: the stdio transport owns stdout, so keep logs off it
// and at Warning+ to avoid corrupting the MCP protocol stream.
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(registry);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync().ConfigureAwait(false);
return 0;

static bool IsProtectedEnvironment(string name) =>
    name.Contains("prod", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("stage", StringComparison.OrdinalIgnoreCase) ||
    name.Contains("release", StringComparison.OrdinalIgnoreCase);
