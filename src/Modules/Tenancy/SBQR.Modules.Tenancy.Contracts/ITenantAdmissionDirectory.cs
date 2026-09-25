namespace SBQR.Modules.Tenancy.Contracts;

/// <summary>
/// Read-only seam into the Tenancy bounded context for cross-module callers
/// that need a tenant's admission state without touching Tenancy aggregates,
/// repositories, or the DbContext. Consumed by the IdentityAccess module's
/// OAuth2 client-credentials handler to reject tokens for Suspended /
/// Terminated tenants at mint time (the privilege-separation safety net that
/// keeps a mid-flight tenant suspension effective even while a previously
/// issued token is still live).
///
/// Implementation: <c>TenantAdmissionDirectory</c> in
/// <c>SBQR.Modules.Tenancy.Infrastructure</c>, registered by
/// <c>TenancyModule.RegisterServices</c>.
/// </summary>
public interface ITenantAdmissionDirectory
{
    /// <summary>
    /// Resolve the admission state of the tenant with id
    /// <paramref name="tenantId"/>, or <c>null</c> when no such tenant exists.
    /// </summary>
    /// <param name="tenantId">The tenant's unique id (opaque Guid on this seam).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<TenantAdmissionState?> GetAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The subset of <c>TenantStatus</c> that cross-module callers may depend on.
/// Deliberately smaller than the Tenancy enum: <c>Pending</c> is admission
/// policy ("registration must be exercisable end-to-end"), not a lifecycle
/// detail other modules should model.
/// </summary>
public enum TenantAdmissionState
{
    /// <summary>Registered but not yet activated. MAY authenticate (register-flow requirement).</summary>
    Pending = 0,

    /// <summary>Fully admitted.</summary>
    Active = 1,

    /// <summary>Temporarily not trusted — token minting must reject.</summary>
    Suspended = 2,

    /// <summary>Permanently removed — token minting must reject.</summary>
    Terminated = 3,
}
