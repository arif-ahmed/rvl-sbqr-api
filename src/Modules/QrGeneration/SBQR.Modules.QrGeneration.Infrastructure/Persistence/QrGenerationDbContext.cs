using Microsoft.EntityFrameworkCore;

namespace SBQR.Modules.QrGeneration.Infrastructure.Persistence;

/// <summary>
/// EF Core context over <c>public.qr_generations</c>. Schema is owned by
/// db/migrations (applied by the external migration tool); this context
/// never migrates.
/// </summary>
public sealed class QrGenerationDbContext : DbContext
{
    public QrGenerationDbContext(DbContextOptions<QrGenerationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SBQR.Modules.QrGeneration.Domain.Aggregates.QrGeneration> Generations
        => Set<SBQR.Modules.QrGeneration.Domain.Aggregates.QrGeneration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<SBQR.Modules.QrGeneration.Domain.Aggregates.QrGeneration>();
        entity.ToTable("qr_generations", "public");
        entity.HasKey(g => g.QrGenerationId);
        entity.Property(g => g.QrGenerationId).HasColumnName("qr_generation_id");
        entity.Property(g => g.TenantId).HasColumnName("tenant_id");
        entity.Property(g => g.QrType).HasColumnName("qr_type");
        entity.Property(g => g.SignatureKeyVersion).HasColumnName("signature_key_version");
        entity.Property(g => g.IdempotencyKey).HasColumnName("idempotency_key");

        // Audit (created only) — stamped by QrGenerationAuditColumnInterceptor.
        entity.Property(g => g.CreatedBy).HasColumnName("created_by").HasMaxLength(200);
        entity.Property(g => g.CreatedAt).HasColumnName("created_at");
    }
}
