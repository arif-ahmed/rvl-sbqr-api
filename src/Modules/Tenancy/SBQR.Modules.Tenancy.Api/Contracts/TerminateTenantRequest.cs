using System.Text.Json.Serialization;

namespace SBQR.Modules.Tenancy.Api.Contracts;

/// <summary>
/// Inbound DTO for <c>POST /v1/admin/tenants/{id}/terminate</c>. Carries an
/// optional free-text reason (≤500 chars, enforced by
/// <c>TerminateTenantValidator</c>). The reason is recorded verbatim in
/// <c>audit_logs.metadata</c> for traceability — it is not interpreted by the
/// domain.
///
/// <para>
/// The endpoint is idempotent-rejected: terminating an already-Terminated
/// tenant returns 409
/// (<see cref="SBQR.SharedKernel.Application.ErrorCode.InvariantViolation"/>).
/// Termination is a one-way trip: the row is preserved for history but
/// <c>is_active = false</c>.
/// </para>
/// </summary>
public sealed record TerminateTenantRequest(
    [property: JsonPropertyName("reason")] string? Reason);
