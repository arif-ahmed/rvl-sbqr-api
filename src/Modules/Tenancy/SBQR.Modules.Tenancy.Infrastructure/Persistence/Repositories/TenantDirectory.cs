using SBQR.Modules.Tenancy.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;

/// <summary>
/// Default <see cref="ITenantDirectory"/>: resolves a tenant's public
/// identity through the Tenancy repository, never exposing the aggregate.
/// </summary>
public sealed class TenantDirectory : ITenantDirectory
{
    private readonly ITenantRepository _tenants;

    public TenantDirectory(ITenantRepository tenants)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    /// <inheritdoc/>
    public async Task<TenantPublicInfo?> LookupAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants
            .GetByIdAsync(new TenantId(tenantId), cancellationToken)
            .ConfigureAwait(false);

        return tenant is null ? null : ToPublicInfo(tenant);
    }

    /// <inheritdoc/>
    public async Task<TenantPublicInfo?> LookupByInstitutionCodeAsync(
        string institutionCode,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants
            .GetByInstitutionCodeAsync(institutionCode, cancellationToken)
            .ConfigureAwait(false);

        return tenant is null ? null : ToPublicInfo(tenant);
    }

    private static TenantPublicInfo ToPublicInfo(Tenant tenant) => new(
        TenantId: tenant.Id.Value,
        InstitutionCode: tenant.InstitutionCode,
        InstitutionName: tenant.InstitutionName,
        Admission: tenant.Status switch
        {
            TenantStatus.Pending => TenantAdmissionState.Pending,
            TenantStatus.Active => TenantAdmissionState.Active,
            TenantStatus.Suspended => TenantAdmissionState.Suspended,
            _ => TenantAdmissionState.Terminated,
        });
}
