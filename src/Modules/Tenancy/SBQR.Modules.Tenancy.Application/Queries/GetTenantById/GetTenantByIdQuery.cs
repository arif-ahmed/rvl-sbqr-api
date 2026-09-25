using MediatR;
using SBQR.Modules.Tenancy.Application.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.GetTenantById;

/// <summary>
/// MediatR query for <c>GET /v1/admin/tenants/{id}</c>. Returns the post-load
/// <see cref="TenantResponse"/> projection, or
/// <see cref="ErrorCode.NotFound"/> if no row matches the supplied
/// <see cref="TenantId"/>.
/// </summary>
public sealed record GetTenantByIdQuery(TenantId TenantId) : IRequest<Result<TenantResponse>>;
