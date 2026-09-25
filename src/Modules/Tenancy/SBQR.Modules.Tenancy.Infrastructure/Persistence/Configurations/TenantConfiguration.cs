using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="Tenant"/> aggregate. Maps
/// every column from <c>docs/design/database-design.md</c> §1.3 plus the indexes
/// from §1.8, and applies the <see cref="TenantId"/> → <c>UUID</c> value
/// converter at the <c>tenant_id</c> PK.
///
/// Note: this configuration is intentionally read-only with respect to the
/// domain invariants — column types, lengths, nullability, CHECK constraints,
/// and indexes all match the canonical migration; the aggregate enforces the
/// invariant semantics on the write side (see <see cref="Tenant.Register"/>).
/// </summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenants", "public");

        // PK: tenant_id (UUID). The TenantId record struct is flattened to its
        // underlying Guid value so PostgreSQL stores it as a native UUID.
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever() // server-assigned in Tenant.Register; EF must not overwrite
            .HasConversion(id => id.Value, value => new TenantId(value));

        builder.Property(t => t.InstitutionName)
            .HasColumnName("institution_name")
            .HasColumnType("varchar(200)")
            .IsRequired();

        builder.Property(t => t.InstitutionCode)
            .HasColumnName("institution_code")
            .HasColumnType("varchar(6)")
            .IsRequired();

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .HasConversion(
                // DB stores UPPER_SNAKE_CASE ('PENDING', 'ACTIVE', etc.). The
                // C# enum is PascalCase; convert to uppercase on write.
                status => status.ToString().ToUpperInvariant(),
                value => Enum.Parse<TenantStatus>(value, ignoreCase: true))
            .IsRequired();

        // Audit columns — populated by the TenancyAuditColumnInterceptor.
        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(t => t.ModifiedBy)
            .HasColumnName("modified_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(t => t.ModifiedAt)
            .HasColumnName("modified_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        // Indexes — the three secondary indexes defined in
        // database-design.md §1.8. institution_code carries the natural unique
        // key for the aggregate (mirrors institution_registries).
        builder.HasIndex(t => t.Status)
            .HasDatabaseName("ix_tenants_status");

        builder.HasIndex(t => t.InstitutionCode)
            .IsUnique()
            .HasDatabaseName("ix_tenants_institution_code");

        builder.HasIndex(t => t.IsActive)
            .HasFilter("is_active = TRUE")
            .HasDatabaseName("ix_tenants_active");
    }
}