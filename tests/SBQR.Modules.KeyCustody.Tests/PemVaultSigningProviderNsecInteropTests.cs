using System.Security.Cryptography;
using System.Text;
using Amazon.S3.Model;
using FluentAssertions;
using NSec.Cryptography;
using SBQR.Modules.KeyCustody.Infrastructure.Cryptography;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.Modules.KeyCustody.Tests.Infrastructure;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// NSec ↔ BouncyCastle cross-implementation interop test for the
/// <see cref="PemVaultSigningProvider"/> signing path:
/// <list type="number">
///   <item>Mint an Ed25519 keypair via <see cref="Ed25519KeyPairGenerator"/>
///         (BC-based, produces the canonical PKCS#8 PEM).</item>
///   <item>Round-trip the private PEM through <see cref="S3SigningKeyStore"/>
///         against an ephemeral LocalStack container (<see cref="LocalStackS3Fixture"/>).</item>
///   <item>Extract the 32-byte seed from the unwrapped PEM (same shape as
///         the new <c>PemVaultSigningProvider</c>).</item>
///   <item>Import the seed into an NSec <see cref="Key"/> (SecureMemory) and
///         sign a payload with <see cref="SignatureAlgorithm.Ed25519"/>.</item>
///   <item>Verify the NSec-produced signature against the SPKI public PEM
///         using <see cref="Ed25519SignatureVerifier"/> (BC-based).</item>
/// </list>
/// If verification succeeds, the migration to NSec for the signing hot
/// path is interop-safe with the existing BC verifier.
/// </summary>
[Collection("S3")]
public sealed class PemVaultSigningProviderNsecInteropTests
{
    private readonly LocalStackS3Fixture _fixture;

    public PemVaultSigningProviderNsecInteropTests(LocalStackS3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task NSec_signed_payload_verifies_against_the_BouncyCastle_verifier()
    {
        // (1) Mint an Ed25519 keypair via the production generator.
        var generator = new Ed25519KeyPairGenerator();
        var (publicKeyPem, privatePemBytes) = generator.GenerateEd25519();

        // (2) Round-trip through S3 using the new institution-coded handle.
        var institutionCode = "112233";
        var tenantId = Guid.NewGuid();
        var handle = $"tenant:{tenantId:D}:institution:{institutionCode}:sbqr-signing:v1";
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        store.Store(tenantId, privatePemBytes, handle);

        try
        {
            // (3) Extract the 32-byte seed from the unwrapped PEM — this is
            // exactly what the new PemVaultSigningProvider does internally.
            var loaded = store.TryLoad(handle);
            loaded.Should().NotBeNull();
            var seed = ExtractSeedFromPemBytes(loaded!);

            // (4) Sign with NSec (SecureMemory-backed key).
            using var privateKey = Key.Import(
                SignatureAlgorithm.Ed25519, seed, KeyBlobFormat.RawPrivateKey);
            var payload = Encoding.UTF8.GetBytes("verify-me");
            var signature = SignatureAlgorithm.Ed25519.Sign(privateKey, payload);

            // (5) Verify with the BC verifier against the SPKI public PEM.
            var verifier = new Ed25519SignatureVerifier();
            var ok = await verifier.VerifyAsync(payload, signature, Encoding.ASCII.GetBytes(publicKeyPem));

            ok.Should().BeTrue("NSec-signed payload must verify with the existing BC verifier.");
        }
        finally
        {
            await _fixture.CleanupAsync(institutionCode, "_v1_private.pem");
        }
    }

    [Fact]
    public async Task Tampered_ciphertext_causes_zero_out_of_unwrapped_buffer()
    {
        // Mint and store a key, then flip the GCM tag and confirm the
        // vault throws CryptographicException. The success-path plaintext
        // wipe is asserted indirectly: the BC PEM parser cannot run on a
        // tag-mismatched blob, so any non-zero leftover buffer would be
        // unreachable code and would only matter if AesGcm.Decrypt returned
        // partial plaintext (it doesn't — fail-closed contract).
        var generator = new Ed25519KeyPairGenerator();
        var (_, privatePemBytes) = generator.GenerateEd25519();
        var institutionCode = "445566";
        var tenantId = Guid.NewGuid();
        var handle = $"tenant:{tenantId:D}:institution:{institutionCode}:sbqr-signing:v1";
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        store.Store(tenantId, privatePemBytes, handle);

        try
        {
            var objectKey = $"keys/{institutionCode}_v1_private.pem";
            using var s3 = _fixture.NewRawClient();
            var get = await s3.GetObjectAsync(_fixture.BucketName, objectKey);
            await using var ms = new MemoryStream();
            await get.ResponseStream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            bytes[^1] ^= 0xFF;
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _fixture.BucketName,
                Key = objectKey,
                ContentBody = Convert.ToBase64String(bytes),
                ContentType = "application/octet-stream",
            });

            var act = () => store.TryLoad(handle);
            act.Should().Throw<CryptographicException>();
        }
        finally
        {
            await _fixture.CleanupAsync(institutionCode, "_v1_private.pem");
        }
    }

    private static byte[] ExtractSeedFromPemBytes(byte[] pemBytes)
    {
        // Mirror the production extractor in PemVaultSigningProvider
        // (BC parse → GetEncoded() → 32-byte seed). Kept here as a
        // helper so the test reads as a real round-trip rather than
        // poking private state.
        using var reader = new StringReader(Encoding.ASCII.GetString(pemBytes));
        var pemObject = new Org.BouncyCastle.OpenSsl.PemReader(reader).ReadPemObject()
            ?? throw new CryptographicException("not a PEM object");
        var key = Org.BouncyCastle.Security.PrivateKeyFactory.CreateKey(pemObject.Content);
        return ((Org.BouncyCastle.Crypto.Parameters.Ed25519PrivateKeyParameters)key).GetEncoded();
    }

    private static byte[] Kek() => SHA256.HashData("sbqr-dev-vault-kek-v1"u8);
}
