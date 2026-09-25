using Npgsql;
using Respawn;

namespace SBQR.Qr.IntegrationTests.Infrastructure;

/// <summary>Respawn checkpoint builder (FK-safe DELETE-only reset).</summary>
internal static class RespawnConfig
{
    private static readonly Respawn.Graph.Table[] IgnoredTables = new[]
    {
        // public.audit_logs — append-only (C8: immutability trigger +
        // INSERT-only grants). Respawn must never DELETE from it.
        "audit_logs",
        // legacy bookkeeping guards (see tenancy RespawnConfig).
        "__schema_migrations",
        "__EFMigrationsHistory",
    }
    .Select(name => new Respawn.Graph.Table(name))
    .ToArray();

    public static async Task<Respawner> BuildAsync(string connectionString, CancellationToken ct = default)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct).ConfigureAwait(false);

        var options = new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            // Single-schema layout (db/migrations/001_module_schemas.sql +
            // README): every SBQR table lives in `public`. public.audit_logs
            // is added to TablesToIgnore below — the audit log is
            // append-only (immutability trigger + INSERT-only grants), so
            // Respawn must never issue DELETEs against it.
            SchemasToInclude = new[]
            {
                "public",
            },
            TablesToIgnore = IgnoredTables,
            WithReseed = true,
        };

        return await Respawner.CreateAsync(conn, options).ConfigureAwait(false);
    }
}
