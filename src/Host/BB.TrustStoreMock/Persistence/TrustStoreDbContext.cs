// src/BB.TrustStoreMock/Persistence/TrustStoreDbContext.cs
using Microsoft.EntityFrameworkCore;

namespace BB.TrustStoreMock.Persistence;

/// <summary>
/// EF Core context over the mock's OWN disposable database
/// (<c>bb_trust_store_mock</c>), created by
/// <c>docker/postgres/init/00-create-databases.sql</c>. Two tables in the
/// plain <c>public</c> schema — this is a standalone external-system
/// simulation, not an SBQR module, so there is no module schema and no
/// db/migrations entry: the schema is created here at startup via
/// <c>EnsureCreated</c> and dies with the database when the real BB trust
/// store replaces this mock.
/// </summary>
public sealed class TrustStoreDbContext(DbContextOptions<TrustStoreDbContext> options)
    : DbContext(options)
{
    public DbSet<InstitutionRow> Institutions => Set<InstitutionRow>();

    public DbSet<KeyRow> Keys => Set<KeyRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<InstitutionRow>(entity =>
        {
            entity.ToTable("institutions");
            entity.HasKey(i => i.InstitutionId);
            entity.Property(i => i.InstitutionId).HasColumnType("char(6)");
            entity.Property(i => i.InstituteType).HasColumnType("char(2)");
            entity.Property(i => i.InstitutionName).HasMaxLength(200);
            entity.HasMany(i => i.Keys)
                .WithOne(k => k.Institution!)
                .HasForeignKey(k => k.InstitutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KeyRow>(entity =>
        {
            entity.ToTable("institution_keys");
            entity.HasKey(k => k.Id);
            entity.Property(k => k.InstitutionId).HasColumnType("char(6)");
            entity.Property(k => k.PublicKeyPem).IsRequired();
            entity.Property(k => k.Sha256).HasColumnType("char(64)");
            entity.Property(k => k.Status).HasMaxLength(16);
            // The mock's key identity: one row per (institution, version).
            entity.HasIndex(k => new { k.InstitutionId, k.KeyVersion }).IsUnique();
        });
    }
}

/// <summary>One participating institution (Annex B Institution_ID = 6 digits).</summary>
public sealed class InstitutionRow
{
    public string InstitutionId { get; set; } = string.Empty;

    public string InstituteType { get; set; } = string.Empty;

    public string InstitutionName { get; set; } = string.Empty;

    public List<KeyRow> Keys { get; set; } = [];
}

/// <summary>
/// One version of an institution's Ed25519 public key. Status vocabulary:
/// ACTIVE (currently trusted), RETIRED (superseded by a newer version),
/// REVOKED (explicitly revoked via the admin surface). ValidFrom/ValidTo
/// are passthrough metadata — the mock never derives status from them;
/// the temporal gate belongs to InstitutionTrust's directory.
/// </summary>
public sealed class KeyRow
{
    public long Id { get; set; }

    public string InstitutionId { get; set; } = string.Empty;

    public InstitutionRow? Institution { get; set; }

    public int KeyVersion { get; set; }

    public string PublicKeyPem { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public string Status { get; set; } = "ACTIVE";

    public DateTime ValidFromUtc { get; set; }

    public DateTime? ValidToUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
