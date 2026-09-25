using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/activate</c>. Moves the
/// tenant from <see cref="TenantStatus.Pending"/> or
/// <see cref="TenantStatus.Suspended"/> into <see cref="TenantStatus.Active"/>.
///
/// <para>
/// This is a state-machine entry, NOT a suspend-reversal: it does NOT cascade
/// onto the tenant's <c>api_credentials</c> rows. Use
/// <see cref="ReactivateTenant.ReactivateTenantCommand"/> for the
/// <c>Suspended → Active</c> path that should reinstate suspended credentials
/// and signing keys.
/// </para>
/// </summary>
public sealed record ActivateTenantCommand(TenantId TenantId) : IRequest<Result<ActivateTenantResult>>;