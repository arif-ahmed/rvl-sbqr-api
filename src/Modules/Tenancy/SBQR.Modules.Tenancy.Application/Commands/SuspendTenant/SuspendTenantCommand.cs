using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/suspend</c>. Carries the
/// target <see cref="TenantId"/> and an optional human-readable reason (≤500
/// chars, validated by <see cref="SuspendTenantValidator"/>).
///
/// <para>The handler is responsible for:</para>
/// <list type="number">
///   <item>Loading the tenant and its currently-<see cref="CryptoKeyStatus.Active"/>
///         signing key.</item>
///   <item>Calling <see cref="Tenant.Suspend"/> on the aggregate, which raises
///         <see cref="Domain.Events.TenantSuspended"/>.</item>
///   <item>Cascade-suspending the active <c>api_credentials</c> rows (via the
///         IdentityAccess Contracts seam — the <c>api_credentials</c> aggregate
///         no longer lives here) and the <see cref="CryptoKey"/> so the token
///         endpoint and signing service can rely on the child rows' status.</item>
///   <item>Writing three <see cref="IAuditLogger"/> entries — one per domain
///         event raised.</item>
/// </list>
/// </summary>
public sealed record SuspendTenantCommand(
    TenantId TenantId,
    string? Reason = null) : IRequest<Result<SuspendTenantResult>>;
