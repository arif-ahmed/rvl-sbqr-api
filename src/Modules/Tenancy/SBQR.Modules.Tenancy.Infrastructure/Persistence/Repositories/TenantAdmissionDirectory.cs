using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;

/// <summary>
/// Default implementation of <see cref="ITenantAdmissionDirectory"/> for the
/// Tenancy bounded context. Translates the Tenancy-native <see cref="TenantStatus"/>
/// into the cross-module <see cref="TenantAdmissionState"/> vocabulary that
/// the IdentityAccess module's OAuth 2.1 token handler needs to decide whether
/// to mint a tenant-scoped token.
///
/// <para>Registered by <see cref="Api.TenancyModule"/> as scoped. The lookup goes
/// through <see cref="ITenantRepository"/> (no EF Core reference here — this
/// class lives in Infrastructure where the repository interface is visible).</para>
/// </summary>
public sealed class TenantAdmissionDirectory : ITenantAdmissionDirectory
{
    private readonly ITenantRepository _tenants;

    public TenantAdmissionDirectory(ITenantRepository tenants)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    /// <inheritdoc/>
    public async Task<TenantAdmissionState?> GetAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenants
            .GetByIdAsync(new TenantId(tenantId), cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return null;
        }

        // Maps 1:1 to TenantAdmissionState (see ITenantAdmissionDirectory).
        // TenantStatus.Pending admits authentication by design so the register
        // flow is exercisable end-to-end before BB activation.
        return tenant.Status switch
        {
            TenantStatus.Pending => TenantAdmissionState.Pending,
            TenantStatus.Active => TenantAdmissionState.Active,
            TenantStatus.Suspended => TenantAdmissionState.Suspended,
            _ => TenantAdmissionState.Terminated,
        };
    }
}
