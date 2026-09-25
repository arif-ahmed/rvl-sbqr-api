using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="TenantApplication"/>
/// aggregate. Maps every column from
/// <c>db/migrations/008_tenant_applications.sql</c>, applies the strongly-typed
/// <see cref="TenantApplicationId"/> UUID conversion at the PK, and adds the
/// three indexes (unique platform+package_id, FK support, active partial).
///
/// <para>
/// Index names MUST mirror the SQL migration 1:1 so the schema-drift test
/// (<c>SchemaModelDriftTests</c>) compares equal. The CHECK constraints
/// (<c>platform IN ('ANDROID','IOS')</c> and <c>status IN ('ACTIVE','SUSPENDED')</c>)
/// are declared by the SQL migration only — EF Core's public
/// <c>ICheckConstraint</c> API is unstable across patch releases so the drift
/// test deliberately skips CHECK comparison (see
/// <c>EfModelSchemaReader</c> for the rationale).
/// </para>
/// </summary>
public sealed class TenantApplicationConfiguration : IEntityTypeConfiguration<TenantApplication>
{
    public void Configure(EntityTypeBuilder<TenantApplication> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenant_applications", "public");

        // PK: tenant_application_id (UUID).
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("tenant_application_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever() // server-assigned in TenantApplication.Register; EF must not overwrite
            .HasConversion(id => id.Value, value => new TenantApplicationId(value));

        // tenant_id (UUID) — opaque Guid across the Tenancy boundary. No
        // navigation: the tenants aggregate lives in the same DbContext and
        // we link by FK only.
        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.Property(a => a.Platform)
            .HasColumnName("platform")
            .HasColumnType("varchar(10)")
            .HasConversion(
                platform => platform == TenantApplicationPlatform.Android
                    ? "ANDROID"
                    : platform == TenantApplicationPlatform.Ios
                        ? "IOS"
                        : platform.ToString().ToUpperInvariant(),
                value => string.Equals(value, "ANDROID", StringComparison.OrdinalIgnoreCase)
                    ? TenantApplicationPlatform.Android
                    : string.Equals(value, "IOS", StringComparison.OrdinalIgnoreCase)
                        ? TenantApplicationPlatform.Ios
                        : Enum.Parse<TenantApplicationPlatform>(value, ignoreCase: true))
            .IsRequired();

        builder.Property(a => a.PackageId)
            .HasColumnName("package_id")
            .HasColumnType("varchar(200)")
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .HasConversion(
                s => s.ToString().ToUpperInvariant(),
                value => string.Equals(value, "ACTIVE", StringComparison.OrdinalIgnoreCase)
                    ? TenantApplicationStatus.Active
                    : string.Equals(value, "SUSPENDED", StringComparison.OrdinalIgnoreCase)
                        ? TenantApplicationStatus.Suspended
                        : Enum.Parse<TenantApplicationStatus>(value, ignoreCase: true))
            .IsRequired();

        // Audit columns — populated by the TenancyAuditColumnInterceptor.
        builder.Property(a => a.CreatedBy)
            .HasColumnName("created_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(a => a.ModifiedBy)
            .HasColumnName("modified_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(a => a.ModifiedAt)
            .HasColumnName("modified_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        builder.Property(a => a.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        // FK to public.tenants — defined in SQL with ON DELETE RESTRICT.
        // EF Core maps the DeleteBehavior.Restrict below so the model
        // agrees with the migration.
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes — mirror db/migrations/008_tenant_applications.sql exactly.
        builder.HasIndex(a => new { a.Platform, a.PackageId })
            .IsUnique()
            .HasDatabaseName("ix_tenant_applications_platform_package_id");

        builder.HasIndex(a => a.TenantId)
            .HasDatabaseName("ix_tenant_applications_tenant");

        builder.HasIndex(a => a.IsActive)
            .HasFilter("is_active = TRUE")
            .HasDatabaseName("ix_tenant_applications_active");
    }
}
