namespace SBQR.SharedKernel.Application;

/// <summary>
/// Production-time denylist for the literal dev-only HS256 signing key.
/// Defined in the shared kernel so the host's startup guard (which needs
/// it) and the test project (which needs to assert its behaviour) can
/// both reference it without depending on the host assembly.
///
/// <para><b>Why this exists</b> (security review B4): the production
/// guard in <c>Program.cs</c> verifies that <c>Jwt:SigningKey</c> is
/// present, but it would happily boot if a copy-paste mistake pulled
/// the dev key out of <c>scripts/dev-seed-user-secrets.sh</c> or
/// <c>docker-compose.yml</c> into Production config. This helper
/// exposes the exact-match check so the mistake surfaces at startup,
/// not at the first forged token.</para>
/// </summary>
public static class DevSigningKeyGuard
{
    /// <summary>
    /// The literal dev-only signing key string. The literal is the
    /// exact value seeded by <c>scripts/dev-seed-user-secrets.sh</c>
    /// and <c>docker-compose.yml</c>.
    /// </summary>
    public const string DeniedKey =
        "dev-only-signing-key-change-me-0123456789abcdef";

    /// <summary>
    /// Returns <c>true</c> when the candidate signing key equals the
    /// literal dev-only value. Comparison is case-sensitive, ordinal,
    /// and exact — any extra characters, differing case, or
    /// surrounding whitespace makes the check fail.
    /// </summary>
    public static bool IsDevOnlyKey(string? candidate) =>
        string.Equals(candidate, DeniedKey, StringComparison.Ordinal);
}
