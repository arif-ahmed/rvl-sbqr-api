namespace SBQR.SharedKernel.Application;

/// <summary>
/// Resolves the current request's tenant identifier. Implemented by
/// middleware in the host that reads <see cref="JwtClaimNames.TenantId"/>
/// from the bearer token (or, for unauthenticated paths, returns a
/// sentinel).
///
/// Used by every module's <c>DbContext.SaveChangesAsync</c> via the
/// audit-column interceptor, and by <see cref="IAuditLogger"/> to stamp
/// the <c>tenant_id</c> column on every audit row.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>
    /// The tenant identifier for the current request. Returns
    /// <see cref="Guid.Empty"/> for unauthenticated paths that have no
    /// tenant context.
    /// </summary>
    Guid TenantId { get; }
}
