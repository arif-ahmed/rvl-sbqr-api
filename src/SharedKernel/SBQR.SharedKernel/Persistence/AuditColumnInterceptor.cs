namespace SBQR.SharedKernel.Persistence;

/// <summary>
/// Marker for the audit-column interceptor that auto-populates
/// <c>created_at</c> / <c>updated_at</c> / <c>tenant_id</c> on every
/// persisted row. The concrete implementation lives in each module's
/// <c>Infrastructure/Persistence/</c> folder (or in the Audit module
/// if a single cross-cutting implementation is preferred).
///
/// This interface only exposes what the shared kernel needs; the
/// EF Core-specific SaveChanges interception is module-private.
/// </summary>
public interface IAuditColumnInterceptor
{
    /// <summary>
    /// Called by the module's DbContext before <c>SaveChangesAsync</c>.
    /// </summary>
    void StampAuditColumns();
}
