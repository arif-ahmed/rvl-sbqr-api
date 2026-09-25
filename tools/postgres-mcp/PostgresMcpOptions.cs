// Configuration for the local PostgreSQL MCP server.
// Sources (lowest to highest precedence): .env file, real environment.
// Connection strings never appear in tool output or logs (C19).

namespace SBQR.PostgresMcp;

internal sealed class PostgresMcpOptions
{
    private const string ConnectionPrefix = "ConnectionStrings__";
    private const int DefaultMaxRows = 100;
    private const int MaxRowsCeiling = 1000;
    private const int DefaultTimeoutSeconds = 30;
    private const int TimeoutCeilingSeconds = 600;

    public Dictionary<string, string> Connections { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool AllowWrites { get; private set; }

    public int MaxRows { get; private set; } = DefaultMaxRows;

    public int TimeoutSeconds { get; private set; } = DefaultTimeoutSeconds;

    public HashSet<string> AllowedHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string EnvironmentName { get; private set; } = "Development";

    public static PostgresMcpOptions Load()
    {
        var fileVars = LoadEnvFile();
        string? Lookup(string key) =>
            Environment.GetEnvironmentVariable(key) is { Length: > 0 } live
                ? live
                : fileVars.TryGetValue(key, out var fileValue) && fileValue.Length > 0 ? fileValue : null;

        var options = new PostgresMcpOptions
        {
            EnvironmentName =
                Lookup("ASPNETCORE_ENVIRONMENT")
                ?? Lookup("DOTNET_ENVIRONMENT")
                ?? Lookup("POSTGRES_MCP_ENVIRONMENT")
                ?? "Development",
        };

        // Every ConnectionStrings__* key becomes a named connection. Real
        // environment variables win; the file also contributes names that
        // exist only on disk (e.g. a developer's local .env).
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in fileVars.Keys)
        {
            if (key.StartsWith(ConnectionPrefix, StringComparison.Ordinal) &&
                key.Length > ConnectionPrefix.Length)
            {
                names.Add(key[ConnectionPrefix.Length..]);
            }
        }

        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string key &&
                key.StartsWith(ConnectionPrefix, StringComparison.Ordinal) &&
                key.Length > ConnectionPrefix.Length)
            {
                names.Add(key[ConnectionPrefix.Length..]);
            }
        }

        foreach (var name in names)
        {
            if (Lookup(ConnectionPrefix + name) is { } value)
            {
                options.Connections[name] = value;
            }
        }

        // DATABASE_URL is the standalone fallback (see repo-root .env):
        // exposed as "default", and used for "sbqr_app" when no explicit
        // ConnectionStrings__sbqr_app is configured.
        if (Lookup("DATABASE_URL") is { } databaseUrl)
        {
            options.Connections.TryAdd("default", databaseUrl);
            options.Connections.TryAdd("sbqr_app", databaseUrl);
        }

        // Npgsql's connection-string builder does not parse postgresql://
        // URLs, so normalize any URL-form values to Host=/Port=/... form.
        foreach (var name in options.Connections.Keys.ToList())
        {
            options.Connections[name] = NormalizeConnectionString(options.Connections[name]);
        }

        if (Lookup("POSTGRES_MCP_ALLOW_WRITES") is { } allowWritesRaw)
        {
            options.AllowWrites = allowWritesRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                allowWritesRaw == "1" ||
                allowWritesRaw.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        if (Lookup("POSTGRES_MCP_MAX_ROWS") is { } maxRowsRaw &&
            int.TryParse(maxRowsRaw, out var maxRows))
        {
            options.MaxRows = Math.Clamp(maxRows, 1, MaxRowsCeiling);
        }

        if (Lookup("POSTGRES_MCP_TIMEOUT_SECONDS") is { } timeoutRaw &&
            int.TryParse(timeoutRaw, out var timeout))
        {
            options.TimeoutSeconds = Math.Clamp(timeout, 1, TimeoutCeilingSeconds);
        }

        if (Lookup("POSTGRES_MCP_ALLOWED_HOSTS") is { } hostsRaw)
        {
            foreach (var host in hostsRaw.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = host.Trim();
                if (trimmed.Length > 0)
                {
                    options.AllowedHosts.Add(trimmed);
                }
            }
        }

        return options;
    }

    private static Dictionary<string, string> LoadEnvFile()
    {
        foreach (var path in CandidateEnvFiles())
        {
            if (File.Exists(path))
            {
                return DotEnv.ParseFile(path);
            }
        }

        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static IEnumerable<string> CandidateEnvFiles()
    {
        // Explicit path always wins when set.
        if (Environment.GetEnvironmentVariable("POSTGRES_MCP_ENV_FILE") is { Length: > 0 } explicitPath)
        {
            yield return explicitPath;
        }

        // Walk up from both the current directory and the app directory so
        // the server finds the repo-root .env whether it runs via
        // `dotnet run --project tools/postgres-mcp` or as a built dll.
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; depth < 6 && dir is not null; depth++, dir = dir.Parent)
            {
                yield return Path.Combine(dir.FullName, ".env");
            }
        }
    }

    // Accepts postgresql://user:pass@host:port/db?sslmode=x and rewrites it
    // to Npgsql key=value form. Key=value strings pass through untouched.
    // Returns the input unchanged when it is neither (validated at startup).
    private static string NormalizeConnectionString(string value)
    {
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value;
        }

        var parts = new List<string>();
        if (uri.Host.Length > 0)
        {
            parts.Add($"Host={uri.Host}");
        }

        if (!uri.IsDefaultPort && uri.Port > 0)
        {
            parts.Add($"Port={uri.Port}");
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
        {
            parts.Add($"Username={Uri.UnescapeDataString(userInfo[0])}");
        }

        if (userInfo.Length > 1 && userInfo[1].Length > 0)
        {
            parts.Add($"Password={Uri.UnescapeDataString(userInfo[1])}");
        }

        var database = uri.AbsolutePath.Trim('/');
        if (database.Length > 0)
        {
            // Database names never contain '/'; take the first segment.
            parts.Add($"Database={Uri.UnescapeDataString(database.Split('/')[0])}");
        }

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add($"SSL Mode={kv[1]}");
            }
        }

        return string.Join(";", parts);
    }
}
