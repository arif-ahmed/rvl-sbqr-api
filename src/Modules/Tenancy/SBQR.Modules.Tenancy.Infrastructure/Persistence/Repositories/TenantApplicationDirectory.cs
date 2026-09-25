using SBQR.Modules.Tenancy.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;

/// <summary>
/// Default implementation of <see cref="ITenantApplicationDirectory"/> for the
/// Tenancy bounded context. Looks up the existence of an active
/// <see cref="TenantApplication"/> row by delegating to
/// <see cref="ITenantApplicationRepository"/>, which holds the EF Core
/// query — this class stays an inert translation layer, mirroring
/// <see cref="TenantAdmissionDirectory"/>.
/// </summary>
public sealed class TenantApplicationDirectory : ITenantApplicationDirectory
{
    private readonly ITenantApplicationRepository _applications;

    public TenantApplicationDirectory(ITenantApplicationRepository applications)
    {
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
    }

    /// <inheritdoc/>
    public Task<bool> IsAllowedAsync(
        Guid tenantId,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
        {
            return Task.FromResult(false);
        }

        if (string.IsNullOrWhiteSpace(packageId))
        {
            return Task.FromResult(false);
        }

        return _applications.ExistsActiveForTenantAsync(
            new TenantId(tenantId),
            packageId,
            cancellationToken);
    }
}
