using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using SBQR.Modules.Audit.Domain.Persistence;
using SBQR.Modules.Audit.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Web;

namespace SBQR.Modules.Audit.Infrastructure;

/// <summary>
/// Concrete <see cref="IAuditLogger"/> — appends one hash-chained row per
/// <see cref="AuditEntry"/> to the <c>public.audit_logs</c> table via
/// <see cref="AuditDbContext"/> (C8 tamper evidence).
///
/// Chain write protocol (must stay in sync with
/// <c>db/migrations/007_audit.sql</c>):
/// <list type="number">
///   <item>Take a per-scope transaction-scoped advisory lock
///         (<c>pg_advisory_xact_lock</c>) — scope is the tenant id, or
///         <c>SYSTEM</c> for platform events — so two concurrent writers can
///         never fork the chain off the same previous entry. Locks are
///         per-scope: the verify hot path is not globally serialized.</item>
///   <item>Reserve the sequence value (<c>public.audit_logs_seq</c>) and read
///         the chain head's <c>entry_hash</c> (64 zeros when the scope is
///         empty).</item>
///   <item>Compute <c>entry_hash</c> = lowercase hex SHA-256 over the
///         canonical JSON of the row — snake_case keys sorted ascending, no
///         whitespace, metadata embedded as its (masked) string value,
///         timestamps as ISO-8601 UTC <c>yyyy-MM-ddTHH:mm:ss.fffZ</c>.</item>
///   <item>Insert with explicit <c>created_at</c> / <c>sequence</c> so the
///         stored row is byte-for-byte what was hashed, then commit.</item>
/// </list>
///
/// Tenant resolution order: the entry's explicit <see cref="AuditEntry.TenantId"/>
/// (e.g. the token endpoint knows the FI's tenant at issuance time), then
/// <see cref="ICurrentTenant"/> when non-empty, else <c>null</c> (platform-level
/// event — <c>audit_logs.tenant_id</c> is nullable by design).
///
/// Failure semantics: an audit write failure is LOGGED at <c>Error</c> level
/// (including the full entry so it can be replayed) but does NOT fail the
/// caller's request. Rationale: audit entries are written after the primary
/// transaction commits (e.g. a tenant is already persisted when its
/// <c>tenant.registered</c> entry is logged); throwing here would turn a
/// successful operation into a client-visible 500 with the work already
/// done. The epic-9 audit stories revisit this with an outbox.
/// </summary>
public sealed partial class AuditLogger : IAuditLogger
{
    private const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly AuditDbContext _db;
    private readonly ICurrentTenant _currentTenant;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogger> _logger;

