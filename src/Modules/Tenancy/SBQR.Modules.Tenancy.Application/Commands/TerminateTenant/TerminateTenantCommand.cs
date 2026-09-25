using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/terminate</c>. Moves the
/// tenant from any non-<see cref="TenantStatus.Terminated"/> state into the
/// terminal <see cref="TenantStatus.Terminated"/> state and sets
/// <see cref="Tenant.IsActive"/> to <c>false</c> atomically.
///
/// <para>The handler is responsible for:</para>
/// <list type="number">
///   <item>Loading the tenant and its currently-<see cref="CryptoKeyStatus.Active"/>
///         signing key.</item>
///   <item>Calling <see cref="Tenant.Deactivate"/> on the aggregate, which raises
///         <see cref="Domain.Events.TenantDeactivated"/>.</item>
///   <item>Cascade-suspending the active <see cref="Domain.Aggregates.CryptoKey"/>
///         in the same unit-of-work, and cascading the credential suspension
///         through the IdentityAccess Contracts seam.</item>
///   <item>Writing one <see cref="IAuditLogger"/> entry per Tenancy-side domain
///         event raised, plus a cross-module breadcrumb if any credentials were
///         cascaded.</item>
/// </list>
///
/// <para>This mirrors <see cref="SuspendTenant.SuspendTenantCommand"/>'s shape
/// exactly so the two lifecycle endpoints read consistently.</para>
/// </summary>
public sealed record TerminateTenantCommand(
    TenantId TenantId,
    string? Reason = null) : IRequest<Result<TerminateTenantResult>>;