using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Inbound DTO for <c>GET /v1/admin/tenants</c>. All query-string parameters are
/// optional: missing filters mean "no filter on this field". Default
/// <c>page</c> = 1, default <c>pageSize</c> = 20.
///
/// <para>
/// <c>status</c> is a string at the wire boundary so the JSON shape matches
/// what admin consoles typically send (<c>"Active"</c>, <c>"Pending"</c>,
/// etc.); the controller parses it into the
/// <see cref="SBQR.Modules.Tenancy.Domain.Aggregates.TenantStatus"/> enum and
/// returns 400 on an unrecognised value. Parse is case-insensitive.
/// </para>
/// </summary>
public sealed record ListTenantsRequest(
    [property: JsonPropertyName("status")] string? Status = null,
    [property: JsonPropertyName("isActive")] bool? IsActive = null,
    [property: JsonPropertyName("page")] int Page = 1,
    [property: JsonPropertyName("pageSize")] int PageSize = 20);