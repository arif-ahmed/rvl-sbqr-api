using MediatR;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.Tenancy.Application.Commands.CreateTenant;

/// <summary>
/// MediatR command for <c>POST /v1/admin/tenants</c>. Carries the seed values for a
/// new tenant; the handler is responsible for building the
/// <see cref="Tenant"/> aggregate via <see cref="Tenant.Register"/> and
/// persisting one <c>tenants</c> row. Client-credential issuance and signing-key
/// generation are intentionally out of scope here — both will land as
/// separate, dedicated endpoints.
///
/// Returns the freshly assigned <see cref="TenantId"/>, wrapped in
/// <see cref="CreateTenantResult"/> and a <see cref="Result{T}"/>.
/// </summary>
public sealed record CreateTenantCommand(
    string InstitutionName,
    string InstitutionCode) : IRequest<Result<CreateTenantResult>>;