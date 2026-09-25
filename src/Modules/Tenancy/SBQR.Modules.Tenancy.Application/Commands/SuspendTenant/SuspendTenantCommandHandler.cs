using MediatR;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;

/// <summary>
/// Handler for <see cref="SuspendTenantCommand"/>. Validated input is guaranteed
/// (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this method only
/// worries about:
/// <list type="number">
///   <item>Loading and suspending the <see cref="Tenant"/> aggregate.</item>
///   <item>Persisting the Tenancy-side change, then firing the two
///         cross-module cascades: the <c>tenant_configurations</c> rows (via the
///         IdentityAccess Contracts seam) and the tenant's signing key (via
///         the KeyCustody Contracts seam — <see cref="SuspendTenantSigningKeysCommand"/>,
///         a tolerant no-op if the tenant has no ACTIVE key). Both cascades
///         commit against their own module's DbContext; this is the
///         established cross-module cascade pattern.</item>
///   <item>Writing the tenant-level <see cref="IAuditLogger"/> entry. The
///         <c>crypto_key.suspended</c> and <c>tenant_configuration.suspended</c>
///         rows are authored by KeyCustody / IdentityAccess respectively —
///         Tenancy does not duplicate them.</item>
/// </list>
/// </summary>
public sealed class SuspendTenantCommandHandler : IRequestHandler<SuspendTenantCommand, Result<SuspendTenantResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;
    private readonly ITenantConfigurationProvisioner _configurations;
    private readonly ISender _mediator;

    public SuspendTenantCommandHandler(
        ITenantRepository tenants,
        IUnitOfWork uow,
        IAuditLogger audit,
        IActorProvider actor,
        ITenantConfigurationProvisioner configurations,
        ISender mediator)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public async Task<Result<SuspendTenantResult>> Handle(
        SuspendTenantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant lookup. NotFound is an expected business outcome; we surface
        //    it via Result.Failure(NotFound, …) so the controller can map to 404.
        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<SuspendTenantResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        // 2. Drive the Tenancy-side state machine. Tenant.Suspend throws
        //    InvalidOperationException on self-transition (already-Suspended)
        //    and on terminal-state; we map that to InvariantViolation so the
        //    controller returns 409.
        var actorId = _actor.CurrentActor();
        try
        {
            tenant.Suspend(actor: actorId, reason: request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result<SuspendTenantResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        // 3. Persist the Tenancy-side change. Then fire both cross-module
        //    cascades — IdentityAccess owns tenant_configurations, KeyCustody owns
        //    crypto_keys; each commits against its own DbContext.
        await _tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var suspendedConfigurations = await _configurations
            .SuspendAllForTenantAsync(tenant.Id.Value, cancellationToken)
            .ConfigureAwait(false);

        await _mediator
            .Send(new SuspendTenantSigningKeysCommand(tenant.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        // 4. Audit trail — one entry per Tenancy-side event raised. The
        //    per-configuration tenant_configuration.suspended rows are authored by
        //    IdentityAccess inside SuspendAllForTenantAsync, and
        //    crypto_key.suspended by KeyCustody's cascade handler; Tenancy
        //    does not duplicate either.
        var suspendedAt = DateTimeOffset.UtcNow;
        await _audit.LogAsync(
            new AuditEntry(
                Action: "tenant.suspended",
                ActorId: actorId,
                ResourceType: "Tenant",
                ResourceId: tenant.Id.Value.ToString(),
                Metadata: BuildTenantMetadata(tenant, request.Reason)),
            cancellationToken).ConfigureAwait(false);

        // Cross-module audit breadcrumb: how many configurations we cascaded. The
        // per-configuration entries are owned by IdentityAccess (same shape as
        // every cross-module audit row that bridges a boundary).
        if (suspendedConfigurations.Count > 0)
        {
            await _audit.LogAsync(
                new AuditEntry(
                    Action: "tenant.credentials.cascade.suspended",
                    ActorId: actorId,
                    ResourceType: "Tenant",
                    ResourceId: tenant.Id.Value.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{tenant.Id.Value:D}\",\"count\":{suspendedConfigurations.Count}}}"),
                cancellationToken).ConfigureAwait(false);
        }

        return Result<SuspendTenantResult>.Ok(new SuspendTenantResult(
            TenantId: tenant.Id,
            Status: tenant.Status,
            Reason: request.Reason,
            SuspendedAt: suspendedAt));
    }

    private static string BuildTenantMetadata(Tenant tenant, string? reason)
    {
        // Hand-built JSON; matches the CreateTenant handler pattern. We escape
        // the reason string so a caller-supplied " or \ doesn't break the audit
        // payload.
        var escapedReason = reason is null
            ? "null"
            : $"\"{EscapeJsonString(reason)}\"";
        return $"{{\"institution_code\":\"{EscapeJsonString(tenant.InstitutionCode)}\",\"reason\":{escapedReason}}}";
    }

    private static string EscapeJsonString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
