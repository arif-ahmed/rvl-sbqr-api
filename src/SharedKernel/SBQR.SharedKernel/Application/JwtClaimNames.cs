namespace SBQR.SharedKernel.Application;

/// <summary>
/// Wire-level names of the JWT claims that travel through the bearer token.
/// Hoisted to the shared kernel because the issuer
/// (<c>SBQR.Modules.IdentityAccess.Infrastructure.Authentication.JwtAccessTokenIssuer</c>)
/// and the host-side resolver
/// (<c>SBQR.Api.Infrastructure.JwtClaimCurrentTenant</c>) must agree on the
/// exact spelling — <see cref="MapInboundClaims"/> is set to <c>false</c>
/// (<c>IdentityAccessModule.cs</c>) so the wire name is preserved verbatim.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>
    /// The tenant identifier claim. Written by
    /// <c>JwtAccessTokenIssuer.Issue</c> as <c>Guid.ToString("D")</c> for
    /// tenant tokens; deliberately absent on bootstrap/admin tokens so the
    /// host resolver falls through to <see cref="Guid.Empty"/>.
    /// </summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// The mobile-app package identifier claim. Written by
    /// <c>JwtAccessTokenIssuer.Issue</c> only when a tenant caller presented
    /// a <c>package_id</c> form field on the OAuth token request and that
    /// value passed the FR-AUTH-002 allow-list check. Deliberately absent on
    /// tokens minted for tenant backends (which never send a package) and on
    /// bootstrap/admin tokens (which must never carry one — see
    /// <c>IssueClientCredentialsTokenCommandHandler.IssueBootstrapTokenAsync</c>).
    /// Public identifier, not a secret; safe to log.
    /// </summary>
    public const string PackageId = "package_id";
}
