using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.KeyCustody.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.CryptoKey.Retire"/> when a key is taken out
/// of service — typically because <c>PUT /v1/crypto-keys/{tenantId}</c>
/// rotated the tenant onto a new key. Retired keys still verify historical QRs;
/// they simply stop being eligible for new signing.
/// </summary>
public sealed record KeyRetired(
    Guid TenantId,
    CryptoKeyId CryptoKeyId,
    DateTimeOffset OccurredAt) : IDomainEvent;
