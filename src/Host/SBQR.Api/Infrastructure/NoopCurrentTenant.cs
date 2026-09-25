using SBQR.SharedKernel.Application;

namespace SBQR.Api.Infrastructure;

/// <summary>
/// Stopgap <see cref="ICurrentTenant"/> implementation that always returns
/// <see cref="Guid.Empty"/>. There is no production implementation yet —
/// Epic-1 will add a middleware that reads the JWT claim
/// <c>"tenant_id"</c> and pushes the resolved id onto <c>HttpContext.Items</c>.
///
/// <para>
/// This no-op is registered so the host can boot far enough to expose the
/// OpenAPI documents and the health endpoints, which don't need a tenant
/// context. Real requests will get a non-empty <c>tenant_id</c> once the
/// middleware ships.
/// </para>
/// </summary>
internal sealed class NoopCurrentTenant : ICurrentTenant
{
    /// <inheritdoc/>
    public Guid TenantId => Guid.Empty;
}
