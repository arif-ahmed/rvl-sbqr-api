namespace SBQR.SharedKernel.Storage;

/// <summary>
/// Creates <see cref="IObjectStorage"/> instances scoped to a folder inside
/// one shared physical bucket/container. Register the concrete factory
/// (<c>SBQR.SharedKernel.Storage.S3.S3ObjectStorageFactory</c>) once at the
/// host composition root; any module then resolves
/// <see cref="IObjectStorageFactory"/> from DI and calls <see cref="Create"/>
/// with its own folder name to get an isolated <see cref="IObjectStorage"/>
/// — no module needs to know the backend is S3, parse <c>Storage:*</c>
/// config itself, or manage a client/connection lifetime.
/// </summary>
public interface IObjectStorageFactory
{
    /// <summary>
    /// Build an <see cref="IObjectStorage"/> namespaced under
    /// <paramref name="vaultFolder"/> — every key the returned instance
    /// touches is prefixed with it, so callers in different modules never
    /// collide even though they share one bucket. Pass an empty string for
    /// an unprefixed (bucket-root) view.
    /// </summary>
    IObjectStorage Create(string vaultFolder);
}
