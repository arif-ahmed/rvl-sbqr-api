using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.ReinstateTenantApplication;

/// <summary>
/// Result of <see cref="ReinstateTenantApplicationCommand"/>. Carries the
/// post-transition snapshot the controller projects into a
/// <c>TenantApplicationResponse</c>.
/// </summary>
public sealed record ReinstateTenantApplicationResult(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    DateTimeOffset ReinstatedAt);
