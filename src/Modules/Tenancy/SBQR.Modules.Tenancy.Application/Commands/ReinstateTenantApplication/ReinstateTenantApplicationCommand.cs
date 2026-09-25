using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.ReinstateTenantApplication;

/// <summary>
/// MediatR command for
/// <c>POST /v1/admin/tenants/{tenantId}/applications/{applicationId}/reinstate</c>.
/// Counterpart to <see cref="SuspendTenantApplication.SuspendTenantApplicationCommand"/>.
/// </summary>
public sealed record ReinstateTenantApplicationCommand(
    TenantId TenantId,
    TenantApplicationId TenantApplicationId) : IRequest<Result<ReinstateTenantApplicationResult>>;
