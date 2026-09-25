using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SBQR.Modules.KeyCustody.Domain.Aggregates;

namespace SBQR.Modules.KeyCustody.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core entity configuration for the <see cref="CryptoKey"/> aggregate.
/// Maps every column from <c>docs/design/database-design.md</c> §1.4, applies
/// the strongly-typed <see cref="CryptoKeyId"/> ↔ <c>UUID</c> conversion at
/// the PK, and adds the indexes from §1.8. KeyCustody owns both reads and
/// writes against <c>crypto_keys</c> — see <see cref="KeyCustodyDbContext"/>.
/// </summary>
public sealed class CryptoKeyConfiguration : IEntityTypeConfiguration<CryptoKey>
{
    public void Configure(EntityTypeBuilder<CryptoKey> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("crypto_keys", "public", t =>
        {
            t.HasCheckConstraint(
                "ck_crypto_keys_status",
                "status IN ('GENERATING','PENDING','ACTIVE','SUSPENDED','RETIRING','RETIRED','REVOKED')");
        });

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id)
            .HasColumnName("crypto_key_id")
            .HasColumnType("uuid")
            .ValueGeneratedNever()
            .HasConversion(id => id.Value, value => new CryptoKeyId(value));

        builder.Property(k => k.TenantId)
            .HasColumnName("tenant_id")
            .HasColumnType("uuid")
            .IsRequired();

        builder.Property(k => k.KeyId)
            .HasColumnName("key_id")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(k => k.KeyVersion)
            .HasColumnName("key_version")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(k => k.PublicKey)
            .HasColumnName("public_key")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(k => k.CustodyKeyReference)
            .HasColumnName("custody_key_reference")
            .HasColumnType("varchar(500)")
            .IsRequired();

        builder.Property(k => k.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(20)")
            .HasConversion(
                s => ToDb(s),
                value => FromDb(value))
            .IsRequired();

        builder.Property(k => k.PublicKeySha256)
            .HasColumnName("public_key_sha256")
            .HasColumnType("char(64)")
            // The aggregate computes this at construction time. EF persists it.
            .IsRequired();

        builder.Property(k => k.CreatedBy)
            .HasColumnName("created_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(k => k.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(k => k.ModifiedBy)
            .HasColumnName("modified_by")
            .HasColumnType("varchar(200)")
            .IsRequired(false);

        builder.Property(k => k.ModifiedAt)
            .HasColumnName("modified_at")
            .HasColumnType("timestamptz")
            .IsRequired(false);

        builder.Property(k => k.IsActive)
            .HasColumnName("is_active")
            .HasColumnType("boolean")
            .HasDefaultValue(true)
            .IsRequired();

        // Soft-deleted rows are excluded from every query by default; callers
        // that need history (e.g. ListCryptoKeys) use IgnoreQueryFilters().
        builder.HasQueryFilter(k => k.IsActive);

        // Indexes from database-design.md §1.8.
        builder.HasIndex(k => new { k.TenantId, k.KeyId, k.KeyVersion })
            .IsUnique()
            .HasDatabaseName("ix_crypto_keys_tenant_keyid_version_unique");

        builder.HasIndex(k => k.TenantId)
            .IsUnique()
            .HasFilter("status = 'ACTIVE'")
            .HasDatabaseName("ix_crypto_keys_active_tenant_unique");

        builder.HasIndex(k => k.Status)
            .HasDatabaseName("ix_crypto_keys_status");

        builder.HasIndex(k => k.IsActive)
            .HasFilter("is_active = TRUE")
            .HasDatabaseName("ix_crypto_keys_active");
    }

    // DB stores GENERATING / PENDING / ACTIVE / SUSPENDED / RETIRING /
    // RETIRED / REVOKED; the C# enum is PascalCase.
    private static string ToDb(CryptoKeyStatus status) => status switch
    {
        CryptoKeyStatus.Generating => "GENERATING",
        CryptoKeyStatus.Pending => "PENDING",
        CryptoKeyStatus.Active => "ACTIVE",
        CryptoKeyStatus.Suspended => "SUSPENDED",
        CryptoKeyStatus.Retiring => "RETIRING",
        CryptoKeyStatus.Retired => "RETIRED",
        CryptoKeyStatus.Revoked => "REVOKED",
        _ => status.ToString().ToUpperInvariant(),
    };

    private static CryptoKeyStatus FromDb(string raw) => raw.ToUpperInvariant() switch
    {
        "GENERATING" => CryptoKeyStatus.Generating,
        "PENDING" => CryptoKeyStatus.Pending,
        "ACTIVE" => CryptoKeyStatus.Active,
        "SUSPENDED" => CryptoKeyStatus.Suspended,
        "RETIRING" => CryptoKeyStatus.Retiring,
        "RETIRED" => CryptoKeyStatus.Retired,
        "REVOKED" => CryptoKeyStatus.Revoked,
        _ => throw new InvalidOperationException(
            $"Unknown crypto_keys.status '{raw}'."),
    };
}
