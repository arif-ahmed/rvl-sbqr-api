// MCP tools for local PostgreSQL inspection. Read path first: every
// catalog tool is safe by construction (parameterized, no raw SQL).
// pg_query adds a client-side read-only guard on top of a server-enforced
// READ ONLY transaction; pg_execute stays disabled unless the operator
// explicitly opts in via POSTGRES_MCP_ALLOW_WRITES (see Program startup gate).

using System.ComponentModel;
using System.Globalization;
using System.Text;
using ModelContextProtocol.Server;
using Npgsql;

namespace SBQR.PostgresMcp;

[McpServerToolType]
internal sealed class PostgresTools(PostgresRegistry registry)
{
    [McpServerTool(Name = "pg_list_connections")]
    [Description("List the configured PostgreSQL connections (names, hosts, write mode). " +
        "Pass a listed name as 'connection' to the other tools. Passwords are never shown.")]
    public string ListConnections()
    {
        var sb = new StringBuilder("| Name | Host | Database | User | Writes |");
        sb.AppendLine().AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var name in registry.Names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var builder = new NpgsqlConnectionStringBuilder(registry.Resolve(name));
                sb.Append(CultureInfo.InvariantCulture,
                    $"| `{name}` | {builder.Host} | {builder.Database} | {builder.Username} | " +
                    $"{(registry.AllowWrites ? "enabled" : "disabled")} |\n");
            }
            catch (Exception ex)
            {
                sb.Append(CultureInfo.InvariantCulture, $"| `{name}` | *unparsable: {ex.Message}* | | | |\n");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Name = "pg_list_databases")]
    [Description("List the databases on the cluster behind a connection.")]
    public async Task<string> ListDatabases(
        [Description("Connection name from pg_list_connections. Omit for the default connection.")]
        string? connection = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT datname AS database, pg_size_pretty(pg_database_size(datname)) AS size
            FROM pg_database WHERE datistemplate = false ORDER BY 1
            """;
        return await ExecuteReaderAsync(connection, sql, [], registry.MaxRows, cancellationToken)
            .ConfigureAwait(false);
    }

    [McpServerTool(Name = "pg_list_tables")]
    [Description("List tables with schema, estimated row count and size. " +
        "SBQR module schemas: tenancy, generation, verification, institution_trust, key_custody, audit.")]
    public async Task<string> ListTables(
        [Description("Connection name from pg_list_connections. Omit for the default connection.")]
        string? connection = null,
        [Description("Restrict to one schema (e.g. 'tenancy'). Omit for all user schemas.")]
        string? schema = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT n.nspname AS schema, c.relname AS table,
                   CASE WHEN c.reltuples < 0 THEN NULL ELSE c.reltuples::bigint END AS est_rows,
                   pg_size_pretty(pg_total_relation_size(c.oid)) AS size
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind IN ('r', 'p')
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND ($1 IS NULL OR n.nspname = $1)
            ORDER BY 1, 2
            """;
        return await ExecuteReaderAsync(
            connection, sql, [schema], registry.MaxRows, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "pg_describe_table")]
    [Description("Describe a table: columns (type, nullability, defaults), " +
        "PK/FK/UNIQUE/CHECK constraints and indexes.")]
    public async Task<string> DescribeTable(
        [Description("Table name, e.g. 'qr_generations'.")] string table,
        [Description("Connection name from pg_list_connections. Omit for the default connection.")]
        string? connection = null,
        [Description("Schema name, e.g. 'generation'. Omit to auto-resolve when unambiguous.")]
        string? schema = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedSchema = schema;
        if (string.IsNullOrWhiteSpace(resolvedSchema))
        {
            resolvedSchema = await ResolveSchemaAsync(connection, table, cancellationToken).ConfigureAwait(false);
            if (resolvedSchema is null)
            {
                return $"Table '{table}' not found in any user schema.";
            }
        }

        var columns = await ExecuteReaderAsync(connection, """
            SELECT column_name, data_type,
                   COALESCE(character_maximum_length::text,
                            numeric_precision::text, datetime_precision::text, '') AS length_precision,
                   is_nullable, COALESCE(column_default, '') AS default
            FROM information_schema.columns
            WHERE table_schema = $1 AND table_name = $2 ORDER BY ordinal_position
            """, [resolvedSchema, table], registry.MaxRows, cancellationToken).ConfigureAwait(false);

        var constraints = await ExecuteReaderAsync(connection, """
            SELECT con.conname AS constraint,
                   CASE con.contype WHEN 'p' THEN 'PRIMARY KEY' WHEN 'f' THEN 'FOREIGN KEY'
                        WHEN 'u' THEN 'UNIQUE' WHEN 'c' THEN 'CHECK' ELSE con.contype::text END AS type,
                   pg_get_constraintdef(con.oid) AS definition
            FROM pg_constraint con
            JOIN pg_class rel ON rel.oid = con.conrelid
            JOIN pg_namespace nsp ON nsp.oid = rel.relnamespace
            WHERE nsp.nspname = $1 AND rel.relname = $2 ORDER BY 1
            """, [resolvedSchema, table], registry.MaxRows, cancellationToken).ConfigureAwait(false);

        var indexes = await ExecuteReaderAsync(connection, """
            SELECT indexname AS index, indexdef AS definition
            FROM pg_indexes WHERE schemaname = $1 AND tablename = $2 ORDER BY 1
            """, [resolvedSchema, table], registry.MaxRows, cancellationToken).ConfigureAwait(false);

        return $"## {resolvedSchema}.{table} — columns\n\n{columns}\n\n" +
            $"## Constraints\n\n{constraints}\n\n## Indexes\n\n{indexes}";
    }

    [McpServerTool(Name = "pg_query")]
    [Description("Run a read-only SELECT/WITH/VALUES/TABLE/EXPLAIN query and get a markdown table. " +
        "Runs inside a READ ONLY transaction with a statement timeout. " +
        "Always filter by the tenant column (tenant_id/TenantId) where the table carries one (C5); " +
        "cross-tenant reads are an incident. Results are row-capped and long values truncated.")]
    public async Task<string> Query(
        [Description("Read-only SQL: SELECT, WITH, VALUES, TABLE or EXPLAIN. One statement only.")]
        string sql,
        [Description("Connection name from pg_list_connections. Omit for the default connection.")]
        string? connection = null,
        [Description("Max rows to return. Capped by the server limit.")]
        int? maxRows = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            SqlGuard.EnsureReadOnly(sql);
        }
        catch (Exception ex)
        {
            return $"Rejected: {ex.Message}";
        }

        var cap = maxRows is null ? registry.MaxRows : Math.Clamp(maxRows.Value, 1, registry.MaxRows);
        try
        {
            await using var conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var tx = await conn.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var setCmd = new NpgsqlCommand("SET TRANSACTION READ ONLY", conn, tx))
            {
                setCmd.CommandTimeout = registry.TimeoutSeconds;
                await setCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            // The reader MUST be disposed before ROLLBACK: Npgsql refuses to
            // run any command on a connector with an open reader.
            List<string> columns;
            List<IReadOnlyList<string?>> rows;
            int total;
            await using (var cmd = new NpgsqlCommand(sql, conn, tx) { CommandTimeout = registry.TimeoutSeconds })
            {
                await using (var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
                {
                    (columns, rows, total) = await ReadRowsAsync(reader, cap, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return MarkdownTable.Render(columns, rows, total, cap);
        }
        catch (Exception ex)
        {
            return FormatDbError(ex);
        }
    }

    [McpServerTool(Name = "pg_execute")]
    [Description("Run a write statement (INSERT/UPDATE/DELETE/DDL). DISABLED by default — " +
        "returns a refusal unless the server was started with POSTGRES_MCP_ALLOW_WRITES=true. " +
        "Prefer the external migration tool (db/migrations/*.sql) for schema changes.")]
    public async Task<string> Execute(
        [Description("Write SQL. One statement only.")]
        string sql,
        [Description("Connection name from pg_list_connections. Omit for the default connection.")]
        string? connection = null,
        CancellationToken cancellationToken = default)
    {
        if (!registry.AllowWrites)
        {
            return "Refused: pg_execute is disabled. Set POSTGRES_MCP_ALLOW_WRITES=true in .env, " +
                "rebuild and restart the client — and prefer db/migrations/*.sql for schema changes.";
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            return "Rejected: SQL must not be empty.";
        }

        try
        {
            SqlGuard.EnsureSingleStatement(sql);
        }
        catch (Exception ex)
        {
            return $"Rejected: {ex.Message}";
        }

        try
        {
            await using var conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = registry.TimeoutSeconds };
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (reader.FieldCount > 0)
            {
                // Statements with RETURNING produce rows — render them like pg_query.
                var (columns, rows, total) = await ReadRowsAsync(reader, registry.MaxRows, cancellationToken)
                    .ConfigureAwait(false);
                return MarkdownTable.Render(columns, rows, total, registry.MaxRows, "Write executed.");
            }

            var affected = reader.RecordsAffected;
            return $"OK — write executed ({(affected < 0 ? 0 : affected)} row(s) affected).";
        }
        catch (Exception ex)
        {
            return FormatDbError(ex);
        }
    }

    private async Task<string?> ResolveSchemaAsync(
        string? connection, string table, CancellationToken cancellationToken)
    {
        await using var conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var cmd = new NpgsqlCommand("""
            SELECT table_schema FROM information_schema.tables
            WHERE table_name = $1 AND table_schema NOT IN ('pg_catalog', 'information_schema')
            ORDER BY 1
            """, conn) { CommandTimeout = registry.TimeoutSeconds };
        cmd.Parameters.AddWithValue(table);
        var schemas = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            schemas.Add(reader.GetString(0));
        }

        return schemas.Count switch
        {
            1 => schemas[0],
            0 => null,
            _ => throw new InvalidOperationException(
                $"Table '{table}' exists in multiple schemas ({string.Join(", ", schemas)}); pass 'schema' explicitly."),
        };
    }

    private async Task<string> ExecuteReaderAsync(
        string? connection, string sql, object?[] parameters, int cap, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await registry.OpenAsync(connection, cancellationToken).ConfigureAwait(false);
            await using var cmd = new NpgsqlCommand(sql, conn) { CommandTimeout = registry.TimeoutSeconds };
            for (var i = 0; i < parameters.Length; i++)
            {
                cmd.Parameters.AddWithValue(parameters[i] ?? DBNull.Value);
            }

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var (columns, rows, total) = await ReadRowsAsync(reader, cap, cancellationToken).ConfigureAwait(false);
            return MarkdownTable.Render(columns, rows, total, cap);
        }
        catch (Exception ex)
        {
            return FormatDbError(ex);
        }
    }

    private static async Task<(List<string> Columns, List<IReadOnlyList<string?>> Rows, int Total)> ReadRowsAsync(
        NpgsqlDataReader reader, int cap, CancellationToken cancellationToken)
    {
        var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
        var rows = new List<IReadOnlyList<string?>>();
        var total = 0;
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            total++;
            if (rows.Count < cap)
            {
                var row = new string?[reader.FieldCount];
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[i] = await reader.IsDBNullAsync(i, cancellationToken).ConfigureAwait(false)
                        ? null
                        : reader.GetValue(i).ToString();
                }

                rows.Add(row);
            }
        }

        return (columns, rows, total);
    }

    // Error hygiene (C13): SqlState + message only. Connection strings,
    // stack traces and inner server detail stay out of tool output.
    private static string FormatDbError(Exception ex) => ex switch
    {
        PostgresException pg => $"PostgreSQL error ({pg.SqlState}): {pg.MessageText}",
        NpgsqlException npgsql => $"Database request failed: {npgsql.Message}",
        KeyNotFoundException => ex.Message,
        InvalidOperationException => ex.Message,
        ArgumentException => ex.Message,
        _ => $"Request failed: {ex.GetType().Name}: {ex.Message}",
    };
}
