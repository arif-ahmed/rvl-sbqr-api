using System.Collections.Concurrent;
using SBQR.SharedKernel.Application;

namespace SBQR.InstitutionTrust.IntegrationTests.Infrastructure;

public sealed class CapturingAuditLogger : IAuditLogger
{
    private readonly ConcurrentQueue<AuditEntry> _entries = new();

    public IReadOnlyList<AuditEntry> Entries => _entries.ToArray();

    public Task LogAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entries.Enqueue(entry);
        return Task.CompletedTask;
    }

    public void Reset()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}
