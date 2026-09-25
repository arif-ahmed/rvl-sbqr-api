using MediatR;
using SBQR.Modules.Tenancy.Application.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.ListTenants;

/// <summary>
/// MediatR query for <c>GET /v1/admin/tenants</c>. Returns a paged list of
/// <see cref="TenantResponse"/> projections, optionally filtered by
/// <see cref="TenantStatus"/> and/or <see cref="Tenant.IsActive"/>.
///
/// <para>Pagination is 1-based (page 1 is the first page). The validator
/// enforces bounds (page ≥ 1, 1 ≤ pageSize ≤ 100).</para>
/// </summary>
/// <param name="Status">Optional status filter.</param>
/// <param name="IsActive">Optional soft-delete flag filter.</param>
/// <param name="Page">1-based page index.</param>
/// <param name="PageSize">Items per page.</param>
public sealed record ListTenantsQuery(
    TenantStatus? Status,
    bool? IsActive,
    int Page,
    int PageSize) : IRequest<Result<PagedTenantResponse>>;