using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// One-time response body for <c>POST /v1/admin/tenants/{id}/tenant-configuration</c>.
/// The <c>clientSecret</c> plaintext is shown here exactly once — it is never
/// persisted (only its Argon2id hash is) and cannot be re-displayed. The FI
/// must store it immediately; losing it means re-provisioning after
/// suspending the old configuration.
/// </summary>
/// <param name="TenantId">Owning tenant id (echoes the route).</param>
/// <param name="CredentialId">The <c>tenant_configurations</c> row id.</param>
/// <param name="ClientId">The <c>{institution_code}-{8hex}</c> identifier the
/// tenant authenticates with at <c>POST /v1/oauth/token</c>.</param>
/// <param name="ClientSecret">Plaintext secret — displayed exactly once.</param>
/// <param name="ExpiresAt">Configuration expiry (ISO-8601).</param>
public sealed record ProvisionTenantConfigurationResponse(
    [property: JsonPropertyName("tenantId")] Guid TenantId,
    [property: JsonPropertyName("credentialId")] Guid CredentialId,
    [property: JsonPropertyName("clientId")] string ClientId,
    [property: JsonPropertyName("clientSecret")] string ClientSecret,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt);
