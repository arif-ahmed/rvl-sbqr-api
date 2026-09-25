namespace SBQR.SharedKernel.Cryptography;

/// <summary>
/// Abstraction over private-key signing. The cryptographic boundary lives
/// entirely inside the KeyCustody module's <c>Infrastructure/Custody/</c>
/// folder; the shared kernel only knows this interface.
///
/// See tactical-design.md §3 (KeyCustody special structure) and §5
/// (Customer/Supplier relationship from QrGeneration/Verification to KeyCustody).
///
/// Implementations:
/// <list type="bullet">
///   <item><c>PlainFileSigningProvider</c> — dev only; hard-blocked in prod.</item>
///   <item><c>PemVaultSigningProvider</c> — Phase 1; encrypted software vault + external KEK unwrap.</item>
///   <item><c>HsmSigningProvider</c> — Phase 2; PKCS#11.</item>
/// </list>
/// </summary>
public interface ISigningProvider
{
    /// <summary>
    /// Stable identifier for the active signing scheme. Used in logs,
    /// in audit entries, and in the trust store to look up the matching
    /// public key for verification.
    /// </summary>
    string ProviderId { get; }

    /// <summary>
    /// Sign <paramref name="payload"/> with the currently active private key.
    /// Implementations MUST throw <see cref="System.Security.Cryptography.CryptographicException"/>
    /// on any error and MUST NOT return the private key in any form.
    /// </summary>
    /// <param name="payload">Canonical signature payload (see QrCodec domain for composition rules).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>64 raw bytes for Ed25519 signatures.</returns>
    Task<byte[]> SignAsync(byte[] payload, CancellationToken cancellationToken = default);
}
