namespace SBQR.Modules.Tenancy.Application.Contracts;

/// <summary>
/// Outbound projection of a <see cref="Domain.Aggregates.Tenant"/> for API
/// responses (e.g. the body of <c>POST /v1/admin/tenants</c> and
/// <c>GET /v1/admin/tenants/{id}</c>). Plain record — no behavior.
///
/// <para>
/// This is the canonical tenant wire shape for the simplified module:
/// the natural unique key (<see cref="InstitutionCode"/>), the human name,
/// the lifecycle <see cref="Status"/>, and the soft-delete
/// <see cref="IsActive"/> flag. Client-credential issuance and signing-key
/// minting live behind dedicated endpoints that produce their own
/// response DTOs — neither slot is included here.
/// </para>
///
/// <para>
/// <b>Non-null contract.</b> <see cref="InstitutionName"/>,
/// <see cref="InstitutionCode"/>, and <see cref="Status"/> are required by the
/// schema (<c>tenants.institution_name</c> NOT NULL,
/// <c>tenants.institution_code</c> NOT NULL UNIQUE, <c>tenants.status</c> NOT NULL).
/// The string fields are declared non-nullable on the record signature; the
/// nullable analyzer will reject any call site that passes a nullable string.
/// </para>
/// </summary>
/// <param name="TenantId">Server-assigned UUID.</param>
/// <param name="InstitutionName">Human-readable name. Required — non-null,
///     ≤200 chars (DB-enforced).</param>
/// <param name="InstitutionCode">BB institution code (6 digits) — the natural
///     unique key for the aggregate, mirrors <c>institution_registries</c>.
///     Required — non-null, exactly six digits (DB-enforced).</param>
/// <param name="Status">Lifecycle status (PENDING / ACTIVE / SUSPENDED /
///     TERMINATED). Required — non-null, defaults to <c>PENDING</c> on
///     <see cref="Domain.Aggregates.Tenant.Register"/>.</param>
/// <param name="IsActive">Soft-delete flag.</param>
public sealed record TenantResponse(
    Guid TenantId,
    string InstitutionName,
    string InstitutionCode,
    string Status,
    bool IsActive);
