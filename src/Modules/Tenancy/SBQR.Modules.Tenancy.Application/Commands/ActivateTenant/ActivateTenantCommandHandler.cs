using MediatR;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;

/// <summary>
/// Handler for <see cref="ActivateTenantCommand"/>. Validated input is
/// guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this
/// method only worries about:
/// <list type="number">
///   <item>Loading the <see cref="Tenant"/> aggregate.</item>
///   <item>Evaluating the activate-gate — three reads from the existing
///         cross-BC Contracts seams (IdentityAccess, KeyCustody,
///         InstitutionTrust). If any precondition fails, the gate fires
///         and the aggregate is left untouched.</item>
///   <item>Calling <see cref="Tenant.Activate(string, TenantReadiness)"/>
///         on it — raises the matching <c>TenantActivated</c> domain event.
///         The aggregate refuses the transition if readiness is not
///         satisfied; the handler maps that <see cref="InvalidOperationException"/>
///         to <c>409 InvariantViolation</c> with the readiness summary.</item>
///   <item>Persisting the change in one unit-of-work so the token endpoint
///         and signing service see a consistent snapshot.</item>
///   <item>Writing the <c>tenant.activated</c> audit row.</item>
/// </list>
///
/// <para>
/// Unlike <see cref="ReactivateTenant.ReactivateTenantCommandHandler"/>, this
/// handler does NOT cascade onto <c>api_credentials</c> or <c>crypto_keys</c>.
/// Activate is a state-machine entry (Pending → Active, Suspended → Active),
/// not a suspend-reversal — credential and key state is independent of the
/// tenant's <c>Active</c> flag and is governed by its own dedicated endpoints.
/// </para>
///
/// <para>
/// <b>Activate-gate contract (FR-TENANT-001).</b> The platform asserts
/// that an institute is <see cref="TenantStatus.Active"/> only when all
/// three downstream resources exist: an ACTIVE
/// <c>tenant_configurations</c> row, an ACTIVE <c>crypto_keys</c> row,
/// and an ACTIVE <c>public.institution_keys</c> row. This
/// closes the F-3 state-machine gap: a tenant can no longer reach
/// <c>Active</c> with no signing key and no trust-directory entry. See
/// <c>docs/functional-requirements/002-tenant-registration/FR-TENANT-001-tenant-registration.md</c>
/// for the full requirement and the recovery path when the crypto-keys
/// auto-publish fails.
/// </para>
/// </summary>
public sealed class ActivateTenantCommandHandler
    : IRequestHandler<ActivateTenantCommand, Result<ActivateTenantResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;
    private readonly IAuditLogger _audit;
    private readonly IActorProvider _actor;
    private readonly ITenantConfigurationProvisioner _configurations;
    private readonly ISender _mediator;

    public ActivateTenantCommandHandler(
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

    public async Task<Result<ActivateTenantResult>> Handle(
        ActivateTenantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant lookup. NotFound is an expected business outcome.
        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<ActivateTenantResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        // 2. Evaluate the activate-gate. Three pure reads across the three
        //    BCs that own the downstream resources. Fail-closed: any seam
        //    exception propagates as a 500 (BR-Activation-3).
        var readiness = await EvaluateReadinessAsync(
                tenant.Id.Value,
                tenant.InstitutionCode,
                cancellationToken)
            .ConfigureAwait(false);

        // 3. Drive the Tenancy-side state machine. Tenant.Activate throws on
        //    a failed gate, on self-transition (already-Active), and on
        //    terminal-state; map all three to InvariantViolation so the
        //    controller returns 409 with the readiness summary in the body.
        var actorId = _actor.CurrentActor();
        try
        {
            tenant.Activate(actor: actorId, readiness: readiness);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ActivateTenantResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        // 4. Persist the change in one transaction.
        await _tenants.UpdateAsync(tenant, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 5. Audit trail — exactly one row, the Tenancy-side activation event.
        //    No credential cascade row (Activate is not a suspend-reversal) and
        //    no crypto-key row (key state is independent of tenant Active).
        var activatedAt = DateTimeOffset.UtcNow;
        await _audit.LogAsync(
            new AuditEntry(
                Action: "tenant.activated",
                ActorId: actorId,
                ResourceType: "Tenant",
                ResourceId: tenant.Id.Value.ToString(),
                Metadata: $"{{\"institution_code\":\"{tenant.InstitutionCode}\"}}"),
            cancellationToken).ConfigureAwait(false);

        return Result<ActivateTenantResult>.Ok(new ActivateTenantResult(
            TenantId: tenant.Id,
            Status: tenant.Status,
            ActivatedAt: activatedAt));
    }

    /// <summary>
    /// Reads the three preconditions from the existing Contracts seams.
    /// Pure — no side-effects, no audit rows, no state mutations. Returns a
    /// <see cref="TenantReadiness"/> the aggregate uses to gate the state
    /// transition.
    /// </summary>
    private async Task<TenantReadiness> EvaluateReadinessAsync(
        Guid tenantId,
        string institutionCode,
        CancellationToken cancellationToken)
    {
        var hasCredential = await _configurations
            .HasActiveAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // KeyCustody seam: HasActiveSigningKeyQuery is a pure boolean read
        // from the KeyCustody.Contracts MediatR seam. Returns false when the
        // tenant has no ACTIVE row in crypto_keys (no rows, all suspended,
        // all revoked). Any exception propagates as a 500 (BR-Activation-3).
        var hasSigningKey = await _mediator
            .Send(new HasActiveSigningKeyQuery(tenantId), cancellationToken)
            .ConfigureAwait(false);

        // InstitutionTrust seam: the existing GetInstitutionPublicKeyQuery
        // returns null when no ACTIVE row exists for the institution_code.
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