using SBQR.Modules.IdentityAccess.Domain.Aggregates;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Domain.Interfaces;

/// <summary>
/// Repository port for the <see cref="TenantConfiguration"/> aggregate. Composes
/// <see cref="IRepository{T}"/> for generic add/load/update and adds the narrow
/// lookups the bounded context actually needs:
/// <list type="bullet">
///   <item><see cref="GetByClientIdAsync"/> — token-endpoint lookup.</item>
///   <item><see cref="GetActiveByTenantAsync"/> — uniqueness pre-check at issuance time.</item>
///   <item><see cref="GetSuspendedByTenantAsync"/> — reactivate cascade.</item>
///   <item><see cref="GetAllByTenantAsync"/> — the suspend/reactivate cascades
///         over every non-terminated configuration row of a tenant.</item>
/// </list>
/// Implementations live in <c>SBQR.Modules.IdentityAccess.Infrastructure/Persistence/</c>.
/// </summary>
public interface ITenantConfigurationRepository : IRepository<TenantConfiguration>
{
    /// <summary>Look up a configuration row by its opaque <c>client_id</c>.</summary>
    /// <param name="clientId">The client identifier presented at the token endpoint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TenantConfiguration?> GetByClientIdAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the currently-<see cref="TenantConfigurationStatus.Active"/> configuration
    /// for a tenant, or <c>null</c> if none exists. Used at provision time to
    /// assert the partial-UQ invariant in
    /// <c>docs/design/database-design.md</c> §1.8 (one ACTIVE per tenant; the
    /// rotation story lifts this to "one non-EXPIRED").
    /// </summary>
    Task<TenantConfiguration?> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the currently-<see cref="TenantConfigurationStatus.Suspended"/> configuration
    /// for a tenant, or <c>null</c> if none exists. Used by the reactivate
    /// cascade to find the child row whose status must be flipped back to ACTIVE.
    /// </summary>
    Task<TenantConfiguration?> GetSuspendedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every non-soft-deleted configuration of a tenant regardless of status —
    /// the tenant-wide cascade surface used when a tenant is suspended or
    /// reinstated from the Tenancy module.
    /// </summary>
    Task<IReadOnlyList<TenantConfiguration>> GetAllByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);
}
