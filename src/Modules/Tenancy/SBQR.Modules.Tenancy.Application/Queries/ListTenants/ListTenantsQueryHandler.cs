using MediatR;
using SBQR.Modules.Tenancy.Application.Contracts;
using SBQR.Modules.Tenancy.Application.Contracts.Mapping;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.ListTenants;

/// <summary>
/// Read-side handler for <see cref="ListTenantsQuery"/>. Pure projection:
/// loads a page of <see cref="Domain.Aggregates.Tenant"/> aggregates via
/// <see cref="ITenantRepository.ListAsync"/>, maps to
/// <see cref="TenantResponse"/>, and wraps in a <see cref="PagedTenantResponse"/>.
///
/// <para>
/// Like <see cref="GetTenantById.GetTenantByIdQueryHandler"/>, this handler
/// does NOT call <c>IUnitOfWork.SaveChangesAsync</c> or
/// <c>IAuditLogger.LogAsync</c>. The access log lives at the host pipeline
/// layer, not here.
/// </para>
/// </summary>
public sealed class ListTenantsQueryHandler : IRequestHandler<ListTenantsQuery, Result<PagedTenantResponse>>
{
    private readonly ITenantRepository _tenants;

    public ListTenantsQueryHandler(ITenantRepository tenants)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    public async Task<Result<PagedTenantResponse>> Handle(
        ListTenantsQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var filter = new TenantListQuery(
            Status: request.Status,
            IsActive: request.IsActive,
            Page: request.Page,
            PageSize: request.PageSize);

        var (items, totalCount) = await _tenants
            .ListAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        var projected = items.Select(TenantResponseBuilder.Build).ToList();

        return Result<PagedTenantResponse>.Ok(new PagedTenantResponse(
            Items: projected,
            Page: request.Page,
            PageSize: request.PageSize,
            TotalCount: totalCount,
            HasMore: request.Page * request.PageSize < totalCount));
    }
}