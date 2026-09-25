namespace SBQR.Tenancy.IntegrationTests.Schema;

/// <summary>
/// Schema descriptor records used by the drift test to compare what PostgreSQL
/// actually stores (via <see cref="PostgresSchemaReader"/>) against what the EF
/// Core model declares (via <see cref="EfModelSchemaReader"/>).
///
/// <para>
/// All types are <c>sealed record</c> so the comparison (FluentAssertions
/// <c>BeEquivalentTo</c>) gets structural equality + a sane
/// <c>ToString()</c> for diagnostic output, and the records stay immutable.
/// </para>
/// </summary>
internal static class SchemaDescriptor
{
    /// <summary>A single table in the <c>public</c> schema.</summary>
    public sealed record Table(
        string Name,
        IReadOnlyList<Column> Columns,
        IReadOnlyList<Index> Indexes,
        IReadOnlyList<CheckConstraint> CheckConstraints,
        IReadOnlyList<ForeignKey> ForeignKeys);

    /// <summary>
    /// A single column. <see cref="DataType"/> is normalized by the reader
    /// (e.g. <c>character varying</c> → <c>varchar</c>, <c>timestamp without time zone</c>
    /// → <c>timestamp</c>) so the EF side and the PG side can be compared
    /// without an off-by-one synonym fight.
    /// </summary>
    public sealed record Column(
        string Name,
        string DataType,
        bool IsNullable,
        string? DefaultExpression);

    /// <summary>An index (unique or non-unique, with optional partial filter).</summary>
    public sealed record Index(
        string Name,
        bool IsUnique,
        IReadOnlyList<string> Columns,
        string? Filter);

    /// <summary>A CHECK constraint.</summary>
    public sealed record CheckConstraint(
        string Name,
        string Expression);

    /// <summary>
    /// A foreign key constraint. <see cref="Columns"/> / <see cref="ReferencedColumns"/>
    /// are ordered, single-column FKs only (sufficient for the Tenancy module's
    /// <c>crypto_keys.tenant_id → tenants.tenant_id</c>; the
    /// <c>tenant_configurations.tenant_id → tenants.tenant_id</c> FK is also
    /// validated by the schema-drift test even though the table itself lives in
    /// IdentityAccess — see <c>SchemaModelDriftTests</c> for the cross-module
    /// assertion).
    /// </summary>
    public sealed record ForeignKey(
        string Name,
        IReadOnlyList<string> Columns,
        string ReferencedTable,
        IReadOnlyList<string> ReferencedColumns,
        string OnDelete);
}
