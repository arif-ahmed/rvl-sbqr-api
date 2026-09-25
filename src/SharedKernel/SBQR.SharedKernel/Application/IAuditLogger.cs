namespace SBQR.SharedKernel.Application;

/// <summary>
/// Cross-cutting audit log abstraction. Implemented by the Audit module's
/// infrastructure. Any module's command handler may call
/// <see cref="LogAsync"/> directly to emit a SEC-06/07-mandated event;
/// no full MediatR <c>INotification</c> event bus is required.
///
/// See tactical-design.md §6 (deliberately simple for MVP).
/// </summary>
public interface IAuditLogger
{
    /// <summary>
    /// Append an audit log entry. The implementation is responsible for
    /// stamping <c>created_at</c> and resolving <c>tenant_id</c> via
    /// <see cref="ICurrentTenant"/>.
    /// </summary>
    /// <param name="entry">The entry to log. Caller-supplied fields must be sanitized; the implementation
    /// never logs payload bytes, signatures, or private-key material.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}

/// <summary>
/// A single audit log entry. Fields are intentionally string-typed at the
/// boundary so that no domain entity escapes the module.
/// </summary>
/// <param name="Action">Short snake_case action name (e.g. <c>"key.activated"</c>).</param>
/// <param name="ActorId">Identifier of the principal that triggered the action (user id, <c>"client:{client_id}"</c>, <c>"platform:{principal}"</c>, or <c>"system"</c>).</param>
/// <param name="ResourceType">Type of the resource the action was performed on (e.g. <c>"SigningKey"</c>).</param>
/// <param name="ResourceId">Identifier of the resource.</param>
/// <param name="Metadata">Optional structured metadata (serialized to JSON in the <c>audit_logs.metadata</c> column).</param>
/// <param name="TenantId">Optional tenant the event belongs to. When <c>null</c>, the implementation falls back to <see cref="ICurrentTenant"/>; platform-level events end up with a <c>null</c> <c>tenant_id</c>.</param>
public sealed record AuditEntry(
    string Action,
    string ActorId,
    string ResourceType,
    string ResourceId,
    string? Metadata = null,
    Guid? TenantId = null);
