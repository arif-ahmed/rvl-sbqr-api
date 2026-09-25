using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;

/// <summary>
/// Result of <see cref="RegisterTenantApplicationCommand"/>. Carries the freshly
/// assigned <see cref="TenantApplicationId"/> and the seed values the admin
/// endpoint projects back into the response body.
/// </summary>
public sealed record RegisterTenantApplicationResult(
    TenantApplicationId TenantApplicationId,
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId,
    DateTimeOffset RegisteredAt);
