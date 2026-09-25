using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.Extensions.Configuration;
using SBQR.SharedKernel.Storage.S3;
using Xunit;

namespace SBQR.Modules.KeyCustody.Tests.Infrastructure;

/// <summary>
/// xUnit <see cref="IAsyncLifetime"/> fixture that owns a single ephemeral
/// LocalStack (S3-only) container for the whole "S3" test collection.
///
/// <para>
/// The real dev/prod bucket (see docs/dev-s3-guide.md) is never touched by
/// automated tests — this fixture is the only S3 endpoint the test suite
/// talks to, so tests stay hermetic, free, and safe to run offline.
/// </para>
/// </summary>
public sealed class LocalStackS3Fixture : IAsyncLifetime
{
    private const string Bucket = "sbqr-test";
    private const string AccessKey = "test";
    private const string SecretKey = "test";
    private const string Region = "us-east-1";

    private readonly IContainer _container = new ContainerBuilder("localstack/localstack:3")
        .WithEnvironment("SERVICES", "s3")
        .WithPortBinding(4566, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(r => r.ForPath("/_localstack/health").ForPort(4566)))
        .Build();

    private string _serviceUrl = string.Empty;

    /// <inheritdoc/>
    public async Task InitializeAsync()
    {
        await _container.StartAsync().ConfigureAwait(false);
        _serviceUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(4566)}";

        using var s3 = NewRawClient();
        await s3.PutBucketAsync(new PutBucketRequest { BucketName = Bucket }).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _container.DisposeAsync().ConfigureAwait(false);

    /// <summary>New <see cref="S3ObjectStorage"/> pointed at the ephemeral container, VaultFolder-free.</summary>
    public S3ObjectStorage NewStorage()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ServiceUrl"] = _serviceUrl,
                ["Storage:AccessKeyId"] = AccessKey,
                ["Storage:SecretAccessKey"] = SecretKey,
                ["Storage:Region"] = Region,
                ["Storage:BucketName"] = Bucket,
                ["Storage:ForcePathStyle"] = "true",
            })
            .Build();
        return new S3ObjectStorage(config);
    }

    /// <summary>
    /// New <see cref="S3ObjectStorageFactory"/> pointed at the ephemeral
    /// container — proves the same reuse contract any module gets from the
    /// host-registered factory (one client, many folder-scoped stores).
    /// </summary>
    public S3ObjectStorageFactory NewFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:ServiceUrl"] = _serviceUrl,
                ["Storage:AccessKeyId"] = AccessKey,
                ["Storage:SecretAccessKey"] = SecretKey,
                ["Storage:Region"] = Region,
                ["Storage:BucketName"] = Bucket,
                ["Storage:ForcePathStyle"] = "true",
            })
            .Build();
        return new S3ObjectStorageFactory(config);
    }

    /// <summary>Raw SDK client for test-side assertions/tampering the port doesn't expose.</summary>
    public AmazonS3Client NewRawClient() => new(
        new BasicAWSCredentials(AccessKey, SecretKey),
        new AmazonS3Config
        {
            ServiceURL = _serviceUrl,
            ForcePathStyle = true,
            AuthenticationRegion = Region,
        });

    public async Task CleanupAsync(string institutionCode, params string[] versionSuffixes)
    {
        using var s3 = NewRawClient();
        foreach (var suffix in versionSuffixes)
        {
            try
            {
                await s3.DeleteObjectAsync(Bucket, $"keys/{institutionCode}{suffix}").ConfigureAwait(false);
            }
            catch
            {
                // best-effort cleanup; the test already passed or failed independently
            }
        }
    }

    // Instance property (not static) so `_fixture.BucketName` reads naturally
    // alongside the fixture's other instance members, even though the value
    // itself is a compile-time constant today.
#pragma warning disable CA1822
    public string BucketName => Bucket;
#pragma warning restore CA1822
}

/// <summary>Shares one <see cref="LocalStackS3Fixture"/> container across every test class in the "S3" collection.</summary>
[CollectionDefinition("S3")]
public sealed class S3TestCollection : ICollectionFixture<LocalStackS3Fixture>
{
}
