using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Domain.Interfaces;

/// <summary>
/// Repository port for the <see cref="TenantApplication"/> aggregate.
/// Composes <see cref="IRepository{T}"/> for generic add/load/update and adds
/// the narrow lookups the bounded context actually needs:
/// <list type="bullet">
///   <item><see cref="GetByIdAsync"/> — single-row load for admin suspend/reinstate flows.</item>
///   <item><see cref="ExistsActiveForTenantAsync"/> — the auth hot-path
///         existence check used by <c>ITenantApplicationDirectory</c>.</item>
///   <item><see cref="ListByTenantAsync"/> — read-side paged list for the
///         admin endpoint.</item>
/// </list>
/// Implementations live in <c>SBQR.Modules.Tenancy.Infrastructure/Persistence/</c>.
/// </summary>
public interface ITenantApplicationRepository : IRepository<TenantApplication>
{
    /// <summary>
    /// Auth hot-path existence check: is there an <c>ACTIVE</c> row for this
    /// tenant whose <c>package_id</c> matches and whose <c>is_active</c> flag
    /// is <c>true</c>? Used by <c>ITenantApplicationDirectory.IsAllowedAsync</c>
    /// at every <c>POST /v1/oauth/token</c> call where a <c>package_id</c> was
    /// claimed. The DB partial index <c>ix_tenant_applications_active</c>
    /// keeps this an index hit.
    /// </summary>
    /// <param name="tenantId">Owning tenant's id.</param>
    /// <param name="packageId">The mobile package identifier claimed by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when an active row exists, otherwise <c>false</c>.</returns>
    Task<bool> ExistsActiveForTenantAsync(
        TenantId tenantId,
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-side list of every registered application for one tenant,
    /// regardless of <see cref="TenantApplication.Status"/> or
    /// <see cref="TenantApplication.IsActive"/>. The admin endpoint projects
    /// this into <c>ListTenantApplicationsResponse</c>.
    /// </summary>
    /// <param name="tenantId">Owning tenant's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<TenantApplication>> ListByTenantAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default);
}
