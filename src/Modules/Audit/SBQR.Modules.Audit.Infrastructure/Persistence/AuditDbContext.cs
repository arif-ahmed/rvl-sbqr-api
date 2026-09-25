using Microsoft.EntityFrameworkCore;
using SBQR.Modules.Audit.Domain.Persistence;

namespace SBQR.Modules.Audit.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the <c>public.audit_logs</c> table. Schema is owned by
/// <c>db/migrations/007_audit.sql</c>
/// (applied by the external migration tool) — this context NEVER calls
/// <c>Database.Migrate()</c>
/// (PERSISTENCE_DECISIONS.md §3: raw-SQL migrations only).
///
/// The context exists for INSERT + SELECT only; audit rows are append-only.
/// No audit-column interceptor is attached: <see cref="AuditLogger"/> stamps
/// <c>created_by</c> / <c>created_at</c> itself from the
/// <see cref="SBQR.SharedKernel.Application.AuditEntry"/> it receives, and
/// runs the hash-chain write protocol (advisory lock → sequence + head
/// read → canonical-JSON hash → insert) inside one transaction.
/// </summary>
public sealed class AuditDbContext : DbContext
{
    public AuditDbContext(DbContextOptions<AuditDbContext> options)
        : base(options)
    {
    }

    /// <summary>The append-only audit event log.</summary>
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
}
