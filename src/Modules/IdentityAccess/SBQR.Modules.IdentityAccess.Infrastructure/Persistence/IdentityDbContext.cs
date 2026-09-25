using Microsoft.EntityFrameworkCore;
using SBQR.Modules.IdentityAccess.Domain.Aggregates;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for the Identity &amp; Access bounded context.
/// Owns the <c>public.tenant_configurations</c> table (replaced the legacy
/// <c>identity.api_credentials</c> table — see migration
/// <c>db/migrations/002_tenancy_and_identity.sql</c>).
/// Schema is created and migrated by the external migration tool via
/// <c>db/migrations/*.sql</c> — this context does NOT call
/// <c>Database.Migrate()</c> (forbidden per <c>docs/PERSISTENCE_DECISIONS.md</c> §3).
/// </summary>
public sealed class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    /// <summary>The <c>public.tenant_configurations</c> aggregate set.</summary>
    public DbSet<TenantConfiguration> TenantConfigurations => Set<TenantConfiguration>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Apply every IEntityTypeConfiguration<T> in this assembly. Keeps each
        // entity's mapping next to its class, with the DbContext unaware of
        // individual configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
