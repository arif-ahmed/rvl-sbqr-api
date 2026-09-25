using System.Text;
using FluentAssertions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Ed25519 verify round-trips against a test-side BouncyCastle signer —
/// an independent construction path from the verifier under test. Covers
/// both accepted public-key shapes (SPKI PEM and raw 32 bytes) and the
/// fail-closed inputs.
/// </summary>
public sealed class Ed25519SignatureVerifierTests
{
    private static (Ed25519PrivateKeyParameters PrivateKey, Ed25519PublicKeyParameters PublicKey, string PublicPem)
        GenerateKey()
    {
        var seed = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        var privateKey = new Ed25519PrivateKeyParameters(seed, 0);
        var publicKey = privateKey.GeneratePublicKey();

        using var writer = new StringWriter();
        using (var pemWriter = new PemWriter(writer))
        {
            pemWriter.WriteObject(publicKey);
        }

        return (privateKey, publicKey, writer.ToString());
    }

    private static byte[] Sign(Ed25519PrivateKeyParameters key, byte[] payload)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, key);
        signer.BlockUpdate(payload, 0, payload.Length);
        return signer.GenerateSignature();
    }

    [Theory]
    [InlineData("Arif Mahmood01711111111")]
    [InlineData("AreebaNawar01711111111")]
    public async Task Valid_signature_verifies_against_the_spki_pem_public_key(string payloadText)
    {
        var (privateKey, _, publicPem) = GenerateKey();
        var payload = Encoding.UTF8.GetBytes(payloadText);
        var signature = Sign(privateKey, payload);
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(payload, signature, Encoding.ASCII.GetBytes(publicPem));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Valid_signature_verifies_against_the_raw_32_byte_public_key()
    {
        var (privateKey, publicKey, _) = GenerateKey();
        var payload = Encoding.UTF8.GetBytes("payload");
        var signature = Sign(privateKey, payload);
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(payload, signature, publicKey.GetEncoded());

        result.Should().BeTrue();
    }

    [Fact]
    public async Task Tampered_payload_fails_verification()
    {
        var (privateKey, _, publicPem) = GenerateKey();
        var signature = Sign(privateKey, Encoding.UTF8.GetBytes("original"));
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(
            Encoding.UTF8.GetBytes("tampered"),
            signature,
            Encoding.ASCII.GetBytes(publicPem));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Tampered_signature_fails_verification()
    {
        var (privateKey, _, publicPem) = GenerateKey();
        var payload = Encoding.UTF8.GetBytes("payload");
        var signature = Sign(privateKey, payload);
        signature[0] ^= 0xFF;
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(payload, signature, Encoding.ASCII.GetBytes(publicPem));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Non_64_byte_signature_returns_false_without_throwing()
    {
        var (_, _, publicPem) = GenerateKey();
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(
            Encoding.UTF8.GetBytes("payload"),
            new byte[63],
            Encoding.ASCII.GetBytes(publicPem));

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Malformed_public_key_returns_false_without_throwing()
    {
        var (privateKey, _, _) = GenerateKey();
        var payload = Encoding.UTF8.GetBytes("payload");
        var signature = Sign(privateKey, payload);
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(payload, signature, new byte[17]);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task Pem_vault_provider_key_shapes_are_mutually_verifiable()
    {
        // Cross-check the stored shapes: a PKCS#8 private PEM (exactly what
        // both registration scenarios store) signs a payload that verifies
        // against the SPKI public PEM (exactly what crypto_keys.public_key
        // stores).
        var (privateKey, _, publicPem) = GenerateKey();

        using var writer = new StringWriter();
        using (var pemWriter = new PemWriter(writer))
        {
            pemWriter.WriteObject(PrivateKeyInfoFactory.CreatePrivateKeyInfo(privateKey));
        }
        var privatePkcs8Pem = Encoding.ASCII.GetBytes(writer.ToString());

        // Re-parse the PKCS#8 PEM the same way PemVaultSigningProvider does.
        using var reader = new StringReader(Encoding.ASCII.GetString(privatePkcs8Pem));
        var pemObject = new PemReader(reader).ReadPemObject();
        var reparsed =
            (Ed25519PrivateKeyParameters)Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(pemObject.Content);

        var payload = Encoding.UTF8.GetBytes("AreebaNawar01711111111");
        var signature = Sign(reparsed, payload);
        var verifier = new Ed25519SignatureVerifier();

        var result = await verifier.VerifyAsync(payload, signature, Encoding.ASCII.GetBytes(publicPem));

        result.Should().BeTrue(
            "the PKCS#8-stored private key must sign payloads verifiable by the stored SPKI public PEM");
    }
}
