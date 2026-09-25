namespace SBQR.SharedKernel.Storage;

/// <summary>
/// Backend-agnostic object-storage port. One implementation per physical
/// backend — <c>SBQR.SharedKernel.Storage.S3.S3ObjectStorage</c> today,
/// future Azure Blob / GCS backends alongside it. Any module may depend on
/// this interface to put/get/list opaque byte blobs without taking on a
/// vendor SDK dependency itself; only the module's composition root (e.g.
/// KeyCustody's <c>S3KeyVaultProvider</c>) ever references the concrete
/// backend type.
///
/// <para>
/// KeyCustody's <c>S3SigningKeyStore</c> is the first consumer: it stores
/// AES-256-GCM-sealed private-key blobs under an operator-readable key
/// layout. The wire format (magic + nonce + ciphertext + tag) belongs to
/// the cryptographic boundary in KeyCustody.Infrastructure/Custody; this
/// port only ever sees opaque byte blobs, never key material shape.
/// </para>
/// </summary>
public interface IObjectStorage
{
    /// <summary>Write <paramref name="content"/> under <paramref name="key"/>, replacing any existing object.</summary>
    Task PutAsync(string key, Stream content, string contentType, SseMode sse, CancellationToken cancellationToken = default);

    /// <summary>
    /// Open the object at <paramref name="key"/> for reading. Throws
    /// <see cref="ObjectStorageKeyNotFoundException"/> when the key does not
    /// exist (the cryptographic boundary turns that into <c>null</c> per the
    /// <c>ISigningKeyStore.TryLoad</c> contract).
    /// </summary>
    Task<Stream> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Cheap existence probe (no body download).</summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Idempotent delete — no error if the key is missing.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>List every object whose key starts with <paramref name="prefix"/>, in lexicographic order.</summary>
    Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken cancellationToken = default);
}

/// <summary>Server-side encryption mode applied to a Put request.</summary>
public enum SseMode
{
    /// <summary>No server-side encryption. Only acceptable for public / non-sensitive data.</summary>
    None,

    /// <summary>SSE-S3 (AES-256, provider-managed keys). The default for tenant signing-key blobs.</summary>
    Aes256,
}

/// <summary>
/// Thrown when an object key is not present. The cryptographic boundary
/// maps this to a <c>null</c> from <c>ISigningKeyStore.TryLoad</c> so the
/// "key not yet minted" and "key mint in progress" states stay
/// indistinguishable from "wrong bucket, wrong handle".
/// </summary>
public sealed class ObjectStorageKeyNotFoundException : Exception
{
    public ObjectStorageKeyNotFoundException(string key)
        : base($"Object storage key '{key}' was not found.")
    {
        Key = key;
    }

    public string Key { get; }
}

/// <summary>Wraps any non-NotFound backend failure (network, auth, IO) so the cryptographic boundary can fail closed.</summary>
public sealed class ObjectStorageException : Exception
{
    public ObjectStorageException(string message, Exception? inner = null)
        : base(message, inner) { }
}
