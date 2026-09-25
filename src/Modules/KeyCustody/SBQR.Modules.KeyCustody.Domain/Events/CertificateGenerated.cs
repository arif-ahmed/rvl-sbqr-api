using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.KeyCustody.Domain.Events;

/// <summary>
/// Raised by <see cref="CryptoKey.Generate"/> (Scenario A — no caller-supplied
/// keys). Carries only the public surface of the new key; private material is
/// never included in domain events.
/// </summary>
public sealed record CertificateGenerated(
    Guid TenantId,
    CryptoKeyId CryptoKeyId,
    string KeyId,
    int KeyVersion,
    string PublicKeySha256,
    DateTimeOffset OccurredAt) : IDomainEvent;
