using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Domain.Interfaces;

/// <summary>
/// Repository port for the <see cref="Tenant"/> aggregate. Composes
/// <see cref="IRepository{T}"/> for generic add/load/update and adds the narrow
/// lookups the bounded context actually needs:
/// <list type="bullet">
///   <item><see cref="GetByInstitutionCodeAsync"/> — overlap lookup vs the trust directory.</item>
///   <item><see cref="ListAsync"/> — paged read-side query with optional filters.</item>
/// </list>
/// Implementations live in <c>SBQR.Modules.Tenancy.Infrastructure/Persistence/</c>
/// (Story 2), backed by EF Core.
/// </summary>
public interface ITenantRepository : IRepository<Tenant>
{
    /// <summary>
    /// Look up a tenant by its logical link to the BB institution registry. The
    /// <c>institution_code</c> is the natural unique key for the aggregate —
    /// it mirrors <c>institution_registries.institution_code</c>. Multiple
    /// tenants should never share an institution_code while active (guarded
    /// by a unique constraint on <c>tenants.institution_code</c>).
    /// </summary>
    /// <param name="institutionCode">The BB-assigned institution code (6 digits).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The tenant, or <c>null</c> if no row matches.</returns>
    Task<Tenant?> GetByInstitutionCodeAsync(string institutionCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-side paged query. Returns the page slice and the total count of
    /// rows matching the filter (so the caller can build a paged response).
    ///
    /// <para>
    /// Returning the tuple keeps <see cref="ITenantRepository"/> in the
    /// Domain layer without dragging the <c>Application.Contracts.PagedResult</c>
    /// shape across the boundary — the Application handler composes the
    /// final <c>PagedResult&lt;TenantResponse&gt;</c> from these two values.
    /// </para>
    /// </summary>
    /// <param name="query">Filter + paging parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A tuple of (page slice, total count of rows matching the filter).
    /// Empty slice + zero count when no rows match.
    /// </returns>
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> ListAsync(
        TenantListQuery query,
        CancellationToken cancellationToken = default);
}