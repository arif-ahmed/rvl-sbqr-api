using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Inbound DTO for
/// <c>POST /v1/admin/tenants/{tenantId}/applications</c>. Carries the
/// <c>platform</c> (Android or iOS) and the <c>package_id</c> (reverse-DNS
/// applicationId / bundle identifier). Validation is performed by
/// <c>RegisterTenantApplicationValidator</c>, not on the DTO.
/// </summary>
public sealed record RegisterTenantApplicationRequest(
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("package_id")] string PackageId);
