using System.Collections.Concurrent;
using SBQR.SharedKernel.Application;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// In-memory <see cref="IAuditLogger"/> that records every entry for
/// assertions. Used by the integration tests in lieu of a real Audit-module
/// implementation so the handler's three <see cref="AuditEntry"/> writes can
/// be inspected after <c>mediator.Send(...)</c> returns.
///
/// <para>
/// Thread-safe (the command pipeline is single-threaded per scope, but
/// Respawn-style reset or parallel assertion code may touch this concurrently).
/// </para>
/// </summary>
public sealed class CapturingAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    /// <summary>Snapshot of every entry logged through this instance.</summary>
    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();

    /// <inheritdoc/>
    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    /// <summary>Clear captured entries. Called between tests.</summary>
    public void Reset()
    {
        while (_entries.TryDequeue(out _))
        {
            // drain
        }
    }
}
