using System.Text.Json.Serialization;

namespace SBQR.Modules.IdentityAccess.Api.Contracts;

/// <summary>
/// Inbound DTO for <c>POST /v1/oauth/token</c>. Field names follow RFC 6749 §4.3
/// (<c>grant_type</c>, <c>client_id</c>, <c>client_secret</c>). The
/// controller accepts both <c>application/x-www-form-urlencoded</c> (the
/// spec-mandated encoding) and JSON for developer convenience.
///
/// <para>
/// <c>package_id</c> is the FR-AUTH-002 mobile-app allow-list field:
/// optional, and when present it must match an active row in
/// <c>public.tenant_applications</c> for the authenticated tenant. Tenant
/// backends omit it (they have no package); mobile apps that talk directly
/// to the platform send it.
/// </para>
/// </summary>
public sealed class TokenRequest
{
    /// <summary>Must be <c>client_credentials</c>.</summary>
    [JsonPropertyName("grant_type")]
    public string GrantType { get; set; } = string.Empty;

    /// <summary>The client identifier (platform bootstrap client or a tenant FI credential).</summary>
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>The client secret. Never logged, never persisted.</summary>
    [JsonPropertyName("client_secret")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional. The mobile-app package identifier claimed by the caller
    /// (Android <c>applicationId</c> or iOS bundle ID). When supplied, the
    /// handler resolves it against the tenant's allow-list in
    /// <c>public.tenant_applications</c>; a mismatch or unknown value fails
    /// closed with <c>invalid_client</c> / audit reason <c>package_mismatch</c>.
    /// </summary>
    [JsonPropertyName("package_id")]
    public string PackageId { get; set; } = string.Empty;
}
