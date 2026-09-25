namespace SBQR.Modules.KeyCustody.Application.Contracts;

/// <summary>
/// Public-metadata projection of a <see cref="Domain.Aggregates.CryptoKey"/>.
/// Private material and <c>custody_key_reference</c> never cross this
/// boundary — this is the shape the <c>api/v1/crypto-keys</c> HTTP surface
/// returns.
/// </summary>
public sealed record CryptoKeySummary(
    Guid CryptoKeyId,
    Guid TenantId,
    string KeyId,
    int KeyVersion,
    string PublicKeyPem,
    string Status,
    bool IsActive,
    DateTimeOffset CreatedAt);
