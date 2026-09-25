namespace SBQR.SharedKernel.Cryptography;

/// <summary>
/// Abstraction over public-key signature verification. Implemented in
/// the KeyCustody module (and any other module that needs verification).
/// </summary>
public interface ISignatureVerifier
{
    /// <summary>
    /// Verify <paramref name="signature"/> against <paramref name="payload"/>
    /// using the Ed25519 public key in <paramref name="publicKey"/>.
    /// </summary>
    /// <returns><c>true</c> iff the signature is valid.</returns>
    Task<bool> VerifyAsync(
        byte[] payload,
        byte[] signature,
        ReadOnlyMemory<byte> publicKey,
        CancellationToken cancellationToken = default);
}
