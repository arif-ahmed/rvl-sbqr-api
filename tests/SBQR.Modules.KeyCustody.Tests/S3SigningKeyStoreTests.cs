using System.Security.Cryptography;
using FluentAssertions;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.Modules.KeyCustody.Tests.Infrastructure;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// S3SigningKeyStore contract tests against an ephemeral LocalStack
/// container (see <see cref="LocalStackS3Fixture"/>) — never the real dev
/// bucket described in docs/dev-s3-guide.md.
///
/// <para>
/// Wire format mirrors <see cref="EncryptedFileSigningKeyStore"/>:
/// "SBQRKEY1" magic ‖ nonce ‖ ciphertext ‖ tag, AES-256-GCM with the handle
/// as AAD. Same KEK bytes in dev so a blob could in principle migrate
/// between the local file vault and S3 unchanged.
/// </para>
///
/// <para>
/// Object-key layout (operator-readable):
/// <c>keys/&lt;institutionCode&gt;_v&lt;n&gt;_private.pem</c> — one
/// immutable object per (institution, version) pair. The institution code
/// and the version are encoded into the handle by
/// <c>CryptoKey.ComposeCustodyHandle</c>. Rotations produce a new
/// <c>_v&lt;n+1&gt;_private.pem</c> while leaving the prior blob in
/// place; historical QRs verify against the version they were signed with.
/// </para>
/// </summary>
[Collection("S3")]
public sealed class S3SigningKeyStoreTests
{
    private readonly LocalStackS3Fixture _fixture;

    public S3SigningKeyStoreTests(LocalStackS3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Stored_material_round_trips_through_s3()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var (handle, institutionCode) = ComposeHandle(institutionCode: "012345", keyVersion: 1);
        var material = RandomMaterial();

        store.Store(Guid.NewGuid(), material, handle);

        var loaded = store.TryLoad(handle);
        loaded.Should().NotBeNull().And.Equal(material);

        await _fixture.CleanupAsync(institutionCode, "_v1_private.pem");
    }

    [Fact]
    public async Task Tampered_blob_fails_closed()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var (handle, institutionCode) = ComposeHandle(institutionCode: "012345", keyVersion: 1);
        store.Store(Guid.NewGuid(), RandomMaterial(), handle);

        // Flip the last byte (the GCM tag) in the stored object.
        var objectKey = $"keys/{institutionCode}_v1_private.pem";
        using (var s3 = _fixture.NewRawClient())
        {
            var get = await s3.GetObjectAsync(_fixture.BucketName, objectKey);
            await using var ms = new MemoryStream();
            await get.ResponseStream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            bytes[^1] ^= 0xFF;
            await s3.PutObjectAsync(new Amazon.S3.Model.PutObjectRequest
            {
                BucketName = _fixture.BucketName,
                Key = objectKey,
                ContentBody = Convert.ToBase64String(bytes),
                ContentType = "application/octet-stream",
            });
        }

        var act = () => store.TryLoad(handle);
        act.Should().Throw<CryptographicException>();

        await _fixture.CleanupAsync(institutionCode, "_v1_private.pem");
    }

    [Fact]
    public async Task Store_writes_exactly_one_versioned_object_and_no_flat_mirror()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var (handle, institutionCode) = ComposeHandle(institutionCode: "678901", keyVersion: 1);
        store.Store(Guid.NewGuid(), RandomMaterial(), handle);

        using var s3 = _fixture.NewRawClient();
        var versioned = await s3.GetObjectMetadataAsync(_fixture.BucketName, $"keys/{institutionCode}_v1_private.pem");
        versioned.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        // The flat-mirror object MUST NOT exist — Store only writes the
        // versioned object. Operators reading the bucket see exactly one
        // entry per (institution, version) pair.
        var act = () => s3.GetObjectMetadataAsync(_fixture.BucketName, $"keys/{institutionCode}_private.pem");
        await act.Should().ThrowAsync<Amazon.S3.AmazonS3Exception>(
            because: "Store must not write the flat-mirror object — the versioned object is the only artifact.");

        await _fixture.CleanupAsync(institutionCode, "_v1_private.pem");
    }

    [Fact]
    public async Task Rotation_writes_a_new_versioned_object_and_leaves_v1_untouched()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var institutionCode = "234567";
        var (handleV1, _) = ComposeHandle(institutionCode: institutionCode, keyVersion: 1);
        var (handleV2, _) = ComposeHandle(institutionCode: institutionCode, keyVersion: 2);

        var materialV1 = RandomMaterial();
        var materialV2 = RandomMaterial();
        store.Store(Guid.NewGuid(), materialV1, handleV1);
        store.Store(Guid.NewGuid(), materialV2, handleV2);

        // TryLoad on the v2 handle returns v2's bytes — versioned lookup is
        // direct off the (institutionCode, keyVersion) pair.
        store.TryLoad(handleV2).Should().NotBeNull().And.Equal(materialV2);

        // v1 blob survives — historical QRs verify against it.
        using var s3 = _fixture.NewRawClient();
        var v1Meta = await s3.GetObjectMetadataAsync(_fixture.BucketName, $"keys/{institutionCode}_v1_private.pem");
        v1Meta.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        var v2Meta = await s3.GetObjectMetadataAsync(_fixture.BucketName, $"keys/{institutionCode}_v2_private.pem");
        v2Meta.HttpStatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        await _fixture.CleanupAsync(institutionCode, "_v1_private.pem", "_v2_private.pem");
    }

    [Fact]
    public void Unknown_handle_returns_null()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var (handle, _) = ComposeHandle(institutionCode: "999999", keyVersion: 1);

        store.TryLoad(handle).Should().BeNull();
    }

    [Fact]
    public void Malformed_handle_throws_when_storing()
    {
        using var storage = _fixture.NewStorage();
        var store = new S3SigningKeyStore(storage, Kek());
        var badHandle = "tenant:abc:sbqr-signing:v1"; // missing institution code segment

        var act = () => store.Store(Guid.NewGuid(), RandomMaterial(), badHandle);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*does not match the expected*shape*");
    }

    private static (string Handle, string InstitutionCode) ComposeHandle(string institutionCode, int keyVersion)
    {
        // Mirror the production shape produced by CryptoKey.ComposeCustodyHandle
        // (kept private to the Domain layer). The S3 vault regex matches this
        // exactly; tests deliberately don't reach into Domain to avoid an extra
        // assembly reference here.
        var tenantId = Guid.NewGuid();
        var handle = $"tenant:{tenantId:D}:institution:{institutionCode}:sbqr-signing:v{keyVersion}";
        return (handle, institutionCode);
    }

    private static byte[] Kek()
    {
        // Same deterministic dev KEK S3KeyVaultProvider / LocalKeyVaultProvider
        // resolve when no KeyCustody:VaultKek is configured — see
        // LocalKeyVaultProvider.ResolveKek.
        return SHA256.HashData("sbqr-dev-vault-kek-v1"u8);
    }

    private static byte[] RandomMaterial(int length = 48)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }
}
