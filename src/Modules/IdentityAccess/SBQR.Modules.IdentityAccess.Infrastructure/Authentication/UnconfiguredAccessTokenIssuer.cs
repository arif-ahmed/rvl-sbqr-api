using SBQR.Modules.IdentityAccess.Application.Abstractions;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Authentication;

/// <summary>
/// Fallback <see cref="IAccessTokenIssuer"/> registered when
/// <c>Jwt:SigningKey</c> is absent or too short. It lets the host (and the
/// integration-test composition, which carries no JWT configuration) boot
/// with every non-token surface intact, while the token endpoint fails
/// loudly with instructions instead of silently issuing unsigned tokens.
/// Validation likewise refuses to boot in Production without the key, so an
/// unconfigured host can never accept the tokens either.
/// </summary>
public sealed class UnconfiguredAccessTokenIssuer : IAccessTokenIssuer
{
    /// <inheritdoc/>
    public IssuedAccessToken Issue(AccessTokenClaims claims) => throw new InvalidOperationException(
        "IAccessTokenIssuer is not configured: Jwt:SigningKey is missing or shorter than 32 characters. " +
        "Set Jwt:SigningKey (env var Jwt__SigningKey, user-secrets, or Key Vault) and restart. " +
        "It must match the key the IdentityAccess bearer pipeline validates with.");
}
