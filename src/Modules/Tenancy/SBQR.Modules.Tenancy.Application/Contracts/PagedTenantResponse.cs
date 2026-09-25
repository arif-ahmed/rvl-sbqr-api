namespace SBQR.Modules.Tenancy.Application.Contracts;

/// <summary>
/// Wire shape for <c>GET /v1/admin/tenants</c>. Reuses <see cref="TenantResponse"/>
/// verbatim for the items so the per-tenant shape stays identical between
/// list and single-get responses.
/// </summary>
/// <param name="Items">Page of tenant projections.</param>
/// <param name="Page">1-based page index returned.</param>
/// <param name="PageSize">Items per page that were returned.</param>
/// <param name="TotalCount">Total tenants matching the filter.</param>
/// <param name="HasMore"><c>true</c> when <c>Page * PageSize &lt; TotalCount</c>.</param>
public sealed record PagedTenantResponse(
    IReadOnlyList<TenantResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore);