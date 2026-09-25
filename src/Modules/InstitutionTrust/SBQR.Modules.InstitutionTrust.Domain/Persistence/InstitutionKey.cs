namespace SBQR.Modules.InstitutionTrust.Domain.Persistence;

/// <summary>
/// One versioned public key an institution published to the directory.
/// The (institution_code, key_version) pair is the natural identity.
///
/// <para>
/// Mirrors the canonical <c>public.institution_keys</c> table
/// (see <c>db/migrations/005_institution_trust.sql</c>) after the
/// 2026-09-09 consolidation that merged the former
/// <c>institution_registries</c> registry columns into the key row.
/// </para>
///
/// <para>
/// <see cref="InstitutionName"/> is the human-readable display name and
/// <see cref="InstituteType"/> is the 2-digit Annex A Institution Type
/// (Tag 26 sub 01) — both denormalised onto the row so a verifier
/// resolving a QR gets the issuer's identity without a join onto
/// <c>public.tenants</c>. Both columns are NOT NULL. <see cref="InstituteType"/>
/// is technically derivable from <c>institution_code[..2]</c>, but the
/// trust-store is the source of truth — the value is carried explicitly
/// so any BB correction out-of-band propagates without us silently
/// re-deriving a stale value.
/// </para>
///
/// <para>
/// <b>Validity window:</b> <see cref="ValidFrom"/> / <see cref="ValidTo"/>
/// / <see cref="RevokedAt"/> implement the C4/C16 gate
/// "expired-or-revoked → reject". A fresh publish sets
/// <see cref="ValidFrom"/> = now and leaves <see cref="ValidTo"/> open
/// (null). <see cref="RevokedAt"/> is set when the trust-store reports
/// a non-ACTIVE status for the institution.
/// </para>
///
/// <para>
/// <b>Source:</b> <see cref="Source"/> = <c>"REGISTRY"</c> for trust-store
/// syncs, <c>"LOCAL"</c> for our own tenants' signing keys (the KeyCustody
/// publish flow). The DB CHECK constraint <c>ck_institution_keys_source</c>
/// enforces that vocabulary; the EF mapping preserves the wire shape.
/// </para>
/// </summary>
public sealed class InstitutionKey
{
    public Guid InstitutionKeyId { get; set; }

    public string InstitutionCode { get; set; } = string.Empty;

    /// <summary>
    /// 2-digit Annex A Institution Type (Tag 26 sub 01). NOT NULL on the
    /// row; the DB CHECK <c>ck_institution_keys_institute_type</c>
    /// enforces <c>^[0-9]{2}$</c>. The application-level shape guard in
    /// <c>TrustRecordPatterns</c> applies the same rule at the boundary.
    /// </summary>
    public string InstituteType { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable display name of the institution (NOT NULL — the
    /// DB-level <c>ck_institution_keys_name_nonblank</c> CHECK rejects
    /// blanks). Carried alongside the cryptographic publication so a
    /// verifier resolving a QR gets the issuer's name without joining
    /// <c>public.tenants</c>.
    /// </summary>
    public string InstitutionName { get; set; } = string.Empty;

    public int KeyVersion { get; set; }

    public string PublicKey { get; set; } = string.Empty;

    public string PublicKeySha256 { get; set; } = string.Empty;

    /// <summary>
    /// REGISTRY (synced from the BB registry) or LOCAL (one of OUR
    /// tenants' signing keys — the KeyCustody publish flow). DB CHECK
    /// constraint enforces this vocabulary.
    /// </summary>
    public string Source { get; set; } = "REGISTRY";

    /// <summary>
    /// Trust-directory status vocabulary shared with KeyCustody lifecycle:
    /// <c>ACTIVE</c> (current signer), <c>SUSPENDED</c> (temporarily
    /// untrusted — fail closed), <c>RETIRED</c> (superseded by a newer
    /// version — still verifies historical QRs), <c>REVOKED</c>
    /// (compromised — must never verify).
    ///
    /// <para>
    /// KeyCustody-internal states (<c>GENERATING</c>, <c>PENDING</c>,
    /// <c>SUSPENDING</c>) do NOT appear here: keys are only published to
    /// the trust store once they reach <c>ACTIVE</c>, and the transition
    /// handler maps <c>SUSPENDING</c> → <c>SUSPENDED</c> at publish time.
    /// See <c>db/migrations/007_verification_consolidation.sql</c>.
    /// </para>
    ///
    /// <para>
    /// <see cref="IsValidForSigning"/> is the C16 gate: only <c>ACTIVE</c>
    /// passes; <c>RETIRED</c> passes only when <c>IncludeHistorical</c> is
    /// requested (signature fallback). <c>SUSPENDED</c> and <c>REVOKED</c>
    /// ALWAYS fail closed.
    /// </para>
    /// </summary>
    public string Status { get; set; } = "ACTIVE";

    /// <summary>Start of the validity window. Defaults to publish time.</summary>
    public DateTimeOffset ValidFrom { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// End of the validity window. Null on publish — opens the window
    /// until the next version supersedes or an explicit revocation closes it.
    /// </summary>
    public DateTimeOffset? ValidTo { get; set; }

    /// <summary>
    /// Set when the trust store reports a non-ACTIVE status for the
    /// institution's key. Distinct from <see cref="ValidTo"/>: a revocation
    /// is an event timestamp, not a window endpoint.
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>When the trust store (or local publish) last confirmed this row.</summary>
    public DateTimeOffset SyncedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsActive { get; set; } = true;

    /// <summary>Actor that created the row (system tag or client id).</summary>
    public string? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ModifiedBy { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }
}
