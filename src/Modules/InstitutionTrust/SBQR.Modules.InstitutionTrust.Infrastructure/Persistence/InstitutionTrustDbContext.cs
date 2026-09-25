using Microsoft.EntityFrameworkCore;
using SBQR.Modules.InstitutionTrust.Domain.Persistence;

namespace SBQR.Modules.InstitutionTrust.Infrastructure.Persistence;

/// <summary>
/// EF Core context over the trust directory. Schema is owned by
/// db/migrations (DbUp); this context never migrates.
///
/// <para>
/// Single-table schema (post 2026-09-09 consolidation): the former
/// <c>institution_registries</c> table has been folded into
/// <c>public.institution_keys</c>. The table carries the versioned public
/// key, its source (REGISTRY vs LOCAL), the merged status vocabulary
/// (ACTIVE | SUSPENDED | RETIRED), and the validity window
/// (<c>valid_from</c> / <c>valid_to</c> / <c>revoked_at</c>) the C4/C16
/// gate relies on. See <c>db/migrations/005_institution_trust.sql</c>.
/// The trust-directory table lives in <c>public</c> (it is a
/// platform-level, non-tenant-scoped resource shared across tenants).
/// </para>
///
/// <para>
/// Institutional identity and the human-readable institution name live in
/// <c>public.tenants</c> joined on <c>institution_code</c>; this row
/// carries the cryptographic publication only.
/// </para>
/// </summary>
public sealed class InstitutionTrustDbContext : DbContext
{
    public InstitutionTrustDbContext(DbContextOptions<InstitutionTrustDbContext> options)
        : base(options)
    {
    }

    public DbSet<InstitutionKey> Keys => Set<InstitutionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var key = modelBuilder.Entity<InstitutionKey>();
        key.ToTable("institution_keys", "public");
        key.HasKey(k => k.InstitutionKeyId);

        key.Property(k => k.InstitutionKeyId).HasColumnName("institution_key_id");
        key.Property(k => k.InstitutionCode).HasColumnName("institution_code");
        key.Property(k => k.InstituteType).HasColumnName("institute_type");
        key.Property(k => k.InstitutionName).HasColumnName("institution_name");
        key.Property(k => k.KeyVersion).HasColumnName("key_version");
        key.Property(k => k.PublicKey).HasColumnName("public_key");
        key.Property(k => k.PublicKeySha256).HasColumnName("public_key_sha256");
        key.Property(k => k.Source).HasColumnName("source");
        key.Property(k => k.Status).HasColumnName("status");
        key.Property(k => k.ValidFrom).HasColumnName("valid_from");
        key.Property(k => k.ValidTo).HasColumnName("valid_to");
        key.Property(k => k.RevokedAt).HasColumnName("revoked_at");
        key.Property(k => k.SyncedAt).HasColumnName("synced_at");
        key.Property(k => k.IsActive).HasColumnName("is_active");
        key.Property(k => k.CreatedBy).HasColumnName("created_by");
        key.Property(k => k.CreatedAt).HasColumnName("created_at");
        key.Property(k => k.ModifiedBy).HasColumnName("modified_by");
        key.Property(k => k.ModifiedAt).HasColumnName("modified_at");

        key.HasQueryFilter(k => k.IsActive);
        key.HasIndex(k => new { k.InstitutionCode, k.KeyVersion })
            .IsUnique()
            .HasDatabaseName("ix_institution_keys_institution");
    }
}
