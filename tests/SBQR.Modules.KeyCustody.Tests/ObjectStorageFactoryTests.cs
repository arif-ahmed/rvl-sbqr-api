using System.Text;
using FluentAssertions;
using SBQR.Modules.KeyCustody.Tests.Infrastructure;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests;

/// <summary>
/// Proves the reuse contract behind <c>IObjectStorageFactory</c>: one
/// factory, backed by one shared S3 client, hands out multiple
/// folder-scoped <c>IObjectStorage</c> views that never see each other's
/// objects — the mechanism any module (not just KeyCustody) relies on when
/// it resolves the host-registered factory from DI and calls
/// <c>Create("&lt;own-folder&gt;")</c>.
/// </summary>
[Collection("S3")]
public sealed class ObjectStorageFactoryTests
{
    private readonly LocalStackS3Fixture _fixture;

    public ObjectStorageFactoryTests(LocalStackS3Fixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Two_folders_from_the_same_factory_do_not_collide()
    {
        using var factory = _fixture.NewFactory();
        var keyCustodyView = factory.Create("keycustody");
        var otherModuleView = factory.Create("some-other-module");

        var keyCustodyBytes = Encoding.UTF8.GetBytes("keycustody-payload");
        var otherModuleBytes = Encoding.UTF8.GetBytes("other-module-payload");

        await keyCustodyView.PutAsync("shared-key-name", new MemoryStream(keyCustodyBytes), "application/octet-stream", SBQR.SharedKernel.Storage.SseMode.None);
        await otherModuleView.PutAsync("shared-key-name", new MemoryStream(otherModuleBytes), "application/octet-stream", SBQR.SharedKernel.Storage.SseMode.None);

        await using var keyCustodyStream = await keyCustodyView.GetAsync("shared-key-name");
        using var keyCustodyMs = new MemoryStream();
        await keyCustodyStream.CopyToAsync(keyCustodyMs);
        keyCustodyMs.ToArray().Should().Equal(keyCustodyBytes,
            because: "each folder-scoped view must only ever see its own module's objects, even for an identical logical key name");

        await using var otherModuleStream = await otherModuleView.GetAsync("shared-key-name");
        using var otherModuleMs = new MemoryStream();
        await otherModuleStream.CopyToAsync(otherModuleMs);
        otherModuleMs.ToArray().Should().Equal(otherModuleBytes);

        await keyCustodyView.DeleteAsync("shared-key-name");
        await otherModuleView.DeleteAsync("shared-key-name");
    }

    [Fact]
    public async Task ListAsync_only_returns_keys_within_the_requesting_folder()
    {
        using var factory = _fixture.NewFactory();
        var storageA = factory.Create("module-a");
        var storageB = factory.Create("module-b");

        await storageA.PutAsync("items/one", new MemoryStream("a1"u8.ToArray()), "application/octet-stream", SBQR.SharedKernel.Storage.SseMode.None);
        await storageB.PutAsync("items/one", new MemoryStream("b1"u8.ToArray()), "application/octet-stream", SBQR.SharedKernel.Storage.SseMode.None);

        var listedFromA = await storageA.ListAsync("items/");
        listedFromA.Should().ContainSingle().Which.Should().Be("items/one");

        await storageA.DeleteAsync("items/one");
        await storageB.DeleteAsync("items/one");
    }
}
