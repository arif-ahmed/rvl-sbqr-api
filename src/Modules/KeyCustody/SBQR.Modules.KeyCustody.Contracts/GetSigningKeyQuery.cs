using MediatR;

namespace SBQR.Modules.KeyCustody.Contracts;

/// <summary>
/// Cross-module query: resolve a tenant's current signing-key record
/// (metadata only — the public half and lifecycle status; never private
/// material). Consumers:
///
/// <list type="bullet">
///   <item><b>QrGeneration</b> — refuses to build a signed QR unless the key is
///         ACTIVE (HTTP 422 KEY_NOT_ACTIVE).</item>
///   <item><b>KeyCustody Admin API</b> — displays the current key status
///         in the crypto-keys listing endpoints.</item>
/// </list>
///
/// <para>
/// <b>Not</b> Verification: Verification resolves ALL issuers through the
/// InstitutionTrust trust store (spec Annex B) via
/// <see cref="InstitutionTrust.Contracts.GetInstitutionPublicKeyQuery"/>,
/// not through KeyCustody's own-custody path.
/// </para>
///
/// Returns the tenant's highest-version live key row regardless of status;
/// the caller interprets the status. Returns <c>null</c> when the tenant has
/// no key row at all (fail-closed consumers treat that as KEY_NOT_FOUND).
/// </summary>
public sealed record GetSigningKeyQuery(Guid TenantId) : IRequest<SigningKeyView?>;

/// <summary>Public-metadata view of one signing key. Private material never crosses this contract.</summary>
public sealed record SigningKeyView(
    Guid CryptoKeyId,
    Guid TenantId,
    string KeyId,
    int KeyVersion,
    string PublicKeyPem,
    string Status);
