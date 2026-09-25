using FluentAssertions;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.Schema;

/// <summary>
/// Structural drift detector: the canonical SQL migration scripts (owned by
/// the external migration tool) are the source of truth for the live schema.
/// The EF Core <c>IEntityTypeConfiguration&lt;T&gt;</c> classes are the source
/// of truth for how the application code reads / writes that schema. This
/// test proves they agree.
///
/// <para>
/// On drift (e.g. someone adds a column to SQL without an EF property, or
/// changes an index without updating the configuration) the test fails with
/// a structural diff so the change shows up in code review. Per the project
/// README, EF Core code-first migrations are <b>forbidden</b> — this drift
/// test is the safety net that keeps the SQL scripts and the EF model in
/// lockstep without an EF migration history table.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaModelDriftTests
{
    /// <summary>
    /// The set of tables that <see cref="SBQR.Modules.Tenancy.Infrastructure.Persistence.TenancyDbContext"/>
    /// still owns after the <c>tenant_configurations</c> aggregate relocated to
    /// the IdentityAccess module (it replaced the legacy <c>api_credentials</c>
    /// table) and the <c>crypto_keys</c> aggregate relocated to the KeyCustody
    /// module (which owns its own drift coverage). The drift test is
    /// intentionally scoped to these tables; IdentityAccess owns its own
    /// drift coverage in a future test project (see
    /// docs/design/tactical-design.md §3/§7).
    ///
    /// <para>
    /// <c>public.tenant_applications</c> was added in migration 008
    /// (FR-AUTH-002). The aggregate lives in <c>SBQR.Modules.Tenancy.Domain</c>
    /// and the EF entity config in
    /// <c>SBQR.Modules.Tenancy.Infrastructure.Persistence.Configurations</c>;
    /// both must agree with the canonical SQL migration script. All SBQR
    /// tables live in the single <c>public</c> schema per
    /// <c>db/migrations/README.md</c>.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> TenancyOwnedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "public.tenants",
        "public.tenant_applications",
    };

    private readonly PostgreSqlFixture _pg;

    public SchemaModelDriftTests(PostgreSqlFixture pg)
    {
        _pg = pg ?? throw new ArgumentNullException(nameof(pg));
    }

    [Fact]
    public async Task Ef_model_matches_actual_postgres_schema()
    {
        // Arrange: read both sides. PG side requires the live fixture (which
        // has already verified the canonical schema is applied — see
        // PostgreSqlFixture.EnsureSchemaAppliedAsync); the EF side needs only
        // a throwaway DbContext because we're just walking
        // Model.GetEntityTypes.
        // The drift detector is scoped to the Tenancy DbContext (no other
        // module's tables under test here). The tenant_configurations aggregate
        // lives in the IdentityAccess module; a separate future drift test
        // will cover that schema against IdentityAccessDbContext.
        var pgTablesAll = await PostgresSchemaReader.ReadAsync(_pg.ConnectionString);
        var efTables = EfModelSchemaReader.Read();

        // Scope both sides to the tables Tenancy still owns.
        var pgTablesScoped = pgTablesAll
            .Where(t => TenancyOwnedTables.Contains(t.Name))
            .ToArray();
        var efTablesScoped = efTables
            .Where(t => TenancyOwnedTables.Contains(t.Name))
            .ToArray();

        // ---- Index-name policy -------------------------------------------
        // PostgreSQL auto-names inline UNIQUE constraints as
        //   <table>_<column>_key  (e.g. tenants_tenant_code_key)
        // and the EF configurations name them explicitly as
        //   ix_<table>_<column>_unique
        // The two are the same physical index — the names will never agree.
        // Translate PG's auto-name into the EF name before comparison so the
        // physical structure can still be compared.
        var pgTablesRenamed = pgTablesScoped
            .Select(t => t with
            {
                Indexes = t.Indexes.Select(RenamePgAutoUniqueIndex).ToArray(),
            })
            .ToArray();

        // ---- Reverse direction: every PG table should have an EF model ----
        foreach (var pg in pgTablesRenamed)
        {
            efTablesScoped.Should().Contain(t => t.Name == pg.Name,
                $"PostgreSQL has table '{pg.Name}' but the EF model does not declare it");
        }

        // ---- Per-table deep compare --------------------------------------
        foreach (var ef in efTablesScoped)
        {
            var pg = pgTablesRenamed.SingleOrDefault(t => t.Name == ef.Name);
            pg.Should().NotBeNull(
                $"EF declares table '{ef.Name}' but PostgreSQL has no matching table " +
                $"(EF tables: {string.Join(", ", efTablesScoped.Select(t => t.Name))}).");

            // Columns: unordered set comparison. PG's information_schema
            // orders by ordinal_position (table creation order); EF's
            // IEntityTypeConfiguration<T> declares properties in source
            // order. The two orderings are not guaranteed to match — what
            // matters is that the same set of physical columns exists on
            // both sides.
            pg!.Columns.Should().BeEquivalentTo(
                ef.Columns,
                options => options.WithoutStrictOrdering(),
                $"the EF model and the SQL migration disagree on columns of '{ef.Name}'");

            // Indexes: unordered. PG may emit indexes in catalog-order, EF
            // may add HasIndex calls in any order — what matters is that the
            // same set of physical indexes exists on both sides.
            pg.Indexes.Should().BeEquivalentTo(
                ef.Indexes,
                options => options.WithoutStrictOrdering(),
                $"the EF model and the SQL migration disagree on indexes of '{ef.Name}'");
        }
    }

    /// <summary>
    /// Map PostgreSQL's auto-generated names for inline UNIQUE constraints to
    /// the names EF Core's <c>HasIndex(...).HasDatabaseName(...)</c> declares.
    /// </summary>
    private static SchemaDescriptor.Index RenamePgAutoUniqueIndex(SchemaDescriptor.Index idx)
    {
        // tenants_tenant_code_key      → ix_tenants_tenant_code_unique
        // tenant_configurations_client_id_key → ix_tenant_configurations_client_id_unique
        if (idx.Name.EndsWith("_key", StringComparison.Ordinal)
            && idx.IsUnique
            && idx.Columns.Count == 1)
        {
            var tablePrefix = idx.Name[..^"_key".Length];
            var lastUnderscore = tablePrefix.LastIndexOf('_');
            if (lastUnderscore > 0)
            {
                var renamed = $"ix_{tablePrefix[..lastUnderscore]}_{tablePrefix[(lastUnderscore + 1)..]}_unique";
                return idx with { Name = renamed };
            }
        }

        // uq_crypto_keys_*            → ix_crypto_keys_*_unique
        if (idx.Name.StartsWith("uq_", StringComparison.Ordinal) && idx.IsUnique)
        {
            var renamed = "ix_" + idx.Name[3..] + "_unique";
            return idx with { Name = renamed };
        }

        return idx;
    }
}
