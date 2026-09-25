using Npgsql;

namespace SBQR.Tenancy.IntegrationTests.Schema;

/// <summary>
/// Reads the live PostgreSQL schema (one ephemeral Testcontainers container,
/// migrated by <c>PostgreSqlFixture</c> from the canonical
/// <c>db/migrations/*.sql</c>) and returns a normalized descriptor of every
/// table that <see cref="SchemaModelDriftTests"/> cares about.
///
/// <para>
/// Implementation notes:
/// <list type="bullet">
///   <item>Tables are read across the module schemas (one schema per module
///         — see db/migrations/001_module_schemas.sql); each table is
///         reported schema-qualified as <c>schema.table</c> so the EF side
///         (which declares the same schema via <c>ToTable(name, schema)</c>)
///         compares equal.</item>
///   <item>Catalog reads use <c>information_schema</c> + <c>pg_catalog</c>;
///         <c>pg_catalog</c> for indexes and CHECK constraints because
///         <c>information_schema.check_constraints</c> omits inline
///         column-level CHECKs.</item>
///   <item>PostgreSQL system columns (<c>xmin</c>, <c>tableoid</c>, <c>oid</c>,
///         <c>cmin</c>, <c>cmax</c>, <c>ctid</c>) are filtered out.</item>
///   <item>Migration bookkeeping tables (<c>__schema_migrations</c>,
///         <c>__EFMigrationsHistory</c>) are also
///         filtered out — they are not part of the application schema.</item>
/// </list>
/// </para>
/// </summary>
internal static class PostgresSchemaReader
{
    /// <summary>
    /// The schemas to enumerate. The canonical single-schema layout
    /// (<c>db/migrations/README.md</c>) puts every SBQR table in
    /// <c>public</c>, so this list is just <c>["public"]</c>. Respawn
    /// deliberately never resets <c>public.audit_logs</c> — the audit log
    /// is append-only (immutability trigger + INSERT-only grants).
    /// </summary>
    private static readonly string[] Schemas =
    {
        "public",
    };

    /// <summary>
    /// Tables the drift test must never read. Same list as
    /// <c>RespawnConfig.TablesToIgnoreCsv</c> to keep reset / drift in lockstep.
    /// </summary>
    private static readonly string[] IgnoredTables =
    {
        "__schema_migrations",
        "__EFMigrationsHistory",
    };

    /// <summary>
    /// Read the schema of every user table across the module schemas,
    /// reporting each as <c>schema.table</c>.
    /// </summary>
    public static async Task<IReadOnlyList<SchemaDescriptor.Table>> ReadAsync(
        string connectionString,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        var tableNames = await ReadTableNamesAsync(conn, ct);
        var tables = new List<SchemaDescriptor.Table>(tableNames.Count);
        foreach (var qualifiedName in tableNames)
        {
            tables.Add(await ReadTableAsync(conn, qualifiedName, ct));
        }

        return tables;
    }

