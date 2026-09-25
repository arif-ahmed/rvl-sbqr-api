using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SBQR.Modules.IdentityAccess.Application.Abstractions;
using SBQR.SharedKernel.Application;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Authentication;

/// <summary>
/// Default <see cref="IAccessTokenIssuer"/> — mints HS256-signed JWTs via
/// <see cref="JsonWebTokenHandler"/>. The symmetric <c>Jwt:SigningKey</c> is
/// shared with this module's own bearer-validation pipeline: for MVP the
/// issuer and the validator are the same process, so no JWKS or OIDC-metadata
/// endpoint exists. Revisit (asymmetric keys + JWKS) when a second party must
/// validate our tokens.
///
/// <para>Scopes are written as multiple <c>scope</c> claims (JSON array) so
/// the validation side's <c>RequireClaim("scope", "admin")</c> matches a
/// single granted value even when several scopes are present.</para>
/// </summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    private readonly string _signingKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _ttlMinutes;

    public JwtAccessTokenIssuer(string signingKey, string issuer, string audience, int ttlMinutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);

        if (signingKey.Length < 32)
        {
            throw new ArgumentException(
                "Jwt:SigningKey must be at least 32 characters (HS256 security floor). " +
                "Generate a strong key and set it via env var / user-secrets / Key Vault.",
                nameof(signingKey));
        }

        if (ttlMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ttlMinutes), "TTL must be positive.");
        }

        _signingKey = signingKey;
        _issuer = issuer;
        _audience = audience;
        _ttlMinutes = ttlMinutes;
    }

    /// <inheritdoc/>
    public IssuedAccessToken Issue(AccessTokenClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentException.ThrowIfNullOrWhiteSpace(claims.Subject);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            NotBefore = DateTime.UtcNow,
            Expires = DateTime.UtcNow.AddMinutes(_ttlMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = claims.Subject,
                ["scope"] = claims.Scopes,
            },
        };

        if (claims.TenantId is { } tenantId)
        {
            descriptor.Claims![JwtClaimNames.TenantId] = tenantId.ToString("D");
        }

        if (!string.IsNullOrWhiteSpace(claims.PackageId))
        {
            descriptor.Claims![JwtClaimNames.PackageId] = claims.PackageId;
        }

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new IssuedAccessToken(token, ExpiresInSeconds: _ttlMinutes * 60);
    }
}
