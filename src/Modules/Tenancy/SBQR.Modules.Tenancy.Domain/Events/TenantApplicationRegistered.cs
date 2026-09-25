using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="TenantApplication.Register"/> when a brand-new
/// tenant application is successfully instantiated. Carries the seed values
/// so the Audit module can record what was registered, not just the id.
/// </summary>
public sealed record TenantApplicationRegistered(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    string Actor,
    DateTimeOffset OccurredAt) : IDomainEvent;
