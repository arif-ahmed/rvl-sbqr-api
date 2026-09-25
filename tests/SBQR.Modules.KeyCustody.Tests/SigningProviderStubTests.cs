using FluentAssertions;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.SharedKernel.Cryptography;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Smoke tests for the KeyCustody placeholder. Real key-lifecycle,
/// sign-payload handler, and NSec↔BouncyCastle cross-impl tests land
/// in epic-5 (KeyCustody stories).
/// </summary>
public sealed class SigningProviderStubTests
{
    [Fact]
    public void PlainFileSigningProvider_should_have_dev_only_provider_id()
    {
        var provider = new PlainFileSigningProvider();

        provider.ProviderId.Should().StartWith("plain-file", "this provider is dev-only and hard-blocked in Production.");
    }

    [Fact]
    public void PlainFileSigningProvider_SignAsync_should_throw_NotImplemented()
    {
        var provider = new PlainFileSigningProvider();
        var payload = new byte[] { 0x01, 0x02, 0x03 };

        var act = () => provider.SignAsync(payload);

        act.Should().ThrowAsync<NotImplementedException>();
    }
}
