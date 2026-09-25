using MediatR;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;

/// <summary>
/// Handler for <see cref="ProvisionTenantConfigurationCommand"/>. This is the
/// dedicated configuration endpoint the register flow defers to — it mints the
/// tenant's initial FI client configuration (<c>client_id</c> +
/// <c>client_secret</c>) through the IdentityAccess Contracts seam, which
/// owns the <c>tenant_configurations</c> aggregate, the Argon2id hash, and
/// the per-configuration <c>auth.client_credentials.issued</c> audit row.
///
/// <para>Order of checks:</para>
/// <list type="number">
///   <item>Tenant lookup — <see cref="ErrorCode.NotFound"/> (→ 404) when the
///         row is missing.</item>
///   <item>Admission guard — <see cref="ErrorCode.InvariantViolation"/>
///         (→ 409) for Suspended / Terminated tenants: the token endpoint
///         re-checks admission at mint time and would reject them anyway, so
///         provisioning a configuration they cannot use is refused up front.
///         <see cref="TenantStatus.Pending"/> and
///         <see cref="TenantStatus.Active"/> tenants may provision — the
///         whole point is that registration is exercisable end-to-end before
///         BB activation.</item>
///   <item>Single-active-configuration guard — the provisioner throws
///         <see cref="InvalidOperationException"/> when the tenant already
///         has an active configuration (rotation is a separate flow); mapped to
///         <see cref="ErrorCode.InvariantViolation"/> (→ 409).</item>
/// </list>
///
/// <para>The plaintext secret crosses the boundary exactly once: result →
/// controller → response body. It is never logged — the Tenancy-side audit
/// breadcrumb records only the <c>client_id</c>.</para>
/// </summary>
public sealed class ProvisionTenantConfigurationCommandHandler
    : IRequestHandler<ProvisionTenantConfigurationCommand, Result<ProvisionTenantConfigurationResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly ITenantConfigurationProvisioner _configurations;
    private readonly IActorProvider _actor;
    private readonly IAuditLogger _audit;

    public ProvisionTenantConfigurationCommandHandler(
        ITenantRepository tenants,
        ITenantConfigurationProvisioner configurations,
        IActorProvider actor,
        IAuditLogger audit)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _configurations = configurations ?? throw new ArgumentNullException(nameof(configurations));
        _actor = actor ?? throw new ArgumentNullException(nameof(actor));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    public async Task<Result<ProvisionTenantConfigurationResult>> Handle(
        ProvisionTenantConfigurationCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Tenant lookup. The institution code that shapes the client_id is
        //    read from the row — callers cannot supply their own.
        var tenant = await _tenants
            .GetByIdAsync(request.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (tenant is null)
        {
            return Result<ProvisionTenantConfigurationResult>.Failure(
                ErrorCode.NotFound,
                $"tenant {request.TenantId.Value:D} does not exist.");
        }

        // 2. Admission guard.
        if (tenant.Status is TenantStatus.Suspended or TenantStatus.Terminated)
        {
            return Result<ProvisionTenantConfigurationResult>.Failure(
                ErrorCode.InvariantViolation,
                $"tenant {tenant.Id.Value:D} is {tenant.Status}; configurations may only be provisioned for " +
                "Pending or Active tenants.");
        }

        // 3. Mint through the Contracts seam. IdentityAccess commits the
        //    tenant_configurations row against its own DbContext and authors
        //    the per-configuration audit entry. The admin's QR capability
        //    choice from the request body is stamped verbatim onto the new
        //    configuration row so the credential's privilege set matches the
        //    admin's intent at provision time. Post-issuance flips live on
        //    TenantConfiguration via the dedicated Enable/Disable verbs.
        ProvisionedConfiguration configuration;
        try
        {
            configuration = await _configurations
                .ProvisionAsync(
                    tenant.Id.Value,
                    tenant.InstitutionCode,
                    request.IsQrGenerationAllowed,
                    request.IsQrValidationAllowed,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ProvisionTenantConfigurationResult>.Failure(
                ErrorCode.InvariantViolation,
                ex.Message);
        }

        // 4. Tenancy-side audit breadcrumb. Records the acting admin — the
        //    IdentityAccess row attributes the issuance to "system" because
        //    the seam is process-internal. The secret stays out of the
        //    metadata; only the client_id is recorded.
        await _audit.LogAsync(
            new AuditEntry(
                Action: "tenant.configuration.provisioned",
                ActorId: _actor.CurrentActor(),
                ResourceType: "Tenant",
                ResourceId: tenant.Id.Value.ToString(),
                Metadata: $"{{\"institution_code\":\"{tenant.InstitutionCode}\",\"client_id\":\"{configuration.ClientId}\"}}",
                TenantId: tenant.Id.Value),
            cancellationToken).ConfigureAwait(false);

        return Result<ProvisionTenantConfigurationResult>.Ok(new ProvisionTenantConfigurationResult(
            TenantId: tenant.Id,
            CredentialId: configuration.CredentialId,
            ClientId: configuration.ClientId,
            ClientSecret: configuration.ClientSecret,
            ExpiresAt: configuration.ExpiresAt));
    }
}
