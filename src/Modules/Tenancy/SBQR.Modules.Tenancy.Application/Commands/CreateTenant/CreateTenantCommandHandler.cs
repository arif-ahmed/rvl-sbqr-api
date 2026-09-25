using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.Tenancy.Application.Commands.CreateTenant;

/// <summary>
/// Minimal handler for <see cref="CreateTenantCommand"/>. Validated input is
/// guaranteed (the <c>ValidationBehavior&lt;,&gt;</c> runs first), so this
/// method only inserts one <c>tenants</c> row and returns the new
/// <see cref="TenantId"/>.
///
/// <para>
/// The audit trail rides on the row itself: <c>TenancyAuditColumnInterceptor</c>
/// stamps <c>created_by</c> + <c>created_at</c> on every newly-attached
/// <see cref="SBQR.SharedKernel.Persistence.IAuditableEntity"/> at
/// <c>SavingChanges</c> time, so this handler does not call
/// <see cref="SBQR.SharedKernel.Application.IAuditLogger"/> directly — a
/// separate <c>audit_logs</c> row would duplicate the row's own audit
/// columns.
/// </para>
///
/// <para>
/// Client-configuration issuance (<c>client_id</c> / <c>client_secret</c> via the
/// IdentityAccess seam) and cryptographic signing-key generation are
/// intentionally out of scope here — configurations are minted on demand by
/// <c>ProvisionTenantConfigurationCommand</c> (<c>POST /v1/admin/tenants/{id}/tenant-configuration</c>),
/// and signing-key generation will land as its own dedicated endpoint.
/// </para>
/// </summary>
public sealed class CreateTenantCommandHandler
    : IRequestHandler<CreateTenantCommand, Result<CreateTenantResult>>
{
    private readonly ITenantRepository _tenants;
    private readonly IUnitOfWork _uow;

    public CreateTenantCommandHandler(ITenantRepository tenants, IUnitOfWork uow)
    {
        _tenants = tenants ?? throw new ArgumentNullException(nameof(tenants));
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    }

    public async Task<Result<CreateTenantResult>> Handle(
        CreateTenantCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = Tenant.Register(
            institutionName: request.InstitutionName,
            institutionCode: request.InstitutionCode);

        await _tenants.AddAsync(tenant, cancellationToken).ConfigureAwait(false);
        await _uow.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<CreateTenantResult>.Ok(new CreateTenantResult(tenant.Id));
    }
}
