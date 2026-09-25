namespace SBQR.Modules.KeyCustody.Domain.Interfaces;

/// <summary>
/// Domain-side port for the "Adopt" scenario: take a tenant-supplied Ed25519
/// private-key PEM, parse it, and return the canonical SPKI-encoded public
/// PEM derived from the supplied private. The Infrastructure implementation
/// (<c>SBQR.Modules.KeyCustody.Infrastructure.Cryptography.Ed25519PEMValidator</c>)
/// uses BouncyCastle to perform RFC 8032 deterministic pub derivation; the
/// returned PEM is the canonical shape the <c>crypto_keys.public_key</c>
/// column stores. Throws <see cref="System.ArgumentException"/> on any parse
/// failure so the Application handler can map to
/// <c>Result.Failure(InvariantViolation, …)</c>.
///
/// Lives alongside <see cref="IKeyPairGenerator"/> in Domain.Interfaces (not
/// Application) so that Infrastructure — which does not reference
/// Application, to avoid a circular dependency with the DbContext-backed
/// query handlers — can implement it directly.
/// </summary>
public interface IKeyPairValidator
{
    /// <summary>
    /// Parse the supplied Ed25519 private-key PEM and derive the
    /// corresponding public key (RFC 8032
    /// <c>pub = scalar_base_mul(seed)</c>) via BouncyCastle. Returns the
    /// canonical SPKI-encoded public PEM wrapped in a standard
    /// <c>BEGIN PUBLIC KEY</c> / <c>END PUBLIC KEY</c> block — the shape the
    /// <c>crypto_keys.public_key</c> and
    /// <c>public.institution_keys.public_key</c> columns store.
    /// </summary>
    /// <param name="privateKeyPem">Ed25519 private-key PEM (PKCS#8 unencrypted).</param>
    /// <returns>The canonical SPKI public-key PEM.</returns>
    /// <exception cref="System.ArgumentException">
    /// Thrown when the PEM is malformed, has unsupported headers, or the
    /// decoded key is not Ed25519.
    /// </exception>
    string DerivePublicKeyPem(string privateKeyPem);
}
