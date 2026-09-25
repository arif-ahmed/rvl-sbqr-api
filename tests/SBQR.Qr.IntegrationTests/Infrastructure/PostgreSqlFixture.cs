using Npgsql;
using Respawn;
using Testcontainers.PostgreSql;
using Xunit;

namespace SBQR.Qr.IntegrationTests.Infrastructure;

/// <summary>
/// Single ephemeral PostgreSQL 16 container for the assembly: starts,
/// applies the canonical <c>db/migrations/*.sql</c> chain in lexicographic
/// order (the fixture plays the external migration tool — the application
/// itself never migrates), and captures a Respawn checkpoint for per-test
/// FK-safe resets. (Same lifecycle contract as the Tenancy integration
/// fixture.)
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

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);

        await ApplyCanonicalMigrationsAsync().ConfigureAwait(false);

        _respawn = await RespawnConfig.BuildAsync(ConnectionString).ConfigureAwait(false);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

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
            "SELECT to_regclass('public.qr_generations')::text;", conn);
        var result = await probe.ExecuteScalarAsync().ConfigureAwait(false);

        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException(
                "The canonical db/migrations chain did not produce " +
                "public.qr_generations on the test container — the " +
                "migration scripts are broken.");
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
