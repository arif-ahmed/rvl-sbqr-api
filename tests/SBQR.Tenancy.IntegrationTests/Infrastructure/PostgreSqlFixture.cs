using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit <see cref="IAsyncLifetime"/> fixture that owns a single ephemeral
/// PostgreSQL 16-alpine container for the entire test assembly. Lifecycle:
/// <list type="number">
///   <item><b>InitializeAsync</b> — starts the container, applies the
///         canonical <c>db/migrations/*.sql</c> chain in lexicographic order
///         (the application itself never runs migrations — the fixture plays
///         the external migration tool against the ephemeral container), and
///         captures a Respawn checkpoint over the live schema.</item>
///   <item><b>Per test</b> — <see cref="ResetAsync"/> DELETEs every row in every
///         user table (FK-safe) so the next test starts from a known state
///         without re-running the migrations.</item>
///   <item><b>DisposeAsync</b> — stops and removes the container.</item>
/// </list>
///
/// <para>
/// <b>Schema ownership:</b> the application deliberately does not bundle a
/// migration runner. The canonical SQL scripts under <c>db/migrations/</c>
/// are the single source of truth; this fixture is the only thing that
/// applies them in test runs, exactly the way the external tool would.
/// </para>
/// </summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container =
        new PostgreSqlBuilder("postgres:16-alpine")
            .WithDatabase("sbqr_app")
            .WithUsername("sbqr")
            .WithPassword("sbqr")
            .WithCleanUp(true)
            .Build();

    private Respawner _respawn = default!;

    /// <summary>
    /// Npgsql-format connection string for the ephemeral container. Stable
    /// across the lifetime of the fixture — Testcontainers reuses the
    /// running container.
    /// </summary>
    public string ConnectionString => _container.GetConnectionString();

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        // Apply the canonical migration chain, then prove it produced this
        // module's sentinel table.
        await ApplyCanonicalMigrationsAsync().ConfigureAwait(false);

        // Capture the Respawn checkpoint over the migrated schema.
        _respawn = await RespawnConfig.BuildAsync(ConnectionString).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync()
    {
        await _container.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// DELETE every row in every user table (FK-safe, preserving schema,
    /// indexes, check constraints). Call from every test's
    /// <c>InitializeAsync</c> before the test does anything else.
    /// </summary>
    public async Task ResetAsync()
    {
        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);
        await _respawn.ResetAsync(conn).ConfigureAwait(false);
    }

    private async Task ApplyCanonicalMigrationsAsync()
    {
        var migrationsDir = FindMigrationsDirectory();

        await using var conn = new NpgsqlConnection(ConnectionString);
        await conn.OpenAsync().ConfigureAwait(false);

        foreach (var script in Directory
                     .EnumerateFiles(migrationsDir, "*.sql")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var sql = await File.ReadAllTextAsync(script).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var probe = new NpgsqlCommand(
            "SELECT to_regclass('public.tenants')::text;", conn);
        var result = await probe.ExecuteScalarAsync().ConfigureAwait(false);

        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                "The canonical db/migrations chain did not produce public.tenants " +
                "on the test container — the migration scripts are broken.");
        }
    }

    /// <summary>
    /// Walk up from the test assembly's output directory until the repo root
    /// (identified by its <c>db/migrations</c> folder with at least one
    /// script) is found. Skips bin/obj trees: stale EMPTY db/migrations
    /// folders are known to appear under test bin directories and would
    /// otherwise short-circuit the walk.
    /// </summary>
    private static string FindMigrationsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var isBuildOutput = dir.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
                                dir.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");
            var candidate = Path.Combine(dir.FullName, "db", "migrations");
            if (!isBuildOutput && Directory.Exists(candidate) &&
                Directory.EnumerateFiles(candidate, "*.sql").Any())
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate db/migrations (with .sql scripts) walking up from {AppContext.BaseDirectory}.");
    }
}
