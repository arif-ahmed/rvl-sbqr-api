using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Inbound DTO for <c>POST /v1/admin/tenants</c>. Shape is the JSON contract
/// documented in the OpenAPI <c>v1.internal-admin</c> document; this record
/// is consumed by <see cref="Controllers.TenantsController.RegisterAsync"/>
/// and projected onto a <c>CreateTenantCommand</c> for the Application layer.
///
/// All validation lives in
/// <c>SBQR.Modules.Tenancy.Application.Commands.CreateTenant.CreateTenantValidator</c>
/// (FluentValidation, executed by the global <c>ValidationBehavior</c>), so
/// this record contains no validation attributes.
///
/// <para>Signing-key generation/adoption is intentionally not part of tenant
/// creation. It will be exposed later through a dedicated crypto-key endpoint.</para>
/// </summary>
/// <param name="InstitutionName">
/// Human-readable name of the institution (max 200 chars per the DB column).
/// </param>
/// <param name="InstitutionCode">
/// Bangladesh Bank institution code. Must be exactly six digits
/// (<c>^[0-9]{6}$</c>). The natural unique key for the tenant — required.
/// </param>
public sealed record RegisterTenantRequest(
    [property: JsonPropertyName("institutionName")] string InstitutionName,
    [property: JsonPropertyName("institutionCode")] string InstitutionCode);
