using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="TenantApplication.Suspend"/> when the per-app kill
/// switch is engaged. Does not affect sibling apps or the owning tenant.
/// </summary>
public sealed record TenantApplicationSuspended(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    string Actor,
    DateTimeOffset OccurredAt) : IDomainEvent;
