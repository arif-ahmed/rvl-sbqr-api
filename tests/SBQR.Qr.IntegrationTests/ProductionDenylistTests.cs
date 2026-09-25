using FluentAssertions;
using SBQR.SharedKernel.Application;
using Xunit;

namespace SBQR.Qr.IntegrationTests;

/// <summary>
/// Regression net for security review B4 — the production startup guard
/// refuses to boot when <c>Jwt:SigningKey</c> equals the literal dev-only
/// value. The presence check alone would happily boot if a copy-paste
/// mistake pulled the dev key out of
/// <c>scripts/dev-seed-user-secrets.sh</c> or <c>docker-compose.yml</c>
/// into Production config; this guard surfaces the mistake at startup
/// rather than at the first forged token.
///
/// <para>The denylist check lives in
/// <see cref="DevSigningKeyGuard.IsDevOnlyKey"/> in <c>SBQR.SharedKernel</c>;
/// these tests cover the exact-match logic.</para>
///
/// <para>The HTTP-host startup integration test would need a
/// <c>WebApplicationFactory</c> configured with the literal dev key +
/// <c>ASPNETCORE_ENVIRONMENT=Production</c> + the rest of the production
/// guard prerequisites satisfied; that is out of scope per the Tenancy
/// integration test project's stated convention. The pure-function
/// check below is the load-bearing assertion — the production guard
/// reads exactly this method.</para>
/// </summary>
public sealed class ProductionDenylistTests
{
    [Fact]
    public void IsDevOnlyKey_matches_the_literal_seed_value()
    {
        // The literal is the exact value seeded by
        // scripts/dev-seed-user-secrets.sh and docker-compose.yml.
        // Pinning it here means a typo in either seed file surfaces in
        // CI, not in production.
        DevSigningKeyGuard.DeniedKey.Should().Be(
            "dev-only-signing-key-change-me-0123456789abcdef");

        DevSigningKeyGuard.IsDevOnlyKey(DevSigningKeyGuard.DeniedKey).Should().BeTrue();
    }

    [Theory]
    [InlineData("production-key-from-key-vault-abc123def456ghi789jkl012mno345pq")]
    [InlineData("e2e-test-signing-key-0123456789abcdef")]
    [InlineData("dev-only-signing-key-change-me-0123456789abcdef0")] // one extra char
    [InlineData("dev-only-signing-key-change-me-0123456789abcde")]  // truncated
    [InlineData("DEV-ONLY-SIGNING-KEY-CHANGE-ME-0123456789ABCDEF")]  // upper-case
    [InlineData("dev-only-signing-key-change-me-0123456789abcdef ")] // trailing space
    [InlineData(" dev-only-signing-key-change-me-0123456789abcdef")] // leading space
    public void IsDevOnlyKey_rejects_everything_else(string candidate)
    {
        DevSigningKeyGuard.IsDevOnlyKey(candidate).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDevOnlyKey_rejects_null_or_whitespace(string? candidate)
    {
        DevSigningKeyGuard.IsDevOnlyKey(candidate).Should().BeFalse();
    }
}
