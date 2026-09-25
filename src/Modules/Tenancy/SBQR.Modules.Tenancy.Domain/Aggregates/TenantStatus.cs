namespace SBQR.Modules.Tenancy.Domain.Aggregates;

/// <summary>
/// Lifecycle status of a <see cref="Tenant"/>. The four values mirror the DB CHECK
/// constraint on <c>tenants.status</c> (see
/// <c>docs/design/database-design.md</c> §1.3). Legal transitions are enforced by
/// the <see cref="Tenant"/> aggregate.
/// </summary>
public enum TenantStatus
{
    /// <summary>
    /// Newly registered; not yet active. Onboarding pending BB registration or
    /// internal KYC. Customers in this state cannot transact.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Active customer. May transact, hold keys, and have valid API credentials.
    /// </summary>
    Active = 1,

    /// <summary>
    /// Temporarily blocked. Existing state is preserved; new operations are denied.
    /// Re-activatable.
    /// </summary>
    Suspended = 2,

    /// <summary>
    /// Offboarded. Terminal state. Row is preserved for audit but
    /// <c>is_active = false</c>.
    /// </summary>
    Terminated = 3,
}
