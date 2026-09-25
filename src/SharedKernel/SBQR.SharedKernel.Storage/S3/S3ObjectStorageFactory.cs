using Amazon.S3;
using Microsoft.Extensions.Configuration;

namespace SBQR.SharedKernel.Storage.S3;

/// <summary>
/// <see cref="IObjectStorageFactory"/> for S3. Builds one
/// <see cref="AmazonS3Client"/> from the <c>Storage:*</c> config block
/// (endpoint, credentials, region, bucket, <c>Storage:ForcePathStyle</c>) —
/// shared by every <see cref="IObjectStorage"/> this factory hands out, so
/// N modules asking for N folders still open exactly one client/connection.
///
/// <para>
/// Register this once at the host composition root as a singleton
/// <see cref="IObjectStorageFactory"/>; any module then resolves that
/// interface from DI and calls <see cref="Create"/> with its own folder
/// name — it never needs to know the backend is S3 or touch
/// <c>Storage:*</c> config itself.
/// </para>
/// </summary>
public sealed class S3ObjectStorageFactory : IObjectStorageFactory, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly string _bucket;

    public S3ObjectStorageFactory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var region = configuration["Storage:Region"]
            ?? throw new InvalidOperationException("Missing Storage:Region.");
        _bucket = configuration["Storage:BucketName"]
            ?? throw new InvalidOperationException("Missing Storage:BucketName.");
        _s3 = S3ObjectStorage.BuildClient(configuration, region);
    }

    /// <inheritdoc/>
    public IObjectStorage Create(string vaultFolder) => new S3ObjectStorage(_s3, _bucket, vaultFolder);

    /// <summary>Disposes the one shared <see cref="AmazonS3Client"/> — call at host shutdown (DI container does this automatically for a singleton).</summary>
    public void Dispose() => _s3.Dispose();
}
