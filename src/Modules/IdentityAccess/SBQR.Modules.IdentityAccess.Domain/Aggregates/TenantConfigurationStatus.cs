namespace SBQR.Modules.IdentityAccess.Domain.Aggregates;

/// <summary>
/// Lifecycle status of a <see cref="TenantConfiguration"/>. Mirrors the DB CHECK
/// constraint on <c>public.tenant_configurations.status</c> defined in
/// <c>docs/design/database-design.md</c> §1.3, plus <c>SUSPENDED</c> added by
/// the cascading-suspend feature (Epic-3 / Story 4).
/// </summary>
public enum TenantConfigurationStatus
{
    /// <summary>Issued and usable for token-endpoint authentication.</summary>
    Active = 0,

    /// <summary>Temporarily not trusted — owner tenant is suspended.</summary>
    Suspended = 1,

    /// <summary>Explicitly revoked by an admin (rotation story).</summary>
    Revoked = 2,

    /// <summary>Past <c>expires_at</c>; the token endpoint must reject it.</summary>
    Expired = 3,

    /// <summary>Superseded by a freshly-issued credential that is still within its overlap window.</summary>
    PendingRotation = 4,
}
