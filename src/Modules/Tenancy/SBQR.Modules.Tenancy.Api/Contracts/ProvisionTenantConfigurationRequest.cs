using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Optional body for <c>POST /v1/admin/tenants/{id}/tenant-configuration</c>.
/// The endpoint historically required no body — the admin just hit the URL
/// and received a one-time <c>clientSecret</c> back. The body is now
/// optional: when present it carries the per-tenant QR capability choice
/// that is stamped onto the new <c>tenant_configurations</c> row.
///
/// <para>
/// Both fields default to <c>true</c> so an admin that sends no body — or a
/// body that omits either field — gets the historical behaviour (a
/// configuration that can do both). The validator enforces an "at least one
/// capability" invariant so the admin cannot create a credential whose row
/// authorises zero QR flows.
/// </para>
///
/// <param name="IsQrGenerationAllowed">
/// Whether the new <c>tenant_configurations</c> row should allow the QR
/// QrGeneration flow. Stamped verbatim onto
/// <c>public.tenant_configurations.is_qr_generation_allowed</c>; can be
/// flipped later by the per-configuration Enable/Disable verbs.
/// </param>
/// <param name="IsQrValidationAllowed">
/// Whether the new <c>tenant_configurations</c> row should allow the QR
/// Verification flow. Stamped verbatim onto
/// <c>public.tenant_configurations.is_qr_validation_allowed</c>; can be
/// flipped later by the per-configuration Enable/Disable verbs.
/// </param>
public sealed record ProvisionTenantConfigurationRequest(
    [property: JsonPropertyName("isQrGenerationAllowed")] bool IsQrGenerationAllowed = true,
    [property: JsonPropertyName("isQrValidationAllowed")] bool IsQrValidationAllowed = true);
