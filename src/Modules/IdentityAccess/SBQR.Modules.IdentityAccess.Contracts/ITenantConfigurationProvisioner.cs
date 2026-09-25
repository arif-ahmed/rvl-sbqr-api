namespace SBQR.Modules.IdentityAccess.Contracts;

/// <summary>
/// Cross-module seam exposing the Identity &amp; Access bounded context's
/// tenant-configuration lifecycle to the Tenancy module. Tenancy calls this from
/// the provision-tenant-configuration command and the suspend / reactivate / terminate
/// cascades instead of touching IdentityAccess's aggregates or DbContext
/// directly. This is the ONLY surface Tenancy may reference from
/// IdentityAccess (tactical-design.md §4/§5).
/// </summary>
public interface ITenantConfigurationProvisioner
{
    /// <summary>
    /// Provision the initial FI client configuration for a tenant that was just
    /// registered (and already persisted) by Tenancy. The <paramref name="tenantId"/>
    /// and <paramref name="institutionCode"/> come from the newly-persisted
    /// <c>tenants</c> row.
    /// </summary>
    /// <param name="tenantId">The owning tenant's id (opaque Guid).</param>
    /// <param name="institutionCode">The BB-assigned institution code — used to shape <c>client_id</c> as <c>{code}-{8hex}</c>.</param>
    /// <param name="isQrGenerationAllowed">Tenant-level initial QR-QrGeneration capability captured at registration. Copied verbatim onto the new <c>tenant_configurations</c> row so the configuration starts in lockstep with the tenant.</param>
    /// <param name="isQrValidationAllowed">Tenant-level initial QR-Verification capability captured at registration. Copied verbatim onto the new <c>tenant_configurations</c> row so the configuration starts in lockstep with the tenant.</param>
    /// <returns>The provisioned configuration's client_id, the server-generated plaintext secret (returned exactly once), and expiry.</returns>
    Task<ProvisionedConfiguration> ProvisionAsync(
        Guid tenantId,
        string institutionCode,
        bool isQrGenerationAllowed,
        bool isQrValidationAllowed,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cascade-suspend every non-soft-deleted configuration of a tenant
    /// (invoked by the Tenancy suspend flow). Returns the suspended
    /// configuration ids/client-ids so the caller can author audit entries that reference them.
    /// </summary>
    Task<IReadOnlyList<ConfigurationSnapshot>> SuspendAllForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cascade-reinstate every suspended configuration of a tenant
    /// (invoked by the Tenancy reactivate flow). Returns the reinstated
    /// configuration ids/client-ids so the caller can author audit entries.
    /// </summary>
    Task<IReadOnlyList<ConfigurationSnapshot>> ReinstateAllForTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pure-read seam for the Tenancy activate-gate: <c>true</c> when the
    /// tenant has at least one <see cref="TenantConfigurationStatus.Active"/>
    /// row. <c>false</c> when the tenant has none (no rows, all suspended,
    /// all revoked, all expired). Backed by the existing repository — no
    /// extra DB hit beyond the partial-UQ lookup. Returns <c>false</c> for
    /// unknown tenants; the caller has already verified existence.
    /// </summary>
    Task<bool> HasActiveAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The configuration minted by <see cref="ITenantConfigurationProvisioner.ProvisionAsync"/>.
/// The plaintext secret is returned exactly once; only its Argon2id hash is
/// persisted.
/// </summary>
/// <param name="CredentialId">The server-generated configuration row id.</param>
/// <param name="ClientId">The opaque <c>{institution_code}-{8hex}</c> identifier.</param>
/// <param name="ClientSecret">The plaintext secret — returned once, never persisted.</param>
/// <param name="ExpiresAt">Expiry of the configuration row (open-ended when null).</param>
public sealed record ProvisionedConfiguration(
    Guid CredentialId,
    string ClientId,
    string ClientSecret,
    DateTimeOffset? ExpiresAt);

/// <summary>
/// Minimal configuration surface returned by the suspend/reactivate cascades so
/// the Tenancy module can author audit entries without holding the full
/// aggregate.
/// </summary>
public sealed record ConfigurationSnapshot(
    Guid CredentialId,
    string ClientId);
