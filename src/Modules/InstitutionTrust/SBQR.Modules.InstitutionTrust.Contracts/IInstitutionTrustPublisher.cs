using System.Text.Json;

namespace SBQR.Modules.InstitutionTrust.Contracts;

/// <summary>
/// Cross-module seam that lets another bounded context (today: KeyCustody)
/// publish state into the InstitutionTrust trust directory without
/// taking a dependency on InstitutionTrust.Application or .Infrastructure.
///
/// <para>
/// Two operations are exposed:
/// <list type="bullet">
///   <item><see cref="PublishActivePublicKeyAsync"/> — publish a freshly
///       minted key as ACTIVE (retires the previous active). Called by
///       KeyCustody's Generate/Adopt handler.</item>
///   <item><see cref="UpdateKeyStatusAsync"/> — propagate a lifecycle
///       transition (suspend / retire / revoke / reinstate) onto the
///       currently-ACTIVE trust-directory row. Called by KeyCustody's
///       Suspend / Rotate / Reinstate handlers so Verification (which
///       reads only InstitutionTrust) sees the same status.</item>
/// </list>
/// </para>
///
/// <para>
/// This is a thin facade over <c>InstitutionUpsertService</c>. The
/// trust-directory wire contract, audit-row shape, and
/// register-if-missing / retire-previous-active / write-new-active
/// semantics are all unchanged; the publisher just gives other modules a
/// seam that does not require them to know about MediatR.
/// </para>
///
/// <para>
/// <b>Failure contract (Phase 6 — mandatory publish).</b> The KeyCustody
/// mint handler sequences its own DB commit first, then calls
/// <see cref="PublishActivePublicKeyAsync"/>. If that throws, the handler
/// returns a failure result and emits a
/// <c>crypto_key.trust_publish_failed</c> audit row. The key row is
/// preserved — the operator can recover with one manual
/// <c>POST /v1/admin/institutions</c> call rather than re-minting.
/// <b>No compensating delete/retire</b>.
/// </para>
///
/// <para>
/// KeyCustody and InstitutionTrust own separate <c>DbContext</c>s, so there
/// is no shared transaction — the handler sequences the writes inside the
/// method body (KeyCustody writes first, then the publisher writes).
/// </para>
/// </summary>
public interface IInstitutionTrustPublisher
{
    /// <summary>
    /// Audit-row actor label for cross-module writes triggered by KeyCustody
    /// lifecycle handlers (Generate/Adopt/Rotate/Suspend/Reactivate).
    /// Published in <c>institution_keys.created_by</c> / <c>modified_by</c>.
    /// Distinguishes from manual admin upserts (<c>"client:{id}"</c>) and
    /// from the daily trust-sync service (<c>"system:trust-sync"</c>).
    /// </summary>
    public const string CryptoCreateActor = "system:crypto-create";

    /// <summary>
    /// Publish a freshly minted SPKI public-key PEM as the institution's
    /// new ACTIVE trust-directory key. Retires the previous ACTIVE key
    /// (if any) and creates a new key row at <c>oldVersion + 1</c>.
    /// </summary>
    /// <param name="institutionCode">Six-digit BB institution code.</param>
    /// <param name="instituteType">Two-digit Annex A Institution Type
    /// (Tag 26 sub 01 — the BB InstitutionId prefix). Carried across the
    /// seam rather than re-derived so callers can propagate a BB
    /// correction out-of-band.</param>
    /// <param name="institutionName">Human-readable display name
    /// (NOT NULL on the row).</param>
    /// <param name="publicKeyPem">SPKI public-key PEM (Ed25519 today;
    /// algorithm-agnostic to this seam).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The trust-store version now ACTIVE plus the public-key SHA-256 fingerprint.</returns>
    Task<PublishedTrustKey> PublishActivePublicKeyAsync(
        string institutionCode,
        string instituteType,
        string institutionName,
        string publicKeyPem,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Propagate a KeyCustody lifecycle transition onto the institution's
    /// currently ACTIVE trust-directory row. Called after KeyCustody
    /// suspends, retires, or reinstates a tenant signing key so the trust
    /// store stays in sync with the authoritative KeyCustody state.
    ///
    /// <para>
    /// Transitions accepted: <c>ACTIVE</c> (reinstate),
    /// <c>SUSPENDED</c>, <c>RETIRED</c>, <c>REVOKED</c>. A no-op
    /// when no ACTIVE row exists for the institution (the key may have
    /// already been superseded or never published).
    /// </para>
    /// </summary>
    /// <param name="institutionCode">Six-digit BB institution code.</param>
    /// <param name="newStatus">The status to set (must be one of the
    /// trust-store vocabulary: ACTIVE, SUSPENDED, RETIRED, REVOKED).</param>
    /// <param name="actorId">Audit actor tag (e.g.
    /// <see cref="InstitutionTrustPublisher.CryptoCreateActor"/> or
    /// <c>"system:trust-sync"</c>).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of rows updated (0 = no ACTIVE row found, a no-op).</returns>
    Task<int> UpdateKeyStatusAsync(
        string institutionCode,
        string newStatus,
        string actorId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read the public-key SHA-256 fingerprint of the institution's currently
    /// ACTIVE trust-directory row, or <c>null</c> if no ACTIVE row exists.
    /// Used by KeyCustody's Adopt-mode drift guard.
    /// </summary>
    /// <param name="institutionCode">Six-digit BB institution code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The lower-case hex SHA-256 of the public-key PEM when an ACTIVE row
    /// exists; <c>null</c> otherwise.
    /// </returns>
    Task<string?> GetActivePublicKeySha256Async(
        string institutionCode,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal return shape so the caller (KeyCustody handler) can include the
/// trust-store version + public-key fingerprint in its own audit row.
/// The plaintext public key is NOT returned — the caller already holds it.
/// </summary>
/// <param name="ActiveKeyVersion">The trust-store version now ACTIVE for the institution.</param>
/// <param name="PublicKeySha256">SHA-256 hex of the public-key PEM, identical to
/// <c>institution_keys.public_key_sha256</c>.</param>
public sealed record PublishedTrustKey(
    int ActiveKeyVersion,
    string PublicKeySha256);
