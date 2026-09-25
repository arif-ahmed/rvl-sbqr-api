using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants/{id}/applications</c>. Carries
/// the platform and the mobile-app package identifier that the admin wants to
/// register on behalf of the owning tenant. The handler is responsible for:
/// <list type="number">
///   <item>Loading the owning tenant and refusing when absent (404).</item>
///   <item>Building a <see cref="TenantApplication"/> via
///         <see cref="TenantApplication.Register"/>.</item>
///   <item>Persisting the row; the audit-column interceptor stamps
///         <c>created_by/at</c>.</item>
///   <item>Surfacing duplicate applications as
///         <see cref="ErrorCode.InvariantViolation"/> (409) — the
///         <c>(platform, package_id)</c> UNIQUE index is the authoritative
///         guard; the EF layer translates the constraint violation.</item>
/// </list>
/// </summary>
public sealed record RegisterTenantApplicationCommand(
    TenantId TenantId,
    TenantApplicationPlatform Platform,
    string PackageId) : IRequest<Result<RegisterTenantApplicationResult>>;
