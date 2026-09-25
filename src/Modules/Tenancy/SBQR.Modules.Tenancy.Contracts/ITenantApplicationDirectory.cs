namespace SBQR.Modules.Tenancy.Contracts;

/// <summary>
/// Read-only seam into the Tenancy bounded context for cross-module callers
/// that need to test whether a mobile-app <c>package_id</c> is registered and
/// active for a tenant. Consumed by the IdentityAccess module's OAuth 2.1
/// client-credentials handler at mint time so a wrong / unregistered /
/// suspended app fails closed with audit reason <c>package_mismatch</c>
/// (FR-AUTH-002 §5.2).
///
/// <para>
/// This is the published-language counterpart to
/// <see cref="ITenantAdmissionDirectory"/>: same shape, different concern.
/// IdentityAccess calls both seams on the hot path; both are registered by
/// <c>TenancyModule.RegisterServices</c>.</para>
///
/// <para>
/// Implementation: <c>TenantApplicationDirectory</c> in
/// <c>SBQR.Modules.Tenancy.Infrastructure</c>.</para>
/// </summary>
public interface ITenantApplicationDirectory
{
    /// <summary>
    /// Returns <c>true</c> iff an active row exists in
    /// <c>public.tenant_applications</c> for the given tenant whose
    /// <c>package_id</c> matches the supplied identifier. Suspended rows,
    /// soft-deleted rows, and rows for other tenants all return
    /// <c>false</c>.
    /// </summary>
    /// <param name="tenantId">The authenticated tenant's id (opaque Guid on this seam).</param>
    /// <param name="packageId">The mobile package identifier claimed by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <paramref name="packageId"/> is compared exactly (case-sensitive) —
    /// Android and iOS both expose identifiers verbatim, so there is no
    /// normalisation to do.
    /// </remarks>
    Task<bool> IsAllowedAsync(
        Guid tenantId,
        string packageId,
        CancellationToken cancellationToken = default);
}
