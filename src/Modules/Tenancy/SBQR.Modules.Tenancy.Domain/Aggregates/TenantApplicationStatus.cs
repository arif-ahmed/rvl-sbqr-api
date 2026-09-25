namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Lifecycle status of a <see cref="TenantApplication"/>. The two values
/// mirror the DB CHECK constraint on <c>tenant_applications.status</c>.
/// Suspension is per-app: a single suspended app row must not affect sibling
/// apps, other credentials, or the owning tenant (FR-AUTH-002 §5.6 BR4).
/// </summary>
public enum TenantApplicationStatus
{
    /// <summary>Registered and allowed — the package_id passes the allow-list check.</summary>
    Active = 0,

    /// <summary>
    /// Per-app kill switch — the package_id is registered but fails the
    /// allow-list check. Distinct from row-level soft-delete (<see cref="TenantApplication.IsActive"/>
    /// = <c>false</c>) which represents tombstoning for audit only.
    /// </summary>
    Suspended = 1,
}
