using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SBQR.Modules.Audit.Domain.Persistence;

/// <summary>
/// Persistence entity for the append-only, hash-chained
/// <c>public.audit_logs</c> table (see <c>docs/design/database-design.md</c>
/// and the <c>db/migrations/007_audit.sql</c> migration).
///
/// Deliberately NOT a rich domain aggregate — audit rows are write-once
/// facts projected from the SharedKernel <see cref="SBQR.SharedKernel.Application.AuditEntry"/>
/// contract. Tamper evidence (C8): each row carries
/// <see cref="PreviousHash"/> / <see cref="EntryHash"/> forming a per-tenant
/// chain; any UPDATE, DELETE or TRUNCATE is refused by the database trigger
/// AND by the runtime role's grants. <c>modified_by</c> /
/// <c>modified_at</c> exist only to satisfy the shared audit-column
/// convention and are never written.
///
/// Schema note: this entity lives in the single <c>public</c> schema (the
/// canonical single-schema layout documented in <c>db/migrations/README.md</c>
/// and produced by migration 001). Earlier code referenced an <c>audit</c>
/// schema; that was reconciled to <c>public</c> in step with the rest of the
/// migrations (003_generation, 004_verification, …) which all live in
/// <c>public</c> too.
/// </summary>
[Table("audit_logs", Schema = "public")]
public sealed class AuditLog
{
    /// <summary>Primary key (<c>audit_log_id</c>; the DB primary key is composite with <c>created_at</c> because the table is partitioned).</summary>
    [Key]
    [Column("audit_log_id")]
    public Guid Id { get; set; }

    /// <summary>
    /// Correlation id for the request that produced this entry (A5). The
    /// entry writer generates one when the caller does not supply context;
    /// producers that have one (e.g. verification) embed the same id in
    /// their own row and in the metadata here.
    /// </summary>
    [Column("correlation_id")]
    public Guid CorrelationId { get; set; }

    /// <summary>Short dot-case action name (e.g. <c>auth.token.issued</c>).</summary>
    [Column("event_type")]
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Type of the resource the action targeted (e.g. <c>ApiCredential</c>).</summary>
    [Column("resource_type")]
    [MaxLength(100)]
    public string? ResourceType { get; set; }

    /// <summary>Identifier of the resource the action targeted.</summary>
    [Column("resource_id")]
    [MaxLength(100)]
    public string? ResourceId { get; set; }

    /// <summary>Optional structured JSON metadata. Never payload bytes, signatures, or key material.</summary>
    [Column("metadata")]
    public string? Metadata { get; set; }

    /// <summary>
    /// The tenant the event belongs to. <c>null</c> for platform-level events
    /// (bootstrap token issuance, trust sync, infrastructure ceremonies).
    /// </summary>
    [Column("tenant_id")]
    public Guid? TenantId { get; set; }

    /// <summary>Actor string (<c>platform:…</c>, <c>client:…</c>, <c>system:…</c>).</summary>
    [Column("created_by")]
    [MaxLength(200)]
    public string? CreatedBy { get; set; }

    /// <summary>UTC creation timestamp — app-supplied so the hash covers the stored value.</summary>
    [Column("created_at")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Chain position — reserved via <c>public.audit_logs_seq</c> before hashing.</summary>
    [Column("sequence")]
    public long Sequence { get; set; }

    /// <summary><c>entry_hash</c> of the previous entry in the same tenant scope; 64 zeros for the first entry.</summary>
    [Column("previous_hash")]
    [Required]
    [MaxLength(64)]
    public string PreviousHash { get; set; } = string.Empty;

    /// <summary>SHA-256 over the canonical JSON of this row (incl. <see cref="PreviousHash"/>).</summary>
    [Column("entry_hash")]
    [Required]
    [MaxLength(64)]
    public string EntryHash { get; set; } = string.Empty;

    /// <summary>Always <c>null</c> — audit rows are immutable.</summary>
    [Column("modified_by")]
    public string? ModifiedBy { get; set; }

    /// <summary>Always <c>null</c> — audit rows are immutable.</summary>
    [Column("modified_at")]
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Always <c>true</c> — audit never soft-deletes.</summary>
    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}
