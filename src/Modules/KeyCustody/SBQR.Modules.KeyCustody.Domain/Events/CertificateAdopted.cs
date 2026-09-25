using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.KeyCustody.Domain.Events;

/// <summary>
/// Raised by <see cref="CryptoKey.Adopt"/> (Scenario B — caller-supplied PEMs).
/// Carries only the public surface of the new key; private material is never
/// included in domain events.
/// </summary>
public sealed record CertificateAdopted(
    Guid TenantId,
    CryptoKeyId CryptoKeyId,
    string KeyId,
    int KeyVersion,
    string PublicKeySha256,
    DateTimeOffset OccurredAt) : IDomainEvent;
