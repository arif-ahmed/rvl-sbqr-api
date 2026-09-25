using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenantApplication;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{tenantId}/applications/{applicationId}/suspend</c>.
/// Carries the target <see cref="TenantId"/> + <see cref="TenantApplicationId"/>.
/// The handler drives the <see cref="TenantApplication.Suspend"/> transition
/// and persists the change; the audit interceptor stamps
/// <c>modified_by/at</c>.
/// </summary>
public sealed record SuspendTenantApplicationCommand(
    TenantId TenantId,
    TenantApplicationId TenantApplicationId) : IRequest<Result<SuspendTenantApplicationResult>>;
