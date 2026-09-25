using SBQR.SharedKernel.Application;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// Stub <see cref="ICurrentTenant"/> used by the integration-test composition
/// root. Returns <see cref="Guid.Empty"/> for every request — the Tenancy
/// module's audit interceptor uses this only as a tenant-id column value on
/// the audit log, and the integration tests do not assert on tenant-id
/// filtering. The production <c>NoopCurrentTenant</c> in <c>SBQR.Api</c> is
/// <c>internal</c>, so we redeclare the same contract here.
/// </summary>
internal sealed class NoopCurrentTenant : ICurrentTenant
{
    /// <inheritdoc/>
    public Guid TenantId => Guid.Empty;
}
