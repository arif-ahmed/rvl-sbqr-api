using SBQR.Modules.Tenancy.Domain.Aggregates;

namespace SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;

/// <summary>
/// Result of <see cref="ProvisionTenantConfigurationCommand"/>. Carries the
/// freshly minted FI client configuration the controller projects into the
/// one-time response body.
/// </summary>
/// <param name="TenantId">The owning tenant.</param>
/// <param name="CredentialId">The <c>tenant_configurations</c> row id
/// (IdentityAccess-side identifier, useful for ops lookups).</param>
/// <param name="ClientId">The opaque <c>{institution_code}-{8hex}</c>
/// identifier the tenant will authenticate with.</param>
/// <param name="ClientSecret">The plaintext secret — returned exactly once
/// here, never persisted, never logged. Only its Argon2id hash is stored.</param>
/// <param name="ExpiresAt">Configuration expiry (the provisioner currently
/// stamps one year).</param>
public sealed record ProvisionTenantConfigurationResult(
    TenantId TenantId,
    Guid CredentialId,
    string ClientId,
    string ClientSecret,
    DateTimeOffset? ExpiresAt);
