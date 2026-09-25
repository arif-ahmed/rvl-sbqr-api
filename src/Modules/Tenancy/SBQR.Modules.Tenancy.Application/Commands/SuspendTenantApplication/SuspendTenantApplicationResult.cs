using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenantApplication;

/// <summary>
/// Result of <see cref="SuspendTenantApplicationCommand"/>. Carries the
/// post-transition snapshot the controller projects into a
/// <c>TenantApplicationResponse</c>.
/// </summary>
public sealed record SuspendTenantApplicationResult(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    DateTimeOffset SuspendedAt);
