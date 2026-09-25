namespace SBQR.Modules.KeyCustody.Api.Contracts;

/// <summary>
/// Wire DTO for <c>POST /v1/crypto-keys</c>. <c>Mode</c> is
/// <c>"Generate"</c> (server mints a fresh Ed25519 keypair) or
/// <c>"Adopt"</c> (caller supplies only the private-key PEM; the public
/// half is read from <c>public.institution_keys</c> by the
/// drift guard, so the operator MUST pre-seed that row via
/// <c>POST /v1/admin/institutions</c> before invoking Adopt) — case-insensitive.
/// <see cref="PrivateKeyPem"/> is required only when <c>Mode</c> is
/// <c>"Adopt"</c>.
/// </summary>
public sealed record CreateCryptoKeyRequest(
    Guid TenantId,
    string Mode,
    string? PrivateKeyPem = null);
