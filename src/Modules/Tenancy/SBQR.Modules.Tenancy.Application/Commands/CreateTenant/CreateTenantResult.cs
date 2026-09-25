using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.CreateTenant;

/// <summary>
/// Application-side result of <see cref="CreateTenantCommand"/>. Carries only
/// the freshly assigned <see cref="TenantId"/>; client-credential issuance and
/// signing-key generation are handled by separate, dedicated endpoints.
/// </summary>
/// <param name="TenantId">The new tenant id.</param>
public sealed record CreateTenantResult(TenantId TenantId);
