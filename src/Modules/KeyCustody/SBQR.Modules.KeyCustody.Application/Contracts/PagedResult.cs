namespace SBQR.Modules.KeyCustody.Application.Contracts;

/// <summary>
/// Page-shaped result envelope, mirroring Tenancy's
/// <c>Application.Contracts.PagedResult&lt;T&gt;</c> shape for consistency
/// across the admin surface. Scoped to this module rather than SharedKernel
/// per the same rationale: promoting a shared paging shape is only worth it
/// once a third module needs it too.
/// </summary>
/// <param name="Items">The slice for the requested page.</param>
/// <param name="Page">1-based page index that was requested.</param>
/// <param name="PageSize">Items per page that was requested.</param>
/// <param name="TotalCount">Total items matching the filter (not just this page).</param>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    /// <summary>True if there are more items past this page.</summary>
    public bool HasMore => Page * PageSize < TotalCount;
}
