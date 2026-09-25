using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace SBQR.Modules.IdentityAccess.Application.Abstractions;

/// <summary>
/// Canonical OWASP-2024 Argon2id parameters used by every secret hasher in
/// the IdentityAccess module (bootstrap <c>client_secret</c>, tenant
/// <c>client_secret</c>, docs-internal-admin password). Centralised here so
/// every caller — hasher implementations, test vectors, the timing-equalizer
/// constant — references the same single source of truth. The package
/// reference to <c>Isopoh.Cryptography.Argon2</c> is intentional: Argon2id
/// is a domain concept, not an infrastructure detail, and having one place
/// to change parameters keeps the whole module consistent.
///
/// <para><b>Why this lives in Application, not Infrastructure:</b> the
/// timing-equalizer constant (see <see cref="TimingEqualizerPhc"/>) is a
/// security primitive that the OAuth handler needs to reference without
/// taking a dependency on the Infrastructure assembly. The hasher itself
/// also reads these constants, so keeping them in Application's
/// Abstractions avoids a circular reference.</para>
/// </summary>
public static class Argon2idParameters
{
    /// <summary>Memory cost in KiB (64 MiB).</summary>
    public const int MemoryKib = 64 * 1024;

    /// <summary>Time cost (iterations).</summary>
    public const int Iterations = 3;

    /// <summary>Parallelism (lanes / threads).</summary>
    public const int Parallelism = 1;

    /// <summary>Salt length in bytes.</summary>
    public const int SaltBytes = 16;

    /// <summary>Digest length in bytes.</summary>
    public const int HashLength = 32;

    /// <summary>
    /// Pre-computed Argon2id PHC string used to equalize wall-time on the
    /// tenant-token rejection paths. The OAuth handler's tenant branch
    /// (<see cref="Commands.IssueToken.IssueClientCredentialsTokenCommandHandler"/>)
    /// runs a dummy <see cref="ISecretHasher.Verify"/> against this constant
    /// whenever a request is rejected before the real verify would have run
    /// (unknown client_id, suspended credential, expired credential, wrong
    /// secret). Without the dummy run an attacker can enumerate valid
    /// client_ids by response latency (FR-AUTH-001 §5 BR6).
    ///
    /// <para>The constant is computed once at first access using the same
    /// parameter set the production hasher uses, so the dummy run costs
    /// the same as a real run (~100 ms at the OWASP defaults).</para>
    ///
    /// <para>Lazy + swallow: a failed Argon2 init (e.g. missing native
    /// binding on a constrained container) returns <c>null</c> rather
    /// than bricking the host at static-init time. The handler's
    /// timing-equalizer absorbs the <c>null</c> via its try/catch and
    /// simply skips the dummy run on that request — accepting a
    /// timing leak on the misconfigured host rather than 500ing every
    /// token request.</para>
    /// </summary>
    public static string? TimingEqualizerPhc => _timingEqualizerPhc.Value;

    private static readonly Lazy<string?> _timingEqualizerPhc = new(() =>
    {
        try
        {
            return HashOnce();
        }
        catch
        {
            return null;
        }
    });

    private static string HashOnce()
    {
        const string placeholder = "sbqr-timing-equalizer-do-not-use";

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var config = new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            Password = Encoding.UTF8.GetBytes(placeholder),
            Salt = salt,
            MemoryCost = MemoryKib,
            TimeCost = Iterations,
            Lanes = Parallelism,
            Threads = Parallelism,
            HashLength = HashLength,
        };

        var phc = Argon2.Hash(config);
        if (string.IsNullOrEmpty(phc))
        {
            throw new InvalidOperationException(
                "Argon2id timing-equalizer PHC returned an empty string.");
        }

        return phc;
    }
}
