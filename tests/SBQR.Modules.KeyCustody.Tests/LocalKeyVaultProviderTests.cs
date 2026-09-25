using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using NSubstitute;
using SBQR.Modules.KeyCustody.Infrastructure.Vault;
using SBQR.SharedKernel.Cryptography;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// <see cref="LocalKeyVaultProvider"/> is the Phase-1 <see cref="IKeyVaultProvider"/>:
/// selects <c>EncryptedFileSigningKeyStore</c> as the <see cref="ISigningKeyStore"/>,
/// reports <see cref="IKeyVaultProvider.Metadata"/> without leaking any
/// secret, and the returned store survives Store/TryLoad round-trips.
/// </summary>
public sealed class LocalKeyVaultProviderTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "sbqr-vault-provider-" + Guid.NewGuid().ToString("N"));

    private static IConfiguration BuildConfig(string? kekBase64 = null, string? storeDir = null)
    {
        var config = Substitute.For<IConfiguration>();
        config["KeyCustody:VaultKek"].Returns(kekBase64);
        config["KeyCustody:KeyStoreDirectory"].Returns(storeDir);
        return config;
    }

    [Fact]
    public void Name_is_Local_and_Metadata_does_not_leak_the_KEK()
    {
        var kek = Convert.ToBase64String(SHA256.HashData([1, 2, 3]));
        var provider = new LocalKeyVaultProvider(BuildConfig(kek));

        provider.Name.Should().Be("Local");

        provider.Metadata.Should().ContainKey("backend").WhoseValue.Should().Be("encrypted-file");
        provider.Metadata.Should().ContainKey("cipher").WhoseValue.Should().Be("aes-256-gcm");
        provider.Metadata.Should().ContainKey("kek_source").WhoseValue.Should().Be("config:KeyCustody:VaultKek");

        // Defence-in-depth: the KEK value must not appear in any Metadata value.
        provider.Metadata.Values.Should().NotContain(v => v.Contains(kek, StringComparison.Ordinal));
    }

    [Fact]
    public void GetSigningKeyStore_returns_a_singleton()
    {
        var provider = new LocalKeyVaultProvider(BuildConfig());

        var first = provider.GetSigningKeyStore();
        var second = provider.GetSigningKeyStore();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void Returned_store_round_trips_a_sealed_blob()
    {
        var kek = Convert.ToBase64String(SHA256.HashData("sbqr-vault-provider-test-kek"u8));
        var provider = new LocalKeyVaultProvider(BuildConfig(kek, _directory));

        var store = provider.GetSigningKeyStore();
        var material = new byte[48];
        RandomNumberGenerator.Fill(material);
        var handle = "tenant:test:sbqr-signing:v1";

        store.Store(Guid.NewGuid(), material, handle);
        store.TryLoad(handle).Should().Equal(material);
    }

    [Fact]
    public void Provider_falls_back_to_deterministic_dev_kek_when_unset()
    {
        // No VaultKek → dev fallback. The store still functions (same shape
        // as the dev path that exists today via KeyCustodyModule.ResolveKek).
        var provider = new LocalKeyVaultProvider(BuildConfig(storeDir: _directory));

        var store = provider.GetSigningKeyStore();
        var handle = "tenant:dev:sbqr-signing:v1";
        var material = new byte[16] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16 };

        store.Store(Guid.NewGuid(), material, handle);
        store.TryLoad(handle).Should().Equal(material);
    }

    [Fact]
    public void Provider_throws_when_kek_is_not_32_bytes()
    {
        var badKek = Convert.ToBase64String(new byte[16]);

        var provider = new LocalKeyVaultProvider(BuildConfig(badKek));

        // Validation is deferred to first GetSigningKeyStore() call
        // (the Lazy<ISigningKeyStore> builder) — same shape as the existing
        // ResolveKek helper it replaced.
        var act = () => provider.GetSigningKeyStore();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly 32 bytes*");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