    private static async Task<List<string>> ReadTableNamesAsync(
        NpgsqlConnection conn, CancellationToken ct)
    {
        const string Sql = @"
            SELECT table_schema || '.' || table_name
            FROM information_schema.tables
            WHERE table_schema = ANY(@schemas)
              AND table_type = 'BASE TABLE'
              AND table_name <> ALL(@ignored)
            ORDER BY table_schema, table_name;";

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schemas", Schemas);
        cmd.Parameters.AddWithValue("ignored", IgnoredTables);

        var names = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                names.Add(reader.GetString(0));
            }
        }

        return names;
    }

    private static async Task<SchemaDescriptor.Table> ReadTableAsync(
        NpgsqlConnection conn, string qualifiedName, CancellationToken ct)
    {
        var parts = qualifiedName.Split('.', 2);
        var schema = parts[0];
        var tableName = parts[1];

        // Npgsql 10 forbids concurrent commands on one connection — each
        // ExecuteReader must complete before the next can start. Run the
        // four catalog reads sequentially (they're all fast index scans on
        // small system catalogs) instead of fanning out via Task.WhenAll.
        var columns = await ReadColumnsAsync(conn, schema, tableName, ct);
        var indexes = await ReadIndexesAsync(conn, schema, tableName, ct);
        var checks = await ReadCheckConstraintsAsync(conn, schema, tableName, ct);
        var fks = await ReadForeignKeysAsync(conn, schema, tableName, ct);

        return new SchemaDescriptor.Table(
            Name: qualifiedName,
            Columns: columns,
            Indexes: indexes,
            CheckConstraints: checks,
            ForeignKeys: fks);
    }

    private static async Task<IReadOnlyList<SchemaDescriptor.Column>> ReadColumnsAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string Sql = @"
            SELECT column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = @schema AND table_name = @table
            ORDER BY ordinal_position;";

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", tableName);

        var cols = new List<SchemaDescriptor.Column>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var columnName = reader.GetString(0);
                if (SchemaNormalize.IsSystemColumn(columnName))
                {
                    continue;
                }

                cols.Add(new SchemaDescriptor.Column(
                    Name: columnName.ToLowerInvariant(),
                    DataType: SchemaNormalize.DataType(reader.GetString(1)),
                    IsNullable: reader.GetString(2) == "YES",
                    DefaultExpression: SchemaNormalize.DefaultExpression(
                        reader.IsDBNull(3) ? null : reader.GetString(3))));
            }
        }

        return cols;
    }

    private static async Task<IReadOnlyList<SchemaDescriptor.Index>> ReadIndexesAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        // Filter out the implicit primary-key index (`<table>_pkey`). EF Core
        // does not surface PK indexes via IIndex — they're handled by the
        // IPrimaryKey metadata. Comparing them would always show one
        // "extra" index on the PG side and false-positive every test.
        const string Sql = @"
            SELECT
                i.relname        AS index_name,
                ix.indisunique   AS is_unique,
                pg_get_indexdef(ix.indexrelid) AS index_def,
                pg_get_expr(ix.indpred, ix.indrelid) AS predicate
            FROM pg_index ix
            JOIN pg_class i   ON i.oid = ix.indexrelid
            JOIN pg_class t   ON t.oid = ix.indrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema AND t.relname = @table
              AND ix.indisprimary = FALSE
            ORDER BY i.relname;";

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", tableName);

        var indexes = new List<SchemaDescriptor.Index>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                indexes.Add(new SchemaDescriptor.Index(
                    Name: reader.GetString(0).ToLowerInvariant(),
                    IsUnique: reader.GetBoolean(1),
                    Columns: ExtractIndexColumns(reader.GetString(2)),
                    Filter: SchemaNormalize.IndexFilter(
                        reader.IsDBNull(3) ? null : reader.GetString(3))));
            }
        }

        return indexes;
    }

    private static async Task<IReadOnlyList<SchemaDescriptor.CheckConstraint>> ReadCheckConstraintsAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        // conrelid is oid; cast the parameter to regclass so PG's operator
        // catalog finds the equality (oid = oid) and we don't trip
        // "operator does not exist: oid = text".
        const string Sql = @"
            SELECT conname, pg_get_constraintdef(oid)
            FROM pg_constraint
            WHERE contype = 'c'
              AND conrelid = (@relid)::regclass
            ORDER BY conname;";

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("relid", $"{schema}.{tableName}");

        var checks = new List<SchemaDescriptor.CheckConstraint>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                checks.Add(new SchemaDescriptor.CheckConstraint(
                    Name: reader.GetString(0).ToLowerInvariant(),
                    Expression: SchemaNormalize.CheckExpression(reader.GetString(1))));
            }
        }

        return checks;
    }

    private static async Task<IReadOnlyList<SchemaDescriptor.ForeignKey>> ReadForeignKeysAsync(
        NpgsqlConnection conn, string schema, string tableName, CancellationToken ct)
    {
        const string Sql = @"
            SELECT
                c.conname,
                c.conkey,
                c.confkey,
                c.confrelid::regclass::text AS ref_table,
                c.confdeltype
            FROM pg_constraint c
            JOIN pg_class t     ON t.oid = c.conrelid
            JOIN pg_namespace n ON n.oid = t.relnamespace
            WHERE n.nspname = @schema
              AND t.relname = @table
              AND c.contype = 'f'
            ORDER BY c.conname;";

        await using var cmd = new NpgsqlCommand(Sql, conn);
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", tableName);

        // Materialize the FK rows fully (consume the reader to end) BEFORE
        // running column-name lookups — Npgsql 10 forbids nested commands on
        // the same connection while a reader is open.
        var raw = new List<FkRow>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                raw.Add(new FkRow(
                    Name: reader.GetString(0),
                    ColIds: (short[])reader.GetValue(1),
                    RefColIds: (short[])reader.GetValue(2),
                    RefTableShort: reader.GetString(3),
                    DelChar: (char)reader.GetValue(4)));
            }
        }

        var fks = new List<SchemaDescriptor.ForeignKey>(raw.Count);
        foreach (var row in raw)
        {
            // Referenced tables are reported bare (schema stripped) to match
            // the EF side, which reports principal table names without schema.
            var refTable = row.RefTableShort.Contains('.')
                ? row.RefTableShort.Split('.').Last().Trim('"')
                : row.RefTableShort;

            fks.Add(new SchemaDescriptor.ForeignKey(
                Name: row.Name.ToLowerInvariant(),
                Columns: row.ColIds.Select(ResolveColumnName).ToArray(),
                ReferencedTable: refTable.ToLowerInvariant(),
                ReferencedColumns: row.RefColIds.Select(ResolveColumnName).ToArray(),
                OnDelete: DecodeFkAction(row.DelChar)));
        }

        return fks;

        string ResolveColumnName(short attnum)
        {
            // attrelid is oid; cast @relid to regclass so PG resolves
            // `oid = oid` and we don't trip "operator does not exist: oid = text".
            const string AttSql = @"
                SELECT a.attname
                FROM pg_attribute a
                WHERE a.attrelid = (@relid)::regclass
                  AND a.attnum = @attnum;";
            using var c = new NpgsqlCommand(AttSql, conn);
            c.Parameters.AddWithValue("relid", $"{schema}.{tableName}");
            c.Parameters.AddWithValue("attnum", attnum);
            return ((string)c.ExecuteScalar()!).ToLowerInvariant();
        }
    }

    /// <summary>
    /// Materialized form of one row from the FK catalog query. Keeps the
    /// raw values out of the catalog reader so we can issue nested
    /// column-name lookups after the reader has closed.
    /// </summary>
    private sealed record FkRow(
        string Name,
        short[] ColIds,
        short[] RefColIds,
        string RefTableShort,
        char DelChar);

    /// <summary>
    /// Parse <c>pg_get_indexdef</c> output and return the ordered column names.
    /// The module schemas do not use expression indexes / INCLUDE columns,
    /// so a single simple regex is sufficient.
    /// </summary>
    private static string[] ExtractIndexColumns(string indexDef)
    {
        // Example: CREATE UNIQUE INDEX ix_tenants_institution_code ON public.tenants USING btree (institution_code)
        //          CREATE INDEX ix_tenant_configurations_active ON public.tenant_configurations USING btree (is_active) WHERE is_active
        var start = indexDef.IndexOf('(');
        var end = indexDef.IndexOf(')', start + 1);
        if (start < 0 || end < 0)
        {
            return Array.Empty<string>();
        }

        var inside = indexDef.Substring(start + 1, end - start - 1);
        return inside
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.ToLowerInvariant())
            .ToArray();
    }

    /// <summary>
    /// pg_constraint.confdeltype: 'a' = no action, 'r' = restrict, 'c' = cascade,
    /// 'n' = set null, 'd' = set default.
    /// </summary>
    private static string DecodeFkAction(char c) => c switch
    {
        'a' => "NO ACTION",
        'r' => "RESTRICT",
        'c' => "CASCADE",
        'n' => "SET NULL",
        'd' => "SET DEFAULT",
        _   => c.ToString(),
    };
}
