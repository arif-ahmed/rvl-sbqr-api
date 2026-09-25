using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Outbound DTO for the <c>/v1/admin/tenants/{tenantId}/applications</c>
/// endpoints. Mirrors
/// <c>SBQR.Modules.Tenancy.Application.Queries.ListTenantApplications.TenantApplicationResponse</c>
/// so the wire format and the Application projection stay in lockstep.
/// </summary>
public sealed record TenantApplicationResponse(
    [property: JsonPropertyName("tenant_application_id")] Guid TenantApplicationId,
    [property: JsonPropertyName("tenant_id")] Guid TenantId,
    [property: JsonPropertyName("platform")] string Platform,
    [property: JsonPropertyName("package_id")] string PackageId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("is_active")] bool IsActive,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("modified_at")] DateTimeOffset? ModifiedAt);
