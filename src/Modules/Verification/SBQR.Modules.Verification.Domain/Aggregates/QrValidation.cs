namespace SBQR.Modules.Verification.Domain.Aggregates;

/// <summary>
/// The single recorded outcome of one verification attempt
/// (<c>public.qr_validations</c>). The former <c>qr_transactions</c>
/// table was removed (2026-09-08): foreign-issued codes can never reference
/// our generation table, and one row per attempt now records both the
/// request context (C5/C6) and its outcome.
///
/// <para>
/// The (tenant_id, request_id) pair is single-use — enforced by the DB
/// unique constraint <c>uq_qr_validations_replay</c> (C6). A duplicate
/// insert raises 23505, which the handler converts into a
/// REQUEST_REPLAYED response; the rejection itself is audited, not
/// re-persisted here.
/// </para>
/// </summary>
public sealed class QrValidation
{
    public Guid QrValidationId { get; set; }

    /// <summary>The VERIFYING tenant (who called the API) — C5. Server-resolved, never client-supplied.</summary>
    public Guid TenantId { get; set; }

    /// <summary>Client-generated unique id for this verification call — C6 replay guard.</summary>
    public string RequestId { get; set; } = string.Empty;

    /// <summary>Client clock at send time; the handler validates a ±5 min window — C6.</summary>
    public DateTimeOffset RequestTimestamp { get; set; }

    /// <summary>Server-generated correlation id — A5; also embedded in the audit metadata.</summary>
    public Guid CorrelationId { get; set; }

    /// <summary>Issuer institution code from the QR payload; null when the payload was too malformed to carry one.</summary>
    public string? InstitutionCode { get; set; }

    public string Verdict { get; set; } = string.Empty;

    /// <summary>TRUST_DIRECTORY (resolved via institution_keys per spec
    /// Annex B) or NONE (not evaluated — structural failure, non-P2P,
    /// stale, or replay). OWN_CUSTODY is no longer a value: Verification
    /// resolves ALL issuers through the trust store.</summary>
    public string TrustSource { get; set; } = "NONE";

    public string? ReasonCode { get; set; }

    public string? CreatedBy { get; set; }
}
