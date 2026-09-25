using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Inbound DTO for <c>POST /v1/admin/tenants/{id}/suspend</c>. Carries an optional
/// free-text reason (≤500 chars, enforced by
/// <c>SuspendTenantValidator</c>). The reason is recorded verbatim in
/// <c>audit_logs.metadata</c> for traceability — it is not interpreted by the
/// domain.
///
/// <para>
/// The endpoint is idempotent-rejected: suspending an already-Suspended tenant
/// returns 409 (<see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>).
/// This mirrors the pattern of the activate/deactivate endpoints and keeps the
/// audit trail honest (one event per real transition).
/// </para>
/// </summary>
public sealed record SuspendTenantRequest(
    [property: JsonPropertyName("reason")] string? Reason);
