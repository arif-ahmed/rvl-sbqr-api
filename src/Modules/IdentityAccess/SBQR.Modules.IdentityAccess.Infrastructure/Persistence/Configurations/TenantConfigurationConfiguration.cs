using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SBQR.Modules.IdentityAccess.Domain.Aggregates;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="TenantConfiguration"/> aggregate.
/// Maps every column from <c>docs/design/database-design.md</c> §1.3 (the
/// `tenant_configurations` row), applies the strongly-typed
/// <see cref="TenantConfigurationId"/> UUID conversion at the PK, and adds the
/// indexes from §1.8. Lives in the <c>public</c> schema.
///
/// <para>
/// The two capability flags (<c>is_qr_generation_allowed</c>,
/// <c>is_qr_validation_allowed</c>) are part of the same row — they're read by
/// the QR QrGeneration / Verification flows to gate the tenant's QR privilege
/// without invalidating their auth credential.
/// </para>
/// </summary>
public sealed class TenantConfigurationConfiguration : IEntityTypeConfiguration<TenantConfiguration>
{
    public void Configure(EntityTypeBuilder<TenantConfiguration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // tenant_configurations table in the public schema (created in
        // db/migrations/002_tenancy_and_identity.sql).
        builder.ToTable("tenant_configurations", "public");

        // PK: tenant_configuration_id (UUID).
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("tenant_configuration_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .HasConversion(id => id.Value, value => new TenantConfigurationId(value));

        // tenant_id (UUID) — opaque Guid across the Tenancy boundary. No FK
        // navigation: Tenancy owns the tenants aggregate; we link by FK only
        // (the SQL migration defines REFERENCES public.tenants (tenant_id)).
        builder.Property(c => c.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(c => c.ClientId)
            .HasColumnName("client_id")
            .HasColumnType("varchar(100)")
            .IsRequired();

        // client_secret_hash: TEXT, Argon2id PHC string. The DB CHECK constraint
        // (chk_tenant_configurations_client_secret_hash_argon2id, defined in
        // db/migrations/20260911120000_…) enforces the PHC format at write time.
        builder.Property(c => c.ClientSecretHash)
            .HasColumnName("client_secret_hash")
            .HasColumnType("text")
            .IsRequired();

        // Capability flags — the new columns that justified the rename from
        // api_credentials to tenant_configurations. Both default TRUE in the DB
        // so issuance flow without explicit flags behaves identically to the
        // pre-rename behaviour.
        builder.Property(c => c.IsQrGenerationAllowed)
            .HasColumnName("is_qr_generation_allowed")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.IsQrValidationAllowed)
            .HasColumnName("is_qr_validation_allowed")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .HasConversion(
                s => ToDb(s),
                value => FromDb(value))
            .IsRequired();

        builder.Property(c => c.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        builder.Property(c => c.LastUsedAt)
            .HasColumnName("last_used_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        // Audit columns.
        builder.Property(c => c.CreatedBy)
            .HasColumnName("created_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(c => c.ModifiedBy)
            .HasColumnName("modified_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(c => c.ModifiedAt)
            .HasColumnName("modified_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        builder.Property(c => c.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        // Indexes from database-design.md §1.8 (same shape as api_credentials;
        // the tenant_configurations index names match the migration).
        builder.HasIndex(c => c.ClientId)
            .IsUnique()
            .HasDatabaseName("ix_tenant_configurations_client_id_unique");

        builder.HasIndex(c => c.TenantId)
            .HasDatabaseName("ix_tenant_configurations_tenant");

        builder.HasIndex(c => c.Status)
            .HasDatabaseName("ix_tenant_configurations_status");

        builder.HasIndex(c => c.ExpiresAt)
            .HasFilter("expires_at IS NOT NULL")
            .HasDatabaseName("ix_tenant_configurations_expires_at");

        builder.HasIndex(c => c.IsActive)
            .HasFilter("is_active = TRUE")
            .HasDatabaseName("ix_tenant_configurations_active");
    }

    private static string ToDb(TenantConfigurationStatus status) => status switch
    {
        TenantConfigurationStatus.Active => "ACTIVE",
        TenantConfigurationStatus.Suspended => "SUSPENDED",
        TenantConfigurationStatus.Revoked => "REVOKED",
        TenantConfigurationStatus.Expired => "EXPIRED",
        TenantConfigurationStatus.PendingRotation => "PENDING_ROTATION",
        _ => status.ToString().ToUpperInvariant(),
    };

    private static TenantConfigurationStatus FromDb(string raw) => raw.ToUpperInvariant() switch
    {
        "ACTIVE" => TenantConfigurationStatus.Active,
        "SUSPENDED" => TenantConfigurationStatus.Suspended,
        "REVOKED" => TenantConfigurationStatus.Revoked,
        "EXPIRED" => TenantConfigurationStatus.Expired,
        "PENDING_ROTATION" => TenantConfigurationStatus.PendingRotation,
        _ => throw new InvalidOperationException($"Unknown tenant_configurations.status '{raw}'."),
    };
}
