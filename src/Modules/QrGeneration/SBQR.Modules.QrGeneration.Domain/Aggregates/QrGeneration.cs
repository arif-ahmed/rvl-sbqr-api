namespace SBQR.Modules.QrGeneration.Domain.Aggregates;

/// <summary>
/// One signed-QR generation row (<c>qr_generations</c>). No payload column
/// and no payload_hash column (decision 2026-09-08): integrity rests on the
/// QR signature + CRC. The computed hash flows only to the API response and
/// the audit entry's resource id for correlation — it is never persisted.
///
/// Persistence shape is owned by the Infrastructure layer; this type carries
/// the domain invariants (idempotency key optional in the API contract but
/// unique per tenant when present — enforced by a DB partial unique index)
/// and the lifecycle fields exposed to the Application pipeline.
///
/// <c>CreatedBy</c> and <c>CreatedAt</c> are stamped by
/// <c>QrGenerationAuditColumnInterceptor</c> on insert. This aggregate
/// deliberately does NOT implement <c>IAuditableEntity</c> because
/// <c>qr_generations</c> has no <c>modified_*</c> columns — declaring them
/// here would force EF Core to throw at flush.
/// </summary>
public sealed class QrGeneration
{
    public Guid QrGenerationId { get; set; }

    public Guid TenantId { get; set; }

    public string QrType { get; set; } = "STATIC";

    public int SignatureKeyVersion { get; set; }

    public string? IdempotencyKey { get; set; }

    // Audit (created only) — stamped by QrGenerationAuditColumnInterceptor.
    public string? CreatedBy { get; set; }

    public DateTimeOffset? CreatedAt { get; set; }
}
