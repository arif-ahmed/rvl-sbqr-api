using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="Aggregates.Tenant.Activate"/> when a tenant successfully
/// transitions to <see cref="Aggregates.TenantStatus.Active"/>.
/// </summary>
public sealed record TenantActivated(
    TenantId TenantId,
    string Actor,
    DateTimeOffset OccurredAt) : IDomainEvent;
