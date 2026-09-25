using MediatR;
using SBQR.Modules.Tenancy.Application.Contracts;
using SBQR.Modules.Tenancy.Application.Contracts.Mapping;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Queries.GetTenantById;

/// <summary>
/// Read-side handler for <see cref="GetTenantByIdQuery"/>. Pure projection:
/// load the aggregate, map to <see cref="TenantResponse"/>, return.
///
/// <para>
/// This handler does NOT write any state: no <c>IUnitOfWork.SaveChangesAsync</c>,
/// no <c>IAuditLogger.LogAsync</c>. The audit trail for "read" is the access log,
/// which lives at the host pipeline layer (see <c>SBQR.Api/Program.cs</c>) — not
/// here. The unit test pins this contract so a future maintainer doesn't add a
/// redundant audit row.
/// </para>
/// </summary>
public sealed class GetTenantByIdQueryHandler : IRequestHandler<GetTenantByIdQuery, Result<TenantResponse>>
{
    private readonly ITenantRepository _tenants;

    public GetTenantByIdQueryHandler(ITenantRepository tenants)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
    }

    public async Task<Result<TenantResponse>> Handle(
        GetTenantByIdQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<TenantResponse>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        return Result<TenantResponse>.Ok(TenantResponseBuilder.Build(tenant));
    }
}