    public AuditLogger(
        AuditDbContext db,
        ICurrentTenant currentTenant,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogger> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _currentTenant = currentTenant ?? throw new ArgumentNullException(nameof(currentTenant));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var tenantId = entry.TenantId
            ?? (_currentTenant.TenantId == Guid.Empty ? null : _currentTenant.TenantId);

        // Reuse the request-scoped correlation id stashed by
        // SBQR.Api.Infrastructure.CorrelationIdMiddleware so every audit row
        // for the same HTTP request shares one value. Falls back to a fresh
        // GUID when called outside an HTTP request (background workers,
        // boot-time tasks) — those rows legitimately have no inbound request.
        var correlationId = ResolveOrMintCorrelationId();
        var createdBy = string.IsNullOrWhiteSpace(entry.ActorId) ? "system" : entry.ActorId;

        // The exact ISO-8601 string that is hashed AND persisted — never
        // let ToString round-tripping drift between the two.
        var createdAtIso = DateTimeOffset.UtcNow.ToString(
            "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var createdAt = DateTimeOffset.Parse(
            createdAtIso, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        try
        {
            await using var transaction = await _db.Database
                .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            // 1. Serialize writers within this chain scope only.
            var scope = tenantId?.ToString("D") ?? "SYSTEM";
            await _db.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({scope}, 0))",
                cancellationToken).ConfigureAwait(false);

            // 2. Reserve the sequence value and read the chain head.
            var sequence = await _db.Database
                .SqlQuery<long>($"SELECT nextval('public.audit_logs_seq') AS \"Value\"")
                .SingleAsync(cancellationToken).ConfigureAwait(false);

            var previousHash = await ReadChainHeadAsync(tenantId, cancellationToken).ConfigureAwait(false)
                ?? GenesisHash;

            // 3. Canonical JSON — snake_case keys, sorted ascending, no
            //    whitespace; nullable values serialize as null.
            var canonical = JsonSerializer.Serialize(new SortedDictionary<string, object?>
            {
                ["correlation_id"] = correlationId,
                ["created_at"] = createdAtIso,
                ["created_by"] = createdBy,
                ["event_type"] = entry.Action,
                ["metadata"] = entry.Metadata,
                ["previous_hash"] = previousHash,
                ["resource_id"] = entry.ResourceId,
                ["resource_type"] = entry.ResourceType,
                ["sequence"] = sequence,
                ["tenant_id"] = tenantId,
            });
            var entryHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();

            // 4. Insert exactly what was hashed.
            _db.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CorrelationId = correlationId,
                EventType = entry.Action,
                ResourceType = entry.ResourceType,
                ResourceId = entry.ResourceId,
                Metadata = entry.Metadata,
                TenantId = tenantId,
                CreatedBy = createdBy,
                CreatedAt = createdAt,
                Sequence = sequence,
                PreviousHash = previousHash,
                EntryHash = entryHash,
                IsActive = true,
            });
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // cancellation is the caller's decision, not an audit failure
        }
        catch (Exception ex)
        {
            LogAuditWriteFailed(
                ex,
                entry.Action,
                createdBy,
                entry.ResourceType,
                entry.ResourceId,
                tenantId,
                entry.Metadata);
        }
    }

    [LoggerMessage(
        EventId = 6001,
        Level = LogLevel.Error,
        Message = "Audit write FAILED (action={Action} actor={Actor} resource={ResourceType}/{ResourceId} tenant={TenantId} metadata={Metadata}). The primary operation was NOT rolled back; replay this entry from the log.")]
    private partial void LogAuditWriteFailed(
        Exception ex,
        string action,
        string actor,
        string? resourceType,
        string? resourceId,
        Guid? tenantId,
        string? metadata);

    /// <summary>
    /// Reads the chain-head <c>entry_hash</c> for the supplied tenant scope
    /// (or the platform scope when <paramref name="tenantId"/> is <c>null</c>).
    /// Returns <c>null</c> when the scope has no prior row.
    /// </summary>
    /// <remarks>
    /// Implemented against <see cref="Microsoft.EntityFrameworkCore.Storage.RelationalConnection"/>
    /// with an explicit <see cref="NpgsqlParameter"/> rather than
    /// <c>Database.SqlQuery&lt;T&gt;(FormattableString)</c>. EF Core's
    /// interpolated-SQL overload, when combined with Npgsql auto-prepare, has
    /// been observed to cache the statement under one parameter shape (e.g.
    /// <c>tenant_id = $1</c>) and re-execute it under another shape after the
    /// same SQL text is seen with a different nullable parameter, producing
    /// <c>Bind message supplies 0 parameters, but prepared statement
    /// "sqlx_s_…" requires 1</c>. Binding the parameter explicitly here gives
    /// Npgsql a stable signature so the cache stays coherent across the
    /// tenant-scoped and platform-scoped (<c>tenant_id IS NULL</c>) call
    /// sites.
    /// </remarks>
    private async Task<string?> ReadChainHeadAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        // IS NOT DISTINCT FROM treats NULL = NULL as TRUE so a single
        // parameter handles both the tenant-scoped and platform-scoped
        // branches. The parameter is always supplied (DBNull.Value when
        // tenantId is null) so the prepared-statement cache signature is
        // stable across both cases.
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT entry_hash
            FROM public.audit_logs
            WHERE tenant_id IS NOT DISTINCT FROM @tenantId
            ORDER BY sequence DESC
            LIMIT 1
            """;

        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenantId";
        parameter.Value = (object?)tenantId ?? DBNull.Value;
        command.Parameters.Add(parameter);

        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    /// <summary>
    /// Returns the request-scoped correlation id when the call originated
    /// inside an HTTP request handled by
    /// <c>CorrelationIdMiddleware</c>; otherwise mints a fresh GUID for
    /// background / boot-time work.
    /// </summary>
    private Guid ResolveOrMintCorrelationId()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is not null
            && context.Items.TryGetValue(SBQR.SharedKernel.Web.CorrelationContract.ItemsKey, out var raw)
            && raw is Guid fromRequest
            && fromRequest != Guid.Empty)
        {
            return fromRequest;
        }

        return Guid.NewGuid();
    }
}
