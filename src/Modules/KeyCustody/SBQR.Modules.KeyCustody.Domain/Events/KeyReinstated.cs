using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.KeyCustody.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.CryptoKey.Reinstate"/> when a previously
/// suspended signing key returns to <see cref="CryptoKeyStatus.Active"/>.
/// </summary>
public sealed record KeyReinstated(
    Guid TenantId,
    CryptoKeyId CryptoKeyId,
    DateTimeOffset OccurredAt) : IDomainEvent;
