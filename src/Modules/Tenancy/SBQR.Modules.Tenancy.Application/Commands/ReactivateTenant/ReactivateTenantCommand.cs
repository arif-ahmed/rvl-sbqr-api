using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/reactivate</c>. Reverses a
/// previous <see cref="SuspendTenant.SuspendTenantCommand"/>: moves the
/// aggregate out of <see cref="TenantStatus.Suspended"/> back into
/// <see cref="TenantStatus.Active"/> and cascades the same transition onto the
/// suspended signing key, then fires the configuration reinstate cascade through
/// the IdentityAccess Contracts seam (the <c>tenant_configurations</c> aggregate
/// is owned by IdentityAccess — it no longer lives here).
///
/// <para>The handler is responsible for:</para>
/// <list type="number">
///   <item>Loading the tenant and its currently-<see cref="CryptoKeyStatus.Suspended"/>
///         signing key.</item>
///   <item>Calling <see cref="Tenant.Activate"/> on the aggregate, which raises
///         <see cref="Domain.Events.TenantActivated"/>.</item>
///   <item>Driving the configuration reinstate cascade via
///         <c>SBQR.Modules.IdentityAccess.Contracts.ITenantConfigurationProvisioner.ReinstateAllForTenantAsync</c>.</item>
///   <item>Writing per-event <see cref="IAuditLogger"/> entries.</item>
/// </list>
/// </summary>
public sealed record ReactivateTenantCommand(
    TenantId TenantId) : IRequest<Result<ReactivateTenantResult>>;
