namespace SBQR.Modules.Tenancy.Application.Contracts;

/// <summary>
/// Page-shaped result envelope. Scoped to Tenancy.Application.Contracts —
/// not in <c>SBQR.SharedKernel</c> — because the paging shape (offset+limit,
/// 1-based pages) is a Tenancy module decision; promoting it to SharedKernel
/// would force every other module to either adopt this shape or work around
/// it. If a second module grows the same need, this is the time to extract
/// to SharedKernel.
///
/// <para>Mirrors what every admin-console consumer expects:
/// <c>?page=1&amp;pageSize=20</c> → <c>{ items: [...], page, pageSize,
/// totalCount, hasMore }</c>.</para>
/// </summary>
/// <typeparam name="T">Item type — usually the read-side projection
/// (e.g. <see cref="TenantResponse"/>).</typeparam>
/// <param name="Items">The slice for the requested page.</param>
/// <param name="Page">1-based page index that was requested.</param>
/// <param name="PageSize">Items per page that was requested.</param>
/// <param name="TotalCount">Total items matching the filter (not just this
/// page).</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>True if there are more items past this page.</summary>
    public bool HasMore => Page * PageSize < TotalCount;
}