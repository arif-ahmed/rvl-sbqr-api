using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using SBQR.Modules.KeyCustody.Domain.Interfaces;

namespace SBQR.Modules.KeyCustody.Infrastructure.Cryptography;

/// <summary>
/// Default <see cref="IKeyPairValidator"/> implementation. Parses a
/// tenant-supplied Ed25519 private-key PEM with BouncyCastle, derives the
/// canonical SPKI public key from the private (RFC 8032
/// <c>pub = scalar_base_mul(seed)</c>), and returns it wrapped in a standard
/// <c>BEGIN PUBLIC KEY</c> PEM block. Throws <see cref="ArgumentException"/>
/// on any parse failure so the handler can map to
/// <c>Result.Failure(InvariantViolation, …)</c>.
///
/// <para>
/// Post-2026-09-09 Adopt-mode refactor: only the private-key half is supplied
/// in the Adopt request body. The handler compares the SHA-256 of the
/// derived PEM against <c>public.institution_keys.public_key_sha256</c>
/// (the drift guard). The half-pair consistency check the previous
/// <c>EnsureMatches</c> implementation performed is gone — the cross-check
/// is now "derived pub equals trust-stored pub", which is strictly stronger
/// because it includes the trust anchor.
/// </para>
/// </summary>
public sealed class Ed25519PEMValidator : IKeyPairValidator
{
    /// <inheritdoc/>
    public string DerivePublicKeyPem(string privateKeyPem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyPem);

        Ed25519PrivateKeyParameters privateKey;
        try
        {
            // PemReader from BouncyCastle.OpenSsl parses OpenSSL PEM headers
            // (e.g. "-----BEGIN PRIVATE KEY-----") and returns either an
            // AsymmetricKeyParameter (for unencrypted keys) or a PrivateKeyInfo
            // ASN.1 structure.
            using var reader = new PemReader(new StringReader(privateKeyPem));
            var parsed = reader.ReadObject();
            privateKey = parsed switch
            {
                Ed25519PrivateKeyParameters typed => typed,
                Org.BouncyCastle.Asn1.Pkcs.PrivateKeyInfo info
                    => ToEd25519Private(PrivateKeyFactory.CreateKey(info)),
                Org.BouncyCastle.Crypto.AsymmetricKeyParameter key
                    when key is Ed25519PrivateKeyParameters pk => pk,
                _ => throw new ArgumentException(
                    $"Unsupported private-key PEM object type: {parsed?.GetType().FullName ?? "null"}."),
            };
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            throw new ArgumentException(
                "private_key_pem did not parse as an Ed25519 private key.", ex);
        }

        var derivedPublic = privateKey.GeneratePublicKey();

        // Return the canonical SPKI PEM derived from the private key. This
        // guarantees the DB stores one consistent shape regardless of the
        // caller's input encoding (BEGIN PUBLIC KEY … END PUBLIC KEY) and
        // gives the drift guard a stable byte sequence to SHA-256.
        using var sw = new StringWriter();
        using (var pemWriter = new PemWriter(sw))
        {
            pemWriter.WriteObject(derivedPublic);
        }
        return sw.ToString();
    }

    private static Ed25519PrivateKeyParameters ToEd25519Private(
        Org.BouncyCastle.Crypto.AsymmetricKeyParameter key)
    {
        if (key is Ed25519PrivateKeyParameters ed)
        {
            return ed;
        }
        throw new ArgumentException(
            "private_key_pem did not parse as an Ed25519 private key " +
            $"(got {key.GetType().FullName}).");
    }
}
