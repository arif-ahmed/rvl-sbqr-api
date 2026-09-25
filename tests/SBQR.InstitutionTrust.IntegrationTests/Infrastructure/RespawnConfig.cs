using Npgsql;
using Respawn;

namespace SBQR.InstitutionTrust.IntegrationTests.Infrastructure;

internal static class RespawnConfig
{
    public static async Task<Respawner> BuildAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        return await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // Single-schema layout (db/migrations/001_module_schemas.sql +
            // README): every SBQR table lives in `public`. public.audit_logs
            // is added to TablesToIgnore below — the audit log is
            // append-only (immutability trigger + INSERT-only grants), so
            // Respawn must never issue DELETEs against it.
            SchemasToInclude =
            [
                "public",
            ],
            TablesToIgnore = new[]
            {
                new Respawn.Graph.Table("audit_logs"),
            },
        }).ConfigureAwait(false);
    }
}
