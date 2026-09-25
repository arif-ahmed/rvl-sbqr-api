namespace SBQR.Modules.IdentityAccess.Application.Abstractions;

/// <summary>
/// Application-side port for an Argon2id-based secret hasher. The
/// Infrastructure-layer implementation is
/// <c>SBQR.Modules.IdentityAccess.Infrastructure.Cryptography.Argon2idSecretHasher</c>,
/// which uses <c>Isopoh.Cryptography.Argon2</c> and emits the PHC-format hash
/// string mandated by <c>docs/design/database-design.md</c> §1.3
/// (<c>$argon2id$v=19$m=…$t=…$p=…$&lt;salt&gt;$&lt;digest&gt;</c>).
///
/// Plaintext client_secret NEVER appears in the database; the hasher is the
/// only way the secret reaches the <c>api_credentials</c> row.
/// </summary>
public interface ISecretHasher
{
    /// <summary>
    /// Compute a fresh Argon2id PHC-format hash of <paramref name="plaintext"/>.
    /// Each call uses a fresh 16-byte random salt, OWASP-2024 defaults
    /// (<c>m=64 MiB</c>, <c>t=3</c>, <c>p=1</c>).
    /// </summary>
    /// <param name="plaintext">The cleartext secret to hash.</param>
    /// <returns>The PHC-format encoded hash string, ready to persist.</returns>
    string Hash(string plaintext);

    /// <summary>
    /// Verify <paramref name="plaintext"/> against a previously-hashed
    /// <paramref name="phcEncodedHash"/>. Constant-time comparison; safe for
    /// the token-endpoint hot path.
    /// </summary>
    /// <param name="plaintext">The cleartext secret to verify.</param>
    /// <param name="phcEncodedHash">A previously produced PHC-format hash.</param>
    /// <returns><c>true</c> if the plaintext matches; otherwise <c>false</c>.</returns>
    bool Verify(string plaintext, string phcEncodedHash);
}
