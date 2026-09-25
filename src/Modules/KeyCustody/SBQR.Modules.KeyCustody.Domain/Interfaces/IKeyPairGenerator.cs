namespace SBQR.Modules.KeyCustody.Domain.Interfaces;

/// <summary>
/// Domain-side port for an in-process Ed25519 keypair generator. The
/// Infrastructure-layer implementation lives at
/// <c>SBQR.Modules.KeyCustody.Infrastructure.Cryptography.Ed25519KeyPairGenerator</c>.
/// Domain code never imports <c>System.Security.Cryptography</c> for
/// algorithm choices — only this port.
/// </summary>
public interface IKeyPairGenerator
{
    /// <summary>
    /// Generate a fresh Ed25519 keypair.
    /// </summary>
    /// <returns>
    /// <para><c>PublicKeyPem</c> — PEM-encoded SPKI public key, ready to
    /// persist in <c>crypto_keys.public_key</c>.</para>
    /// <para><c>PrivateKeyBytes</c> — the PKCS#8 PEM bytes; the caller hands
    /// these to <see cref="SBQR.SharedKernel.Cryptography.ISigningKeyStore.Store"/>
    /// for wrapping and storage.</para>
    /// </returns>
    (string PublicKeyPem, byte[] PrivateKeyBytes) GenerateEd25519();
}
