using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.Tenant.Register"/> when a brand-new tenant is
/// successfully instantiated. Carries the seed values so subscribers (Audit module)
/// can record what was created, not just the id.
/// </summary>
/// <remarks>
/// Distinct from the lifecycle events (<see cref="TenantActivated"/>,
/// <see cref="TenantSuspended"/>, <see cref="TenantDeactivated"/>): this event
/// records *creation*, not a status transition.
/// </remarks>
public sealed record TenantRegistered(
    TenantId TenantId,
    string InstitutionName,
    string InstitutionCode,
    DateTimeOffset OccurredAt) : IDomainEvent;