using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.KeyCustody.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.CryptoKey.Suspend"/> when a tenant
/// suspension cascades onto its currently-active signing key.
/// </summary>
public sealed record KeySuspended(
    Guid TenantId,
    CryptoKeyId CryptoKeyId,
    DateTimeOffset OccurredAt) : IDomainEvent;
