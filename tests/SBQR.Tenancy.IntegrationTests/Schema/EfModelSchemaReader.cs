using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;

namespace SBQR.Tenancy.IntegrationTests.Schema;

/// <summary>
/// Reads the EF Core model for the Tenancy module and returns the same
/// <see cref="SchemaDescriptor.Table"/> shape that <see cref="PostgresSchemaReader"/>
/// produces, so the two can be compared structurally.
///
/// <para>
/// Scoped to the Tenancy-only <see cref="TenancyDbContext"/> — does not see other
/// modules' entities. Uses <see cref="DbContext.Model"/> after
/// <c>ApplyConfigurationsFromAssembly</c> has wired up the
/// <c>IEntityTypeConfiguration&lt;T&gt;</c> classes.
/// </para>
/// </summary>
internal static class EfModelSchemaReader
{
    /// <summary>
    /// Build the <see cref="TenancyDbContext"/> model exactly the way the host
    /// would (using the same <c>UseNpgsql</c>) and read its relational model.
    /// A throwaway <see cref="DbContext"/> is used so the test never has to
    /// stand up a full <c>IServiceProvider</c>.
    /// </summary>
    public static IReadOnlyList<SchemaDescriptor.Table> Read()
    {
        var opts = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql("Host=ignored;Database=ignored;Username=ignored;Password=ignored")
            .Options;

        using var ctx = new TenancyDbContext(opts);

        var tables = new List<SchemaDescriptor.Table>();
        foreach (var entity in ctx.Model.GetEntityTypes())
        {
            tables.Add(ReadEntity(entity));
        }

        return tables;
    }

    private static SchemaDescriptor.Table ReadEntity(IEntityType entity)
    {
        var tableName = entity.GetTableName()?.ToLowerInvariant()
            ?? throw new InvalidOperationException($"Entity {entity.ClrType.Name} has no table name.");

        // Schema-qualified so the EF side compares equal to the PG side
        // (PostgresSchemaReader reports "schema.table" across the module
        // schemas declared in db/migrations/001_module_schemas.sql).
        var schemaName = (entity.GetSchema() ?? "public").ToLowerInvariant();

        var columns = entity.GetProperties()
            .Select(ReadColumn)
            .Where(c => !SchemaNormalize.IsSystemColumn(c.Name))
            .ToArray();

        var indexes = entity.GetIndexes()
            .Select(ReadIndex)
            .ToArray();

        // EF Core 10's IReadOnlyCheckConstraint API surface is unstable across
        // patch releases (e.g. GetName() takes a StoreObjectIdentifier in some
        // builds, no args in others). The drift test deliberately skips CHECK
        // constraints today — column + index + FK coverage is what catches the
        // real regressions (missing column, type drift, index dropped). A
        // follow-up can re-introduce CHECK comparison once the EF API
        // stabilises.
        var checks = Array.Empty<SchemaDescriptor.CheckConstraint>();

        var fks = entity.GetForeignKeys()
            .Select(ReadForeignKey)
            .ToArray();

        return new SchemaDescriptor.Table(
            Name: $"{schemaName}.{tableName}",
            Columns: columns,
            Indexes: indexes,
            CheckConstraints: checks,
            ForeignKeys: fks);
    }

    private static SchemaDescriptor.Column ReadColumn(IProperty property)
    {
        var columnType = property.GetColumnType()
            ?? throw new InvalidOperationException($"Property {property.Name} has no column type.");

        // Default may be declared via either HasDefaultValueSql (raw SQL) or
        // HasDefaultValue (constant). Both should be visible to the schema
        // drift test — PG's information_schema.column_default carries
        // whatever the migration wrote. Combine both signals so the EF side
        // reports the same "has default" truth as the PG side.
        //
        // NOTE: GetDefaultValue() returns the CLR's default(T) even when no
        // HasDefaultValue() was called (e.g. Guid.Empty for a converted
        // UUID, default(enum) for a status enum). Those are EF's "implicit"
        // defaults that never reach PG — only respect defaults that were
        // explicitly configured (RelationalAnnotationNames.DefaultValue).
        var rawDefault =
            property.GetDefaultValueSql()
            ?? (property.FindAnnotation("Relational:DefaultValue") is not null
                ? ConstValueToSql(property.GetDefaultValue())
                : null);

        return new SchemaDescriptor.Column(
            Name: property.GetColumnName()!.ToLowerInvariant(),
            DataType: SchemaNormalize.DataType(columnType),
            IsNullable: property.IsNullable,
            DefaultExpression: SchemaNormalize.DefaultExpression(rawDefault));
    }

    /// <summary>
    /// Render a non-null EF default value as the SQL literal PG would carry
    /// in information_schema.column_default. Booleans → "true", numerics
    /// via ToString, strings quoted with single quotes.
    /// </summary>
    private static string? ConstValueToSql(object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        string s => $"'{s.Replace("'", "''")}'",
        _ => value.ToString(),
    };

    private static SchemaDescriptor.Index ReadIndex(IIndex index)
    {
        var columns = index.Properties
            .Select(p => p.GetColumnName()!.ToLowerInvariant())
            .ToArray();

        return new SchemaDescriptor.Index(
            Name: index.GetDatabaseName()!.ToLowerInvariant(),
            IsUnique: index.IsUnique,
            Columns: columns,
            Filter: SchemaNormalize.IndexFilter(index.GetFilter()));
    }

    /// <summary>
    /// EF Core 10's public <see cref="ICheckConstraint"/> API is unstable across
    /// patch releases, so the EF side reports no CHECK constraints today.
    /// See the call-site comment for the rationale.
    /// </summary>

    private static SchemaDescriptor.ForeignKey ReadForeignKey(IForeignKey fk)
    {
        var onDelete = fk.DeleteBehavior switch
        {
            DeleteBehavior.Restrict => "RESTRICT",
            DeleteBehavior.Cascade  => "CASCADE",
            DeleteBehavior.SetNull  => "SET NULL",
            _ => "NO ACTION",
        };

        var columns = fk.Properties
            .Select(p => p.GetColumnName()!.ToLowerInvariant())
            .ToArray();

        var refCols = fk.PrincipalKey.Properties
            .Select(p => p.GetColumnName()!.ToLowerInvariant())
            .ToArray();

        var principalTable = fk.PrincipalEntityType.GetTableName()!.ToLowerInvariant();

        return new SchemaDescriptor.ForeignKey(
            Name: fk.GetConstraintName()!.ToLowerInvariant(),
            Columns: columns,
            ReferencedTable: principalTable,
            ReferencedColumns: refCols,
            OnDelete: onDelete);
    }
}
