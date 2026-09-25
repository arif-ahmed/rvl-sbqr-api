using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.IdentityAccess.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.TenantConfiguration.Suspend"/> when a tenant
/// suspension cascades onto the configuration row and <c>status</c> moves to
/// <see cref="TenantConfigurationStatus.Suspended"/>. The event carries the owning
/// tenant id (opaque Guid — see <see cref="TenantConfigurationCreated"/>) so
/// subscribers can join without dereferencing the aggregate.
/// </summary>
public sealed record TenantConfigurationSuspended(
    Guid TenantId,
    TenantConfigurationId TenantConfigurationId,
    DateTimeOffset OccurredAt) : IDomainEvent;
