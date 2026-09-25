using MediatR;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;

/// <summary>
/// Handler for <see cref="ReactivateTenantCommand"/>. Validated input is
/// guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this method
/// only worries about:
/// <list type="number">
///   <item>Loading and activating the <see cref="Tenant"/> aggregate — the
///         state transition is gated by <see cref="TenantReadiness"/>, the
///         same three-read precondition used by
///         <see cref="ActivateTenant.ActivateTenantCommandHandler"/>.</item>
///   <item>Persisting the Tenancy-side change, then firing the two
///         cross-module cascades: the <c>tenant_configurations</c> rows (via the
///         IdentityAccess Contracts seam) and the tenant's signing key (via the
///         KeyCustody Contracts seam — <see cref="ReinstateTenantSigningKeysCommand"/>,
///         a tolerant no-op if the tenant has no SUSPENDED key).</item>
///   <item>Writing the tenant-level <see cref="IAuditLogger"/> entry. The
///         <c>crypto_key.reinstated</c> and <c>tenant_configuration.reactivated</c>
///         rows are authored by KeyCustody / IdentityAccess respectively.</item>
/// </list>
///
/// <para>
/// <b>Activate-gate symmetry (FR-TENANT-001).</b> Reactivate shares the
/// same gate as Activate. An admin who deletes a credential row out
/// from under a <c>Suspended</c> tenant and then reactives will get a
/// clean <c>409</c> with the readiness summary instead of a
/// half-broken <c>Active</c> tenant. This is the F-1 symmetry fix.
/// </para>
/// </summary>
public sealed class ReactivateTenantCommandHandler : IRequestHandler<ReactivateTenantCommand, Result<ReactivateTenantResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;
    private readonly ITenantConfigurationProvisioner _configurations;
    private readonly ISender _mediator;

    public ReactivateTenantCommandHandler(
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

    public async Task<Result<ReactivateTenantResult>> Handle(
        ReactivateTenantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant lookup. NotFound is an expected business outcome.
        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<ReactivateTenantResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        // 2. Evaluate the activate-gate (same three reads as Activate).
        var readiness = await EvaluateReadinessAsync(
                tenant.Id.Value,
                tenant.InstitutionCode,
                cancellationToken)
            .ConfigureAwait(false);

        // 3. Drive the Tenancy-side state machine. Tenant.Activate throws on
        //    a failed gate, on self-transition (already-Active), and on
        //    terminal-state; map all three to InvariantViolation.
        var actorId = _actor.CurrentActor();
        try
        {
            tenant.Activate(actor: actorId, readiness: readiness);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ReactivateTenantResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        // 4. Persist the Tenancy-side change. Then fire both cross-module
        //    cascades — each commits against its own module's DbContext.
        await _tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var reinstatedConfigurations = await _configurations
            .ReinstateAllForTenantAsync(tenant.Id.Value, cancellationToken)
            .ConfigureAwait(false);

        await _mediator
            .Send(new ReinstateTenantSigningKeysCommand(tenant.Id.Value), cancellationToken)
            .ConfigureAwait(false);

        // 5. Audit trail — one entry per Tenancy-side event raised. The
        //    per-configuration tenant_configuration.reactivated rows are authored by
        //    IdentityAccess, and crypto_key.reinstated by KeyCustody's
        //    cascade handler; Tenancy does not duplicate either.
        var reactivatedAt = DateTimeOffset.UtcNow;
        await _audit.LogAsync(
            new AuditEntry(
                Action: "tenant.reactivated",
                ActorId: actorId,
                ResourceType: "Tenant",
                ResourceId: tenant.Id.Value.ToString(),
                Metadata: $"{{\"institution_code\":\"{tenant.InstitutionCode}\"}}"),
            cancellationToken).ConfigureAwait(false);

        // Cross-module audit breadcrumb: how many configurations we cascaded.
        if (reinstatedConfigurations.Count > 0)
        {
            await _audit.LogAsync(
                new AuditEntry(
                    Action: "tenant.credentials.cascade.reinstated",
                    ActorId: actorId,
                    ResourceType: "Tenant",
                    ResourceId: tenant.Id.Value.ToString(),
                    Metadata: $"{{\"tenant_id\":\"{tenant.Id.Value:D}\",\"count\":{reinstatedConfigurations.Count}}}"),
                cancellationToken).ConfigureAwait(false);
        }

        return Result<ReactivateTenantResult>.Ok(new ReactivateTenantResult(
            TenantId: tenant.Id,
            Status: tenant.Status,
            ReactivatedAt: reactivatedAt));
    }

    /// <summary>
    /// Same three pure reads as <see cref="ActivateTenant.ActivateTenantCommandHandler"/>.
    /// Fail-closed: any seam exception propagates as a 500 (BR-Activation-3).
    /// </summary>
    private async Task<TenantReadiness> EvaluateReadinessAsync(
        Guid tenantId,
        string institutionCode,
        CancellationToken cancellationToken)
    {
        var hasCredential = await _configurations
            .HasActiveAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var hasSigningKey = await _mediator
            .Send(new HasActiveSigningKeyQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        var trustView = await _mediator
            .Send(new GetInstitutionPublicKeyQuery(institutionCode), cancellationToken)
            .ConfigureAwait(false);
        var hasTrustEntry = trustView is not null;

        return new TenantReadiness(
            HasCredential: hasCredential,
            HasSigningKey: hasSigningKey,
            HasTrustEntry: hasTrustEntry);
    }
}
