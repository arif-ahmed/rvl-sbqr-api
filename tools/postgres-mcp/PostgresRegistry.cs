// Named-connection registry: resolves agent-facing connection names to
// Npgsql connections and enforces the local-host-only startup gate.
// Hostnames (never connection strings) are the only thing that leaves here.

using Npgsql;

namespace SBQR.PostgresMcp;

internal sealed class PostgresRegistry(PostgresMcpOptions options)
{
    private static readonly HashSet<string> LocalHosts =
        new(["localhost", "127.0.0.1", "::1"], StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> Names => options.Connections.Keys.ToList().AsReadOnly();

    public bool AllowWrites => options.AllowWrites;

    public int MaxRows => options.MaxRows;

    public int TimeoutSeconds => options.TimeoutSeconds;

    /// <summary>Startup gate: every configured host must be local (C20, dev-only tool).</summary>
    public void EnsureLocalHostsOnly()
    {
        foreach (var (name, connectionString) in options.Connections)
        {
            string host;
            try
            {
                host = new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Connection '{name}' is not a valid Npgsql connection string: {ex.Message}");
            }

            if (!LocalHosts.Contains(host) && !options.AllowedHosts.Contains(host))
            {
                throw new InvalidOperationException(
                    $"Connection '{name}' targets host '{host}', which is not local. " +
                    "This dev-only server refuses non-local hosts. " +
                    "Add the host to POSTGRES_MCP_ALLOWED_HOSTS if you really mean it.");
            }
        }
    }

    public string Resolve(string? name)
    {
        var key = string.IsNullOrWhiteSpace(name) ? options.Connections.Keys.FirstOrDefault() : name;
        if (key is not null && options.Connections.TryGetValue(key, out var connectionString))
        {
            return connectionString;
        }

        var available = options.Connections.Count == 0
            ? "(none configured)"
            : string.Join(", ", options.Connections.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        throw new KeyNotFoundException(
            $"Unknown connection '{name ?? "(default)"}'. Available: {available}.");
    }

    public async Task<NpgsqlConnection> OpenAsync(string? name, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(Resolve(name));
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public string Describe(string name)
    {
        var builder = new NpgsqlConnectionStringBuilder(options.Connections[name]);
        return $"Host={builder.Host};Database={builder.Database};Username={builder.Username}";
    }
}
