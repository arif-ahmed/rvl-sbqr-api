using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Domain;

namespace SBQR.Modules.Tenancy.Domain.Events;

/// <summary>
/// Raised by <see cref="TenantApplication.Reinstate"/> when a previously
/// suspended app is re-allowed.
/// </summary>
public sealed record TenantApplicationReinstated(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    string Actor,
    DateTimeOffset OccurredAt) : IDomainEvent;
