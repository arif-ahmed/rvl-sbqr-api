using FluentAssertions;
using SBQR.Modules.KeyCustody.Domain.Aggregates;
using SBQR.Modules.KeyCustody.Domain.Events;
using SBQR.SharedKernel.Cryptography;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Pure-domain unit tests for <see cref="CryptoKey.Suspend"/>,
/// <see cref="CryptoKey.Reinstate"/>, and <see cref="CryptoKey.Retire"/> —
/// the lifecycle state machine now fully owned by KeyCustody.
/// </summary>
public sealed class CryptoKeyLifecycleTests
{
    /// <summary>Test double for <see cref="ISigningKeyStore"/> — no real vault I/O.</summary>
    private sealed class InMemoryVault : ISigningKeyStore
    {
        public string Store(Guid tenantId, ReadOnlySpan<byte> privateKeyBytes, string suggestedHandle)
        {
            _ = tenantId;
            _ = privateKeyBytes;
            return suggestedHandle;
        }

        public byte[]? TryLoad(string custodyHandle) => null;
    }

    private const string PublicKeyPem =
        "-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEAGb9ECWmEzf6FQbrBZ9w7lshQ7c1bD8=\n-----END PUBLIC KEY-----";

    private static readonly byte[] PrivateKeyBytes =
    {
        0x30, 0x2E, 0x02, 0x01, 0x00, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70,
        0x04, 0x22, 0x04, 0x20,
    };

    private static CryptoKey CreateActiveKey(Guid? tenantId = null, int keyVersion = 1) =>
        CryptoKey.Generate(
            tenantId: tenantId ?? Guid.NewGuid(),
            keyId: CryptoKey.DefaultKeyId,
            institutionCode: "010101",
            publicKeyPem: PublicKeyPem,
            privateKeyBytes: PrivateKeyBytes,
            vault: new InMemoryVault(),
            keyVersion: keyVersion);

    [Fact]
    public void Generate_should_default_to_version_1_and_Active()
    {
        var key = CreateActiveKey();

        key.KeyVersion.Should().Be(1);
        key.Status.Should().Be(CryptoKeyStatus.Active);
        key.DomainEvents.OfType<CertificateGenerated>().Should().ContainSingle();
    }

    [Fact]
    public void Generate_should_accept_an_explicit_version_for_rotation()
    {
        var key = CreateActiveKey(keyVersion: 2);

        key.KeyVersion.Should().Be(2);
    }

    [Fact]
    public void Suspend_should_flip_active_key_to_suspended_and_raise_event()
    {
        var key = CreateActiveKey();

        key.Suspend();

        key.Status.Should().Be(CryptoKeyStatus.Suspended);
        var ev = key.DomainEvents.OfType<KeySuspended>().Single();
        ev.CryptoKeyId.Should().Be(key.Id);
        ev.TenantId.Should().Be(key.TenantId);
    }

    [Fact]
    public void Suspend_on_already_suspended_key_must_not_be_idempotent()
    {
        var key = CreateActiveKey();
        key.Suspend();

        var act = () => key.Suspend();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Suspended*");
    }

    [Fact]
    public void Reinstate_should_flip_suspended_key_back_to_active_and_raise_event()
    {
        var key = CreateActiveKey();
        key.Suspend();
        key.ClearDomainEvents();

        key.Reinstate();

        key.Status.Should().Be(CryptoKeyStatus.Active);
        var ev = key.DomainEvents.OfType<KeyReinstated>().Single();
        ev.CryptoKeyId.Should().Be(key.Id);
        ev.TenantId.Should().Be(key.TenantId);
    }

    [Fact]
    public void Reinstate_on_already_active_key_must_not_be_idempotent()
    {
        var key = CreateActiveKey();

        var act = () => key.Reinstate();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Active*");
    }

    [Fact]
    public void Retire_should_flip_active_key_to_retired_and_raise_event()
    {
        var key = CreateActiveKey();

        key.Retire();

        key.Status.Should().Be(CryptoKeyStatus.Retired);
        var ev = key.DomainEvents.OfType<KeyRetired>().Single();
        ev.CryptoKeyId.Should().Be(key.Id);
        ev.TenantId.Should().Be(key.TenantId);
    }

    [Fact]
    public void Retire_should_also_accept_a_suspended_key()
    {
        var key = CreateActiveKey();
        key.Suspend();
        key.ClearDomainEvents();

        key.Retire();

        key.Status.Should().Be(CryptoKeyStatus.Retired);
    }

    [Fact]
    public void Retire_on_already_retired_key_must_not_be_idempotent()
    {
        var key = CreateActiveKey();
        key.Retire();

        var act = () => key.Retire();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Retired*");
    }
}
