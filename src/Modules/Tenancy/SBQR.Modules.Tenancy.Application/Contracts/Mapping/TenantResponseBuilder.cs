using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Contracts.Mapping;

/// <summary>
/// Single source of truth for projecting a <see cref="Tenant"/> aggregate onto
/// the public <see cref="TenantResponse"/> shape. Used by every Tenancy handler
/// that needs to return the tenant in an HTTP response:
/// <list type="bullet">
///   <item><c>GetTenantByIdQueryHandler</c> — read-side projection.</item>
///   <item><c>ListTenantsQueryHandler</c> — read-side paged projection.</item>
///   <item><c>TenantsController.SuspendAsync / ReactivateAsync / TerminateAsync / ActivateAsync</c>
///         — post-transition reload via the repository.</item>
/// </list>
///
/// <para>
/// Centralising the projection here keeps the public <see cref="TenantResponse"/>
/// contract from drifting between callers; if the wire shape gains a field, all
/// callers pick it up from one place. <see cref="TenantResponse.ApiCredentials"/>
/// and <see cref="TenantResponse.CryptoKey"/> are deliberately left at their
/// defaults (both <c>null</c>): credential issuance and signing-key minting are
/// dedicated endpoints that fill those slots when they fire, not on the
/// lifecycle endpoints.
/// </para>
/// </summary>
public static class TenantResponseBuilder
{
    /// <summary>Project the aggregate. All callers should use this helper.</summary>
    public static TenantResponse Build(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return new TenantResponse(
            TenantId: tenant.Id.Value,
            InstitutionName: tenant.InstitutionName,
            InstitutionCode: tenant.InstitutionCode,
            Status: tenant.Status.ToString(),
            IsActive: tenant.IsActive);
    }
}