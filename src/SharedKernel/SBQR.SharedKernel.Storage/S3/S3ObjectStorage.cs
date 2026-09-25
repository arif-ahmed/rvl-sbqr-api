using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace SBQR.SharedKernel.Storage.S3;

/// <summary>
/// <see cref="IObjectStorage"/> backed by a single <see cref="AmazonS3Client"/>.
/// Reads the <c>Storage:*</c> config block at construction: endpoint,
/// credentials, region, bucket, and a <c>Storage:VaultFolder</c> key prefix
/// that namespaces every object this instance touches (see <see cref="Compose"/>).
///
/// <para>
/// <b>Real AWS vs. emulator.</b> Leave <c>Storage:ServiceUrl</c> blank to hit
/// real AWS S3 endpoints (region-resolved, virtual-hosted addressing). Set
/// it (and <c>Storage:ForcePathStyle=true</c>) to point at an S3-compatible
/// emulator such as LocalStack instead — same adapter, only config differs.
/// Leave <c>Storage:AccessKeyId</c> / <c>SecretAccessKey</c> blank to fall
/// back to the AWS SDK's default credential resolution (env vars, shared
/// profile, SSO, instance role) instead of static keys.
/// </para>
///
/// <para>
/// <b>VaultFolder.</b> Every key passed to this instance is namespaced under
/// a folder prefix before it reaches S3, and stripped back off on
/// <see cref="ListAsync"/> results — callers only ever see logical keys.
/// This lets multiple modules (or multiple developers sharing one bucket)
/// use the same bucket without colliding. Get a folder-scoped instance from
/// <see cref="S3ObjectStorageFactory"/> (one shared client, many folders)
/// rather than constructing this type directly, unless you genuinely want
/// your own dedicated client (e.g. a test fixture pointed at an emulator).
/// </para>
/// </summary>
public sealed class S3ObjectStorage : IObjectStorage, IDisposable
{
    private readonly AmazonS3Client _s3;
    private readonly bool _ownsClient;
    private readonly string _bucket;
    private readonly string _vaultFolder;

    /// <summary>
    /// Stand-alone constructor: builds and owns its own <see cref="AmazonS3Client"/>
    /// from the <c>Storage:*</c> config block (endpoint, credentials, region,
    /// bucket, <c>Storage:VaultFolder</c>, <c>Storage:ForcePathStyle</c>).
    /// Prefer <see cref="S3ObjectStorageFactory"/> when multiple
    /// folder-scoped instances are needed — it shares one client instead of
    /// opening one per instance.
    /// </summary>
    public S3ObjectStorage(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var region = configuration["Storage:Region"]
            ?? throw new InvalidOperationException("Missing Storage:Region.");
        _bucket = configuration["Storage:BucketName"]
            ?? throw new InvalidOperationException("Missing Storage:BucketName.");
        _vaultFolder = NormalizeFolder(configuration["Storage:VaultFolder"]);

        _s3 = BuildClient(configuration, region);
        _ownsClient = true;
    }

    /// <summary>
    /// Factory-path constructor: wraps a client the caller owns (and
    /// disposes) under a specific bucket/folder. See
    /// <see cref="S3ObjectStorageFactory.Create"/>.
    /// </summary>
    internal S3ObjectStorage(AmazonS3Client s3, string bucket, string vaultFolder)
    {
        _s3 = s3;
        _bucket = bucket;
        _vaultFolder = NormalizeFolder(vaultFolder);
        _ownsClient = false;
    }

    internal static AmazonS3Client BuildClient(IConfiguration configuration, string region)
    {
        var serviceUrl = configuration["Storage:ServiceUrl"];
        var accessKey = configuration["Storage:AccessKeyId"];
        var secretKey = configuration["Storage:SecretAccessKey"];
        var forcePathStyle = bool.TryParse(configuration["Storage:ForcePathStyle"], out var parsed) && parsed;

        var config = new AmazonS3Config { AuthenticationRegion = region };
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            // Emulator mode (e.g. LocalStack): the endpoint is explicit and
            // path-style addressing is usually required.
            config.ServiceURL = serviceUrl;
            config.ForcePathStyle = forcePathStyle;
        }
        else
        {
            // Real AWS: resolve the region's default endpoint, virtual-hosted addressing.
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
        }

        return string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
    }

    /// <summary>
    /// Disposes the underlying <see cref="AmazonS3Client"/> only when this
    /// instance built it itself (the stand-alone constructor). A
    /// factory-vended instance shares the factory's client — the factory
    /// owns that client's lifetime, not the individual folder-scoped views.
    /// </summary>
    public void Dispose()
    {
        if (_ownsClient)
        {
            _s3.Dispose();
        }
    }

    /// <inheritdoc/>
    public async Task PutAsync(string key, Stream content, string contentType, SseMode sse, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(content);

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = Compose(key),
            InputStream = content,
            ContentType = contentType,
            ServerSideEncryptionMethod = sse switch
            {
                SseMode.Aes256 => ServerSideEncryptionMethod.AES256,
                SseMode.None   => ServerSideEncryptionMethod.None,
                _ => throw new ArgumentOutOfRangeException(nameof(sse), sse, "Unknown SseMode."),
            },
        };

        try
        {
            await _s3.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"S3 PutObject '{key}' failed ({ex.ErrorCode}): {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            var response = await _s3.GetObjectAsync(_bucket, Compose(key), cancellationToken).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                                        || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            throw new ObjectStorageKeyNotFoundException(key);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"S3 GetObject '{key}' failed ({ex.ErrorCode}): {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _s3.GetObjectMetadataAsync(_bucket, Compose(key), cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound
                                        || string.Equals(ex.ErrorCode, "NotFound", StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(ex.ErrorCode, "NoSuchKey", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"S3 GetObjectMetadata '{key}' failed ({ex.ErrorCode}): {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        try
        {
            await _s3.DeleteObjectAsync(_bucket, Compose(key), cancellationToken).ConfigureAwait(false);
        }
        catch (AmazonS3Exception ex)
        {
            throw new ObjectStorageException(
                $"S3 DeleteObject '{key}' failed ({ex.ErrorCode}): {ex.Message}", ex);
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default)
    {
        var keys = new List<string>();
        string? continuationToken = null;
        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucket,
                Prefix = Compose(prefix ?? string.Empty),
                ContinuationToken = continuationToken,
            };
            ListObjectsV2Response response;
            try
            {
                response = await _s3.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);
            }
            catch (AmazonS3Exception ex)
            {
                throw new ObjectStorageException(
                    $"S3 ListObjectsV2 (prefix '{prefix}') failed ({ex.ErrorCode}): {ex.Message}", ex);
            }
            foreach (var obj in response.S3Objects)
            {
                keys.Add(Decompose(obj.Key));
            }
            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);

        return keys;
    }

    /// <summary>Prefix a logical key with the configured vault folder, if any.</summary>
    private string Compose(string key) => _vaultFolder.Length == 0 ? key : $"{_vaultFolder}/{key}";

    /// <summary>Strip the configured vault folder back off an S3 object key.</summary>
    private string Decompose(string objectKey) =>
        _vaultFolder.Length > 0 && objectKey.StartsWith(_vaultFolder + "/", StringComparison.Ordinal)
            ? objectKey[(_vaultFolder.Length + 1)..]
            : objectKey;

    private static string NormalizeFolder(string? folder) =>
        string.IsNullOrWhiteSpace(folder) ? string.Empty : folder.Trim('/');
}
