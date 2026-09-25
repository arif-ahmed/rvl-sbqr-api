using Npgsql;
using Respawn;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// Builds and caches a Respawn checkpoint for a PostgreSQL connection string.
/// Respawn performs an FK-safe DELETE-only reset of every user table so each
/// test starts from a clean slate without dropping/recreating the schema
/// (the schema is owned by the external migration tool; only the data is
/// reset).
/// </summary>
internal static class RespawnConfig
{
    /// <summary>
    /// Tables Respawn must NEVER touch. The list is comma-separated so it's
    /// easy to grep; the consumer splits on commas.
    /// <list type="bullet">
    ///   <item><c>public.audit_logs</c> — append-only audit log (C8:
    ///         immutability trigger + INSERT-only grants). Respawn must
    ///         never DELETE from it; the test assertions still see the
    ///         rows written by previous tests but accept them as
    ///         accumulating history.</item>
    ///   <item><c>__schema_migrations</c> — legacy name some teams use; kept
    ///         here defensively even though this codebase uses the external
    ///         migration tool's own bookkeeping table name.</item>
    ///   <item><c>__EFMigrationsHistory</c> — EF Core's own history table;
    ///         not used by this codebase (EF migrations are forbidden), but if
    ///         a future module brings in EF Core code-first for any reason
    ///         this guard keeps Respawn from deleting that history.</item>
    /// </list>
    /// </summary>
    private const string TablesToIgnoreCsv =
        "audit_logs,__schema_migrations,__EFMigrationsHistory";

    /// <summary>
    /// Build a Respawn checkpoint by sampling the live schema (called once
    /// after the migrations have run). Subsequent
    /// <see cref="Respawner.ResetAsync(string)"/> calls DELETE every row in
    /// every user table (preserving the schema, FKs, indexes, and check
    /// constraints) so each test starts from a known state.
    /// </summary>
    /// <param name="connectionString">Npgsql-format connection string for the
    /// ephemeral test database.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<Respawner> BuildAsync(string connectionString, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        // Respawn needs an open connection while building the checkpoint to
        // walk the catalog. We open + close it ourselves so Respawn can do
        // its discovery on the live schema.
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
            TablesToIgnore = TablesToIgnoreCsv
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => new Respawn.Graph.Table(name))
                .ToArray(),
            WithReseed = true,
        };

        return await Respawner.CreateAsync(conn, options).ConfigureAwait(false);
    }
}
