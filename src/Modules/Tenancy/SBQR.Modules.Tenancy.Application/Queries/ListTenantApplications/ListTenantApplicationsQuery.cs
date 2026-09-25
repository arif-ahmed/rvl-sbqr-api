using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.ListTenantApplications;

/// <summary>
/// Read-side MediatR query for
/// <c>GET /v1/admin/tenants/{id}/applications</c>. Pure projection: load the
/// rows, map to <see cref="TenantApplicationListItem"/>, return. Returns
/// <see cref="ErrorCode.NotFound"/> when the owning tenant does not exist
/// (we explicitly probe the tenant so the admin endpoint surfaces 404 vs.
/// an empty list — the caller is asking about a specific tenant).
/// </summary>
public sealed record ListTenantApplicationsQuery(
    TenantId TenantId) : IRequest<Result<IReadOnlyList<TenantApplicationListItem>>>;

/// <summary>
/// Internal projection of <see cref="TenantApplication"/> carried across the
/// Application → Api boundary by the list query. The controller then maps
/// each item into the public <c>SBQR.Modules.Tenancy.Api.Contracts.
/// TenantApplicationResponse</c> DTO so the wire shape stays owned by the
/// API layer and this projection remains a pure internal carrier.
/// </summary>
public sealed record TenantApplicationListItem(
    Guid TenantApplicationId,
    Guid TenantId,
    string Platform,
    string PackageId,
    string Status,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ModifiedAt);
