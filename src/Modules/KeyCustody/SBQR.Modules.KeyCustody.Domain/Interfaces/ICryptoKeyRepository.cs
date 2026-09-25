using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.KeyCustody.Domain.Interfaces;

/// <summary>
/// Repository port for the <see cref="CryptoKey"/> aggregate. Composes
/// <see cref="IRepository{T}"/> for generic add/load/update and adds the
/// narrow lookups the lifecycle handlers need.
/// Implementations live in <c>SBQR.Modules.KeyCustody.Infrastructure/Persistence/</c>.
/// </summary>
public interface ICryptoKeyRepository : IRepository<CryptoKey>
{
    /// <summary>
    /// Return the currently-<see cref="CryptoKeyStatus.Active"/> key for a
    /// tenant, or <c>null</c> if none exists.
    /// </summary>
    Task<CryptoKey?> GetActiveByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the currently-<see cref="CryptoKeyStatus.Suspended"/> key for a
    /// tenant, or <c>null</c> if none exists. Used by the reactivate cascade
    /// to find the child row whose status must be flipped back to ACTIVE.
    /// </summary>
    Task<CryptoKey?> GetSuspendedByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the highest-version row for a tenant regardless of status, or
    /// <c>null</c> if the tenant has no key at all. Used to compute the next
    /// <c>KeyVersion</c> at rotation time.
    /// </summary>
    Task<CryptoKey?> GetLatestByTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-side paged query. Returns the page slice and the total count of
    /// rows matching the filter (so the caller can build a paged response).
    /// </summary>
    /// <param name="tenantId">Optional tenant filter — <c>null</c> lists across all tenants.</param>
    /// <param name="status">Optional lifecycle-status filter.</param>
    /// <param name="page">1-based page index.</param>
    /// <param name="pageSize">Items per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A tuple of (page slice, total count of rows matching the filter).</returns>
    Task<(IReadOnlyList<CryptoKey> Items, int TotalCount)> ListAsync(
        Guid? tenantId,
        CryptoKeyStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
