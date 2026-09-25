using Microsoft.AspNetCore.Http;

namespace SBQR.Api.Infrastructure;

/// <summary>
/// Helper that extracts the <c>client_id</c> field from an OAuth2
/// client-credentials token request form for use as a rate-limit
/// partition key. Lives in the host assembly (next to Program.cs) rather
/// than the shared kernel because the shared kernel is
/// infrastructure-free by design (zero ASP.NET Core references). The
/// partition-key wiring in <c>Program.cs §9a</c> invokes
/// <see cref="TryExtract"/>; the integration test project asserts its
/// behaviour via <c>SBQR.Qr.IntegrationTests.RateLimitPartitionKeyTests</c>.
///
/// <para><b>Why this exists</b> (security review B3): partitioning the
/// token-endpoint rate limiter by remote IP alone lets an attacker with
/// a /24 of IPs get <c>PermitLimit × 256</c> attempts per minute against
/// a single client_id. Partitioning by <c>{client_id}|{remoteIp}</c>
/// collapses the ceiling back to <c>PermitLimit</c> per client_id × per
/// IP. Argon2id's per-verify cost (~100 ms) was the only floor before
/// this change.</para>
///
/// <para><b>Failure modes</b>: every failure path returns <c>null</c> so
/// the caller falls back to per-IP-only partitioning — the same
/// protection the pre-fix version offered. The function never throws,
/// never blocks legitimate clients, and bounds work to 4 KiB of body
/// read.</para>
/// </summary>
public static class RateLimitClientIdExtractor
{
    /// <summary>
    /// Maximum bytes read from the request body. A well-formed
    /// client_id is at most ~128 chars; anything larger is a probe
    /// and we want to bound the work the rate limiter does.
    /// </summary>
    public const int BodyReadLimit = 4096;

    /// <summary>
    /// Maximum accepted client_id length (chars). Anything longer is
    /// rejected to bound the partition-key dictionary size an attacker
    /// can inflate.
    /// </summary>
    public const int MaxClientIdLength = 128;

    /// <summary>
    /// Synchronously extract <c>client_id</c> from the token-endpoint
    /// form, then rewind the body so downstream model binding still
    /// works. Returns <c>null</c> on any failure (malformed body,
    /// missing form, over-long value, parse error, control characters).
    /// </summary>
    public static string? TryExtract(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            context.Request.EnableBuffering(bufferThreshold: BodyReadLimit, bufferLimit: BodyReadLimit);
            context.Request.Body.Position = 0;

            // ASP.NET Core's FormCollection is async, but we wrap it in
            // GetAwaiter().GetResult() because the rate-limiter partition
            // callback is synchronous. For a 4 KiB max body this is
            // negligible (sub-millisecond).
            var form = context.Request.ReadFormAsync().GetAwaiter().GetResult();
            var raw = form["client_id"].ToString();

            context.Request.Body.Position = 0;

            return IsValidClientId(raw) ? raw : null;
        }
        catch
        {
            // Malformed body / size exceeded / parse error. Rewind and
            // fall through to per-IP-only partition so the request still
            // gets rate-limited.
            try
            {
                context.Request.Body.Position = 0;
            }
            catch
            {
            }

            return null;
        }
    }

    /// <summary>
    /// Pure-function validation of a candidate client_id. Exposed for
    /// tests so the validation rule (printable ASCII, bounded length)
    /// can be exercised without spinning up an HttpContext.
    /// </summary>
    public static bool IsValidClientId(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return false;
        }

        if (raw.Length > MaxClientIdLength)
        {
            return false;
        }

        foreach (var ch in raw)
        {
            if (ch < 0x20 || ch > 0x7E)
            {
                return false;
            }
        }

        return true;
    }
}
