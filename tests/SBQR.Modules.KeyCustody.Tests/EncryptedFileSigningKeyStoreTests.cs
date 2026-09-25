using System.Security.Cryptography;
using FluentAssertions;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// EncryptedFileSigningKeyStore behavior: round-trip, restart durability,
/// tamper/wrong-KEK fail-closed, and handle-binding (AAD) — the properties
/// the whole Phase-1 custody story rests on.
/// </summary>
public sealed class EncryptedFileSigningKeyStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sbqr-kats-" + Guid.NewGuid().ToString("N"));

    private static byte[] Kek(int seed) =>
        SHA256.HashData([(byte)seed, (byte)(seed >> 8), (byte)(seed >> 16)]);

    private static byte[] Material(int length = 48)
    {
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    [Fact]
    public void Stored_material_round_trips_through_the_same_store()
    {
        var store = new EncryptedFileSigningKeyStore(_directory, Kek(1));
        var material = Material();

        store.Store(Guid.NewGuid(), material, "tenant:1:sbqr-signing:v1");

        store.TryLoad("tenant:1:sbqr-signing:v1").Should().Equal(material);
    }

    [Fact]
    public void Material_survives_a_process_restart()
    {
        var handle = "tenant:2:sbqr-signing:v1";
        var material = Material();
        new EncryptedFileSigningKeyStore(_directory, Kek(1)).Store(Guid.NewGuid(), material, handle);

        // A fresh instance simulates a restart: nothing cached in-process.
        var reopened = new EncryptedFileSigningKeyStore(_directory, Kek(1));

        reopened.TryLoad(handle).Should().Equal(material);
    }

    [Fact]
    public void Unknown_handle_returns_null()
    {
        var store = new EncryptedFileSigningKeyStore(_directory, Kek(1));

        store.TryLoad("tenant:never:stored").Should().BeNull();
    }

    [Fact]
    public void Tampered_blob_fails_closed()
    {
        var handle = "tenant:3:sbqr-signing:v1";
        var store = new EncryptedFileSigningKeyStore(_directory, Kek(1));
        store.Store(Guid.NewGuid(), Material(), handle);

        var file = Directory.GetFiles(_directory, "*.key").Single();
        var blob = File.ReadAllBytes(file);
        blob[^1] ^= 0xFF; // flip the last tag byte
        File.WriteAllBytes(file, blob);

        var act = () => store.TryLoad(handle);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Wrong_kek_fails_closed()
    {
        var handle = "tenant:4:sbqr-signing:v1";
        new EncryptedFileSigningKeyStore(_directory, Kek(1)).Store(Guid.NewGuid(), Material(), handle);

        var wrongKekStore = new EncryptedFileSigningKeyStore(_directory, Kek(2));

        var act = () => wrongKekStore.TryLoad(handle);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_renamed_blob_cannot_be_loaded_under_a_different_handle()
    {
        var handleA = "tenant:5:sbqr-signing:v1";
        var handleB = "tenant:6:sbqr-signing:v1";
        var store = new EncryptedFileSigningKeyStore(_directory, Kek(1));
        store.Store(Guid.NewGuid(), Material(), handleA);

        // Move A's blob onto B's (hash-derived) filename — the GCM AAD binds
        // the blob to handle A, so loading it as B must fail.
        var fileA = Directory.GetFiles(_directory, "*.key").Single();
        var store2 = new EncryptedFileSigningKeyStore(_directory, Kek(1));
        store2.Store(Guid.NewGuid(), Material(), handleB);
        var fileB = Directory.GetFiles(_directory, "*.key").Single(f => f != fileA);
        File.Copy(fileA, fileB, overwrite: true);

        var act = () => new EncryptedFileSigningKeyStore(_directory, Kek(1)).TryLoad(handleB);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Non_32_byte_kek_is_rejected_at_construction()
    {
        var act = () => new EncryptedFileSigningKeyStore(_directory, new byte[16]);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Empty_material_is_rejected()
    {
        var store = new EncryptedFileSigningKeyStore(_directory, Kek(1));

        var act = () => store.Store(Guid.NewGuid(), ReadOnlySpan<byte>.Empty, "tenant:7:key");

        act.Should().Throw<ArgumentException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
