using SBQR.Modules.KeyCustody.Domain.Aggregates;

namespace SBQR.Modules.KeyCustody.Application.Contracts;

/// <summary>
/// Single source of truth for projecting a <see cref="CryptoKey"/> aggregate
/// onto the public <see cref="CryptoKeySummary"/> shape.
/// </summary>
public static class CryptoKeySummaryBuilder
{
    public static CryptoKeySummary Build(CryptoKey key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return new CryptoKeySummary(
            CryptoKeyId: key.Id.Value,
            TenantId: key.TenantId,
            KeyId: key.KeyId,
            KeyVersion: key.KeyVersion,
            PublicKeyPem: key.PublicKey,
            Status: key.Status.ToString().ToUpperInvariant(),
            IsActive: key.IsActive,
            CreatedAt: key.CreatedAt);
    }
}
