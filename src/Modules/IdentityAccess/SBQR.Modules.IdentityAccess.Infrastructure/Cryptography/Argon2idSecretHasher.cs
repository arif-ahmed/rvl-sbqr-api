using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using SBQR.Modules.IdentityAccess.Application.Abstractions;

namespace SBQR.Modules.IdentityAccess.Infrastructure.Cryptography;

/// <summary>
/// Default <see cref="ISecretHasher"/> implementation. Uses
/// <c>Isopoh.Cryptography.Argon2</c> with OWASP-2024 defaults
/// (<c>m=64 MiB</c>, <c>t=3</c>, <c>p=1</c>) and emits the PHC-format hash
/// string mandated by <c>docs/design/database-design.md</c> §1.3
/// (<c>$argon2id$v=19$m=…,t=…,p=…$&lt;salt&gt;$&lt;digest&gt;</c>).
///
/// Moved here from the Tenancy module when the OAuth2 client-credentials
/// surface relocated; only the IdentityAccess module's token endpoint and
/// credential provisioner touch <c>api_credentials.client_secret_hash</c>.
/// </summary>
public sealed class Argon2idSecretHasher : ISecretHasher
{
    /// <inheritdoc/>
    public string Hash(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var salt = RandomNumberGenerator.GetBytes(Argon2idParameters.SaltBytes);

        var config = new Argon2Config
        {
            // Isopoh v2.0.0 maps the Argon2id variant to Argon2Type.HybridAddressing
            // (= 2). DataDependentAddressing is Argon2d; DataIndependentAddressing
            // is Argon2i. The PHC string Argon2.Hash emits still carries the
            // "$argon2id$" prefix because the underlying variant is hybrid.
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            Password = Encoding.UTF8.GetBytes(plaintext),
            Salt = salt,
            MemoryCost = Argon2idParameters.MemoryKib,
            TimeCost = Argon2idParameters.Iterations,
            Lanes = Argon2idParameters.Parallelism,
            Threads = Argon2idParameters.Parallelism,
            HashLength = Argon2idParameters.HashLength,
        };

        // Argon2.Hash(Argon2Config) returns the full PHC string:
        //   $argon2id$v=19$m=.,t=.,p=.$<base64-salt>$<base64-digest>
        var phc = Argon2.Hash(config);
        if (string.IsNullOrEmpty(phc))
        {
            throw new InvalidOperationException(
                "Argon2id hashing returned an empty PHC string.");
        }

        return phc;
    }

    /// <inheritdoc/>
    public bool Verify(string plaintext, string phcEncodedHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(phcEncodedHash);

        // Defensive precondition (FR-AUTH-001 §5 BR6 — every auth failure
        // returns the same 401 invalid_client). A malformed stored hash
        // must NEVER throw upward into the pipeline; that would leak config
        // state via a 500. The IdentityAccess module's
        // PlatformBootstrapOptions PostConfigure warns at startup when the
        // configured hash is not a PHC string; this Verify-time check is
        // the second line of defence.
        if (!phcEncodedHash.StartsWith("$argon2id$", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            return Argon2.Verify(phcEncodedHash, Encoding.UTF8.GetBytes(plaintext));
        }
        catch
        {
            return false;
        }
    }
}
