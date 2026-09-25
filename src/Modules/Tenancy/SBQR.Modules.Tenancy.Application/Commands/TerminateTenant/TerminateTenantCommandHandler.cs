using MediatR;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;

/// <summary>
/// Handler for <see cref="TerminateTenantCommand"/>. Validated input is
/// guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this
/// method only worries about:
/// <list type="number">
///   <item>Loading the <see cref="Tenant"/> aggregate.</item>
///   <item>Calling <see cref="Tenant.Deactivate"/> on the aggregate — which
///         atomically moves to <see cref="TenantStatus.Terminated"/> and sets
///         <see cref="Tenant.IsActive"/> = <c>false</c>, and raises
///         <see cref="Domain.Events.TenantDeactivated"/>.</item>
///   <item>Persisting the Tenancy-side change, then cascading to the
///         <c>tenant_configurations</c> rows (IdentityAccess Contracts seam) and
///         the tenant's signing key (KeyCustody Contracts seam —
///         <see cref="SuspendTenantSigningKeysCommand"/>, tolerant no-op if
///         no ACTIVE key).</item>
///   <item>Writing per-event <see cref="IAuditLogger"/> entries from this
///         module, plus a cross-module breadcrumb if any configurations were
///         cascaded.</item>
/// </list>
///
/// <para>
/// This is the offboarding counterpart to <see cref="SuspendTenant.SuspendTenantCommandHandler"/>:
/// one-way terminal transition instead of a reversible suspension. Same
/// dependency shape so the two handlers stay symmetric.
/// </para>
/// </summary>
public sealed class TerminateTenantCommandHandler
    : IRequestHandler<TerminateTenantCommand, Result<TerminateTenantResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;
    private readonly ITenantConfigurationProvisioner _configurations;
    private readonly ISender _mediator;

    public TerminateTenantCommandHandler(
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

    public async Task<Result<TerminateTenantResult>> Handle(
        TerminateTenantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant lookup. NotFound is an expected business outcome.
        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<TerminateTenantResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        // 2. Drive the Tenancy-side state machine. Tenant.Deactivate throws
        //    on self-transition (already-Terminated) — it's a one-way trip.
        //    Map the exception to InvariantViolation so the controller
        //    returns 409.
        var actorId = _actor.CurrentActor();
        try
        {
            tenant.Deactivate(actor: actorId, reason: request.Reason);
        }
        catch (InvalidOperationException ex)
        {
            return Result<TerminateTenantResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        // 3. Persist the Tenancy-side change. Then fire both cross-module
        //    cascades — each commits against its own module's DbContext.
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
        //    IdentityAccess, and crypto_key.suspended by KeyCustody's
        //    cascade handler; Tenancy does not duplicate either.
        var terminatedAt = DateTimeOffset.UtcNow;
        await _audit.LogAsync(
            new AuditEntry(
                Action: "tenant.terminated",
                ActorId: actorId,
                ResourceType: "Tenant",
                ResourceId: tenant.Id.Value.ToString(),
                Metadata: BuildTenantMetadata(tenant, request.Reason)),
            cancellationToken).ConfigureAwait(false);

        // Cross-module audit breadcrumb: how many configurations we cascaded.
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

        return Result<TerminateTenantResult>.Ok(new TerminateTenantResult(
            TenantId: tenant.Id,
            Status: tenant.Status,
            Reason: request.Reason,
            TerminatedAt: terminatedAt));
    }

    private static string BuildTenantMetadata(Tenant tenant, string? reason)
    {
        // Hand-built JSON; matches the Suspend handler pattern. We escape
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
