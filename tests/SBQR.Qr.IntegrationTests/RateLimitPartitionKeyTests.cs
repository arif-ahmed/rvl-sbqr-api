using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SBQR.Api.Infrastructure;
using Xunit;

namespace SBQR.Qr.IntegrationTests;

/// <summary>
/// Regression net for security review B3 — the OAuth token-endpoint rate
/// limiter now partitions by <c>{client_id}|{remoteIp}</c> instead of
/// <c>{remoteIp}</c>, so an attacker with a /24 of IPs cannot get
/// PermitLimit × 256 attempts per minute against a single client_id.
/// Argon2id's per-verify cost was the only floor before this change.
///
/// <para>The partition-key extraction logic lives in
/// <see cref="RateLimitClientIdExtractor"/> in <c>SBQR.Api</c>;
/// these tests cover the extractor's pure-function validation rules
/// (<see cref="RateLimitClientIdExtractor.IsValidClientId"/>) plus a full
/// <see cref="HttpContext"/> round-trip via
/// <see cref="RateLimitClientIdExtractor.TryExtract"/>.</para>
///
/// <para>The HTTP middleware itself (which wires the extractor into the
/// rate-limit partition callback) requires a running ASP.NET Core
/// host — covered by the manual smoke-test in
/// <c>docs/bootstrap-dev-guide.md</c> Step 8, not by an integration
/// test. Wire-shape HTTP-rate-limit tests would need a
/// WebApplicationFactory with a custom <c>RateLimit:TokenEndpoint:PermitLimit</c>
/// override (out of scope per the Tenancy integration test project's
/// stated convention).</para>
/// </summary>
public sealed class RateLimitPartitionKeyTests
{
    // ---------------------------------------------------------------------
    // Pure-function validation rules
    // ---------------------------------------------------------------------

    [Theory]
    [InlineData("platform-bootstrap")]
    [InlineData("020202-0123abcd")]
    [InlineData("a")]
    [InlineData("x.y-z_0:1")]
    public void IsValidClientId_accepts_printable_ascii_within_length(string candidate)
    {
        RateLimitClientIdExtractor.IsValidClientId(candidate).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsValidClientId_rejects_null_or_empty(string? candidate)
    {
        RateLimitClientIdExtractor.IsValidClientId(candidate).Should().BeFalse();
    }

    [Fact]
    public void IsValidClientId_rejects_over_long_values()
    {
        var overLong = new string('a', RateLimitClientIdExtractor.MaxClientIdLength + 1);
        RateLimitClientIdExtractor.IsValidClientId(overLong).Should().BeFalse();
    }

    [Fact]
    public void IsValidClientId_accepts_values_at_the_length_boundary()
    {
        var atBoundary = new string('a', RateLimitClientIdExtractor.MaxClientIdLength);
        RateLimitClientIdExtractor.IsValidClientId(atBoundary).Should().BeTrue();
    }

    [Theory]
    [InlineData("tab\there")]
    [InlineData("newline\nhere")]
    [InlineData("crlf\r\nhere")]
    [InlineData("nul\0here")]
    [InlineData("high\x7F")]
    [InlineData("non\xE9ascii")]
    public void IsValidClientId_rejects_control_or_non_ascii_characters(string candidate)
    {
        RateLimitClientIdExtractor.IsValidClientId(candidate).Should().BeFalse();
    }

    // ---------------------------------------------------------------------
    // Full HttpContext round-trip
    // ---------------------------------------------------------------------

    [Fact]
    public async Task TryExtract_returns_client_id_from_application_x_www_form_urlencoded_body()
    {
        var context = BuildContext(
            "grant_type=client_credentials&client_id=020202-0123abcd&client_secret=secret");

        var clientId = RateLimitClientIdExtractor.TryExtract(context);

        clientId.Should().Be("020202-0123abcd");
    }

    [Fact]
    public async Task TryExtract_rewinds_body_so_model_binding_still_works()
    {
        const string body =
            "grant_type=client_credentials&client_id=rewind-check&client_secret=secret";
        var context = BuildContext(body);

        RateLimitClientIdExtractor.TryExtract(context).Should().Be("rewind-check");

        // Re-read the form: if the extractor did not rewind, the second
        // ReadFormAsync call returns an empty collection. This protects
        // downstream model binding from being starved by the rate-limit
        // partition callback.
        var form = await context.Request.ReadFormAsync();
        form["client_id"].ToString().Should().Be("rewind-check");
        form["client_secret"].ToString().Should().Be("secret");
        form["grant_type"].ToString().Should().Be("client_credentials");
    }

    [Fact]
    public void TryExtract_returns_null_when_client_id_is_missing()
    {
        var context = BuildContext("grant_type=client_credentials&client_secret=secret");

        RateLimitClientIdExtractor.TryExtract(context).Should().BeNull();
    }

    [Fact]
    public void TryExtract_returns_null_when_body_is_over_size_limit()
    {
        // Build a body > 4 KiB. The exact threshold is
        // RateLimitClientIdExtractor.BodyReadLimit; we exceed it by 1 KiB.
        var padding = new string('x', RateLimitClientIdExtractor.BodyReadLimit + 1024);
        var context = BuildContext(
            $"grant_type=client_credentials&client_id=valid-id&garbage={padding}");

        // The extractor must NEVER throw — failure paths return null so
        // the caller falls back to per-IP-only partitioning.
        RateLimitClientIdExtractor.TryExtract(context).Should().BeNull();
    }

    [Fact]
    public void TryExtract_returns_null_when_client_id_exceeds_length_limit()
    {
        var overLong = new string('a', RateLimitClientIdExtractor.MaxClientIdLength + 1);
        var context = BuildContext(
            $"grant_type=client_credentials&client_id={overLong}&client_secret=secret");

        RateLimitClientIdExtractor.TryExtract(context).Should().BeNull();
    }

    private static DefaultHttpContext BuildContext(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentType = "application/x-www-form-urlencoded";
        ctx.Request.Method = "POST";
        ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("127.0.0.1");
        return ctx;
    }
}
