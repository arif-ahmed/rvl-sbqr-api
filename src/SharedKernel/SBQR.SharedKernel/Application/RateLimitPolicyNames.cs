namespace SBQR.SharedKernel.Application;

/// <summary>
/// Rate-limiting policy names. The host registers the concrete limiter
/// policies via <c>AddRateLimiter</c> in <c>SBQR.Api/Program.cs</c>; modules
/// reference these constants in <c>[EnableRateLimiting(...)]</c> attributes
/// so that no module takes a dependency on the host's rate-limit wiring.
/// </summary>
public static class RateLimitPolicyNames
{
    /// <summary>
    /// Fixed-window limiter on the OAuth2 token endpoint
    /// (<c>POST /v1/oauth/token</c>). Brute-force blunt force: without it, an
    /// attacker can attempt unlimited client_secret guesses against the
    /// Argon2id verification path.
    /// </summary>
    public const string TokenEndpoint = "token-endpoint";
}
