namespace SBQR.Modules.IdentityAccess.Application.Abstractions;

/// <summary>
/// Application-side port for minting OAuth 2.1 access tokens (JWT bearer).
/// The Infrastructure-layer implementation
/// (<c>SBQR.Modules.IdentityAccess.Infrastructure.Authentication.JwtAccessTokenIssuer</c>)
/// signs HS256 with the symmetric <c>Jwt:SigningKey</c> shared with this
/// module's own bearer-validation pipeline — issuer and validator are the
/// same process for MVP, so no JWKS / OIDC-metadata endpoint is needed
/// (revisit when a second party must validate our tokens).
/// </summary>
public interface IAccessTokenIssuer
{
    /// <summary>
    /// Mint a signed access token for the given claims. The token's expiry
    /// is the issuer's configured TTL (<c>Jwt:AccessTokenTtlMinutes</c>);
    /// the caller does not pass timestamps.
    /// </summary>
    /// <param name="claims">Subject, optional tenant binding, and scope values.</param>
    /// <returns>The encoded JWT plus its lifetime in seconds.</returns>
    IssuedAccessToken Issue(AccessTokenClaims claims);
}

/// <summary>
/// Claim set for an access token. Kept free of JWT-handler types so the
/// Application layer stays infrastructure-agnostic.
/// </summary>
/// <param name="Subject">Value of the <c>sub</c> claim. Actor-tag grammar:
///     <c>platform:{principal}</c> for platform machine principals,
///     <c>client:{client_id}</c> for tenant FI machine principals.</param>
/// <param name="TenantId">Optional tenant binding written as the <c>tenant_id</c>
///     claim. <c>null</c> for platform (non-tenant) principals — only tenant
///     credentials ever carry this claim.</param>
/// <param name="Scopes">Scope values written as <c>scope</c> claims. The
///     platform bootstrap client receives <c>admin</c>; tenant clients receive
///     the QR operation scopes and NEVER <c>admin</c> — this is the
///     privilege-separation keystone of the flow.</param>
/// <param name="PackageId">Optional FR-AUTH-002 mobile-app package identifier.
///     When non-empty it is written as the <c>package_id</c> claim so
///     downstream services (audit, verification) can correlate the token with
///     the originating mobile app build. <c>null</c> for tenant backend
///     tokens and bootstrap tokens (a package id supplied with the bootstrap
///     credential is rejected at mint time, see
///     <c>IssueClientCredentialsTokenCommandHandler.IssueBootstrapTokenAsync</c>).</param>
public sealed record AccessTokenClaims(
    string Subject,
    Guid? TenantId,
    IReadOnlyList<string> Scopes,
    string? PackageId = null);

/// <summary>
/// The result of a successful token issuance.
/// </summary>
/// <param name="AccessToken">The encoded, signed JWT.</param>
/// <param name="ExpiresInSeconds">Lifetime in seconds (mirrors the OAuth2
///     <c>expires_in</c> response field).</param>
public sealed record IssuedAccessToken(string AccessToken, int ExpiresInSeconds);
