using MediatR;

namespace SBQR.Modules.InstitutionTrust.Contracts;

/// <summary>
/// Cross-module query: resolve an institution's public key by its six-digit
/// institution code (Tag 26 sub 01 ‖ sub 02 per Annex B step 2).
/// Consumed by Verification's <c>qr/validate</c> endpoint — the single
/// authority for ALL issuer key resolution (no KeyCustody bypass).
///
/// <para>
/// By default (IncludeHistorical = false) only <c>ACTIVE</c> keys that pass
/// the C16 temporal gate (ValidFrom ≤ now ≤ ValidTo, not revoked) are
/// returned. When signature verification against the active key fails,
/// Verification retries with IncludeHistorical = true to pick up the latest
/// <c>RETIRED</c> key — retired keys still verify historical QRs but
/// cannot sign new ones.
/// </para>
///
/// Returns <c>null</c> when no matching key exists or all candidates are
/// revoked/suspended — the caller decides the fail-closed verdict.
/// </summary>
public sealed record GetInstitutionPublicKeyQuery(
    string InstitutionCode,
    bool IncludeHistorical = false) : IRequest<InstitutionPublicKeyView?>;

/// <summary>
/// Public-key view from the trust directory. Metadata only — the plaintext
/// public key PEM is included because the verifier needs it to reconstruct
/// the Ed25519 signature check.
/// </summary>
public sealed record InstitutionPublicKeyView(
    string InstitutionCode,
    int KeyVersion,
    string PublicKeyPem,
    string Status);
