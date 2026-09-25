using Microsoft.EntityFrameworkCore;
using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence;

/// <summary>
/// EF Core <see cref="DbContext"/> for the Tenancy &amp; Access bounded context.
/// Owns the <c>tenants</c> aggregate only. The <c>api_credentials</c> aggregate
/// relocated to the IdentityAccess module; the <c>crypto_keys</c> aggregate
/// relocated to the KeyCustody module (which now owns the full signing-key
/// lifecycle: mint, rotate, suspend, reinstate, retire — see
/// <c>SBQR.Modules.KeyCustody.Infrastructure.Persistence.KeyCustodyDbContext</c>).
/// Tenancy's lifecycle handlers cascade onto keys through
/// <c>SBQR.Modules.KeyCustody.Contracts</c> commands, never by touching
/// KeyCustody's Domain or Infrastructure directly.
///
/// Schema is created and migrated by the external migration tool via
/// <c>db/migrations/*.sql</c> — this context does NOT call
/// <c>Database.Migrate()</c> (forbidden per <c>docs/PERSISTENCE_DECISIONS.md</c>
/// §3).
/// </summary>
public sealed class TenancyDbContext : DbContext
{
    /// <summary>
    /// Construct the context with explicit options. The composition root in
    /// <c>TenancyModule.RegisterServices</c> builds these options with
    /// <c>UseNpgsql(...)</c> and the <c>TenancyAuditColumnInterceptor</c>
    /// attached.
    /// </summary>
    /// <param name="options">Pre-configured context options.</param>
    public TenancyDbContext(DbContextOptions<TenancyDbContext> options)
        : base(options)
    {
    }

    /// <summary>The <c>tenants</c> aggregate set.</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>
    /// The <c>tenant_applications</c> aggregate set — the per-tenant
    /// allow-list of registered mobile-app package identifiers consulted by
    /// <c>ITenantApplicationDirectory</c> at OAuth token mint time
    /// (FR-AUTH-002). Added to the Tenancy DbContext together with
    /// <c>tenants</c> because both rows share the same lifecycle owner.
    /// </summary>
    public DbSet<TenantApplication> TenantApplications => Set<TenantApplication>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Apply every IEntityTypeConfiguration<T> in this assembly. Keeps each
        // entity's mapping next to its class, with the DbContext unaware of
        // individual configurations.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TenancyDbContext).Assembly);
    }
}
