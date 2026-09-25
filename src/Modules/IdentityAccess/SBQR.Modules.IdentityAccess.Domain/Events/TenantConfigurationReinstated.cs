using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.IdentityAccess.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.TenantConfiguration.Reinstate"/> when a
/// previously suspended configuration returns to
/// <see cref="TenantConfigurationStatus.Active"/>.
/// </summary>
public sealed record TenantConfigurationReinstated(
    Guid TenantId,
    TenantConfigurationId TenantConfigurationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
