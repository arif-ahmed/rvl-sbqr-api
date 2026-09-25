using System.Text;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Infrastructure.Custody;

/// <summary>
/// Ed25519 signature verification over public material only — no custody,
/// no private keys, no state. Accepts the public key as either an SPKI
/// "-----BEGIN PUBLIC KEY-----" PEM (ASCII bytes, the shape
/// <c>crypto_keys.public_key</c> and the BB trust store publish) or a raw
/// 32-byte Ed25519 public key.
///
/// Malformed keys or signatures return <c>false</c> — a broken input is a
/// failed verification, not an exception: the fail-closed verdict is the
/// caller's to record.
/// </summary>
public sealed class Ed25519SignatureVerifier : ISignatureVerifier
{
    /// <inheritdoc/>
    public Task<bool> VerifyAsync(
        byte[] payload,
        byte[] signature,
        ReadOnlyMemory<byte> publicKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(signature);

        if (signature.Length != 64)
        {
            return Task.FromResult(false);
        }

        try
        {
            var parameters = ParsePublicKey(publicKey.Span);
            var verifier = new Ed25519Signer();
            verifier.Init(false, parameters);
            verifier.BlockUpdate(payload, 0, payload.Length);
            return Task.FromResult(verifier.VerifySignature(signature));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException)
        {
            return Task.FromResult(false);
        }
    }

    private static Ed25519PublicKeyParameters ParsePublicKey(ReadOnlySpan<byte> publicKey)
    {
        if (publicKey.Length == 32)
        {
            return new Ed25519PublicKeyParameters(publicKey);
        }

        // Otherwise must be an SPKI PEM block.
        var text = Encoding.ASCII.GetString(publicKey);
        if (!text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            throw new FormatException(
                $"Public key must be an SPKI PEM or 32 raw bytes; got {publicKey.Length} bytes with no PEM header.");
        }

        using var reader = new StringReader(text);
        var pemObject = new PemReader(reader).ReadPemObject()
            ?? throw new FormatException("Public key PEM could not be read.");
        var key = PublicKeyFactory.CreateKey(pemObject.Content);
        return key as Ed25519PublicKeyParameters
            ?? throw new FormatException($"Public key is a {key.GetType().Name}; expected Ed25519 SPKI.");
    }
}
