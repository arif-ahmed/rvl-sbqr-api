using SBQR.SharedKernel.Application;

namespace SBQR.Api.Infrastructure;

/// <summary>
/// Resolves <see cref="ICurrentTenant.TenantId"/> from the bearer token's
/// <see cref="JwtClaimNames.TenantId"/> claim. Bootstrap tokens carry no
/// <c>tenant_id</c> by design (see
/// <c>IssueClientCredentialsTokenCommandHandler.IssueBootstrapTokenAsync</c>),
/// so a missing or unauthenticated principal deliberately returns
/// <see cref="Guid.Empty"/> — the QR-generation handler and the signing
/// provider both treat that as "no tenant context, fail closed".
/// Mirrors the read-claim-and-fall-back-to-sentinel pattern used by
/// <c>HttpContextActorProvider</c> for the audit <c>sub</c> claim.
/// </summary>
public sealed class JwtClaimCurrentTenant : ICurrentTenant
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Scoped lifetime + one resolution per request. The handler reads the
    // property multiple times via the audit interceptor, the signing
    // provider, and the QR-generation command handler itself; caching
    // avoids re-walking ClaimsPrincipal on every read.
    private Guid? _resolved;

    public JwtClaimCurrentTenant(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor
            ?? throw new ArgumentNullException(nameof(httpContextAccessor));
    }

    public Guid TenantId => _resolved ??= Resolve();

    private Guid Resolve()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true)
        {
            return Guid.Empty;
        }

        var raw = context.User.FindFirst(JwtClaimNames.TenantId)?.Value;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Guid.Empty;
        }

        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }
}