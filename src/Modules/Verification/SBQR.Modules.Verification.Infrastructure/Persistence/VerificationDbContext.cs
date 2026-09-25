using Microsoft.EntityFrameworkCore;
using SBQR.Modules.Verification.Domain.Aggregates;

namespace SBQR.Modules.Verification.Infrastructure.Persistence;

/// <summary>
/// EF Core context over <c>public.qr_validations</c>. Schema is owned
/// by db/migrations (applied by the external migration tool); this context
/// never migrates. One row per processed request records the request
/// context (tenant / request id / timestamp / correlation id) and its
/// outcome in a single SaveChanges — an attempt always records exactly one
/// outcome, even when the outcome is a failure. The (tenant_id, request_id)
/// unique index in the database is the C6 replay guard.
/// </summary>
public sealed class VerificationDbContext : DbContext
{
    public VerificationDbContext(DbContextOptions<VerificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<QrValidation> Validations => Set<QrValidation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var validation = modelBuilder.Entity<QrValidation>();
        validation.ToTable("qr_validations", "public");
        validation.HasKey(v => v.QrValidationId);
        validation.Property(v => v.QrValidationId).HasColumnName("qr_validation_id");
        validation.Property(v => v.TenantId).HasColumnName("tenant_id");
        validation.Property(v => v.RequestId).HasColumnName("request_id");
        validation.Property(v => v.RequestTimestamp).HasColumnName("request_timestamp");
        validation.Property(v => v.CorrelationId).HasColumnName("correlation_id");
        validation.Property(v => v.InstitutionCode).HasColumnName("institution_code");
        validation.Property(v => v.Verdict).HasColumnName("verdict");
        validation.Property(v => v.TrustSource).HasColumnName("trust_source");
        validation.Property(v => v.ReasonCode).HasColumnName("reason_code");
        validation.Property(v => v.CreatedBy).HasColumnName("created_by");
        validation.HasIndex(v => new { v.TenantId, v.RequestId })
            .IsUnique()
            .HasDatabaseName("uq_qr_validations_replay");
    }
}
