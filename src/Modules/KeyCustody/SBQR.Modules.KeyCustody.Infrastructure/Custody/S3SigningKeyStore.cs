using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.Storage;

namespace SBQR.Modules.KeyCustody.Infrastructure.Custody;

/// <summary>
/// S3-backed variant of <see cref="EncryptedFileSigningKeyStore"/>: same
/// AES-256-GCM wire format (magic header + nonce + ciphertext + tag), same
/// KEK, same handle binding (GCM AAD), same fail-closed semantics. Only
/// the byte source and the operator-readable object-key layout change.
///
/// <para>
/// <b>Object-key layout.</b> The Domain encodes the institution code and
/// the key version into the custody handle (see
/// <c>CryptoKey.ComposeCustodyHandle</c>) so this store can compose keys
/// that operators can read with <c>aws s3 ls</c> / <c>mc ls</c>:
/// <code>
/// keys/&lt;institutionCode&gt;_v&lt;n&gt;_private.pem
/// </code>
/// Every <see cref="Store"/> writes exactly one object, named after the
/// version encoded in the custody handle. <see cref="TryLoad"/> reads the
/// same object back. Rotations produce a new <c>v&lt;n+1&gt;</c> blob and
/// the prior version survives untouched — historical QRs verify against
/// the version they were signed with, satisfying the retire/RETAIN
/// invariant in <c>database-design.md §1.3</c> and <c>CryptoKey.Retire()</c>.
/// Adopt mode (institute-supplied keypair) writes to the SAME object key
/// as Generate mode: an Adopt at v1 overwrites any prior Generate v1 blob
/// in place, which is the intended behaviour — the institute's private
/// key is replacing whatever the server had. Distinction between Generate
/// and Adopt is recorded in the <c>crypto_keys.mode</c> column and in the
/// audit trail (<c>crypto_key.minted</c> vs <c>crypto_key.adopted</c>),
/// not in the on-disk object name.
/// </para>
///
/// <para>
/// Lives in <c>SBQR.Modules.KeyCustody.Infrastructure.Custody</c> to
/// satisfy the architecture test
/// <c>Signing_types_must_only_live_in_KeyCustody_Infrastructure_Custody</c>.
/// </para>
///
/// <para>
/// <b>Memory hygiene.</b> The decrypted <c>plaintext</c> buffer is wiped
/// with <see cref="Array.Clear"/> on both the success path (before the
/// method returns — callers receive a copy) and the failure path (GCM
/// tag mismatch throws and zero-out runs in the same <c>finally</c>).
/// The GCM AAD still binds the ciphertext to its handle, so a blob moved
/// under another handle's path fails authentication.
/// </para>
/// </summary>
public sealed class S3SigningKeyStore : ISigningKeyStore
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int HeaderSize = 8 /* magic */ + NonceSizeBytes;
    private static readonly byte[] Magic = "SBQRKEY1"u8.ToArray();
    private const string KeyPrefix = "keys/";
    private const string VersionedSuffix = "_v";
    private const string PrivateSuffix = "_private.pem";
    private const string ContentType = "application/octet-stream";

    // Handle shape produced by CryptoKey.ComposeCustodyHandle:
    //   tenant:<guid>:institution:<6digit>:<keyId>:v<n>
    // Adopt and Generate produce the SAME handle shape and the SAME S3
    // object key — Adopt overwrites Generate in place at the same
    // (institution, version). The mode distinction lives in the
    // crypto_keys row and the audit trail, not on disk.
    // We capture the institution code (group 1) and the version (group 2)
    // to compose the S3 object key without a runtime cross-module lookup.
    private static readonly Regex HandlePattern = new(
        pattern: @"^tenant:[0-9a-fA-F\-]+:institution:(\d{6}):[^:]+:v(\d+)$",
        options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IObjectStorage _storage;
    private readonly byte[] _kek;

    /// <param name="storage">Object-storage backend (the LocalStack S3 adapter in dev).</param>
    /// <param name="kek">Exactly 32 bytes (AES-256). Operator-supplied vault KEK.</param>
    public S3SigningKeyStore(IObjectStorage storage, ReadOnlySpan<byte> kek)
    {
        ArgumentNullException.ThrowIfNull(storage);
        if (kek.Length != 32)
        {
            throw new CryptographicException(
                $"Vault KEK must be exactly 32 bytes (AES-256); got {kek.Length}. " +
                "Set KeyCustody:VaultKek (base64) or run in Development for the deterministic dev key.");
        }

        _storage = storage;
        _kek = kek.ToArray();
    }

    /// <inheritdoc/>
    public string Store(Guid tenantId, ReadOnlySpan<byte> privateKeyBytes, string suggestedHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedHandle);
        if (privateKeyBytes.IsEmpty)
        {
            throw new ArgumentException("privateKeyBytes must be non-empty.", nameof(privateKeyBytes));
        }

        var (institutionCode, keyVersion) = ParseHandle(suggestedHandle);

        var handleBytes = Encoding.UTF8.GetBytes(suggestedHandle);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[privateKeyBytes.Length];
        var tag = new byte[TagSizeBytes];

        using (var aes = new AesGcm(_kek, TagSizeBytes))
        {
            aes.Encrypt(nonce, privateKeyBytes, ciphertext, tag, handleBytes);
        }

        var output = new byte[HeaderSize + ciphertext.Length + TagSizeBytes];
        Magic.CopyTo(output, 0);
        nonce.CopyTo(output, Magic.Length);
        ciphertext.CopyTo(output, HeaderSize);
        tag.CopyTo(output, HeaderSize + ciphertext.Length);

        try
        {
            // Single PUT: one versioned object per key version. The handle
            // already encodes (institutionCode, keyVersion), so the active
            // key and every historical key each get their own immutable
            // object. Rotations simply produce a new _v<n+1>_private.pem
            // while leaving the prior blob untouched.
            //
            // Sync-over-async on purpose: ISigningKeyStore.Store is the sync
            // contract the CryptoKey aggregate factory already calls. The
            // local emulator returns within milliseconds.
            using var stream = new MemoryStream(output, writable: false);
            var versionedKey = VersionedObjectKey(institutionCode, keyVersion);
            _storage
                .PutAsync(versionedKey, stream, ContentType, SseMode.Aes256)
                .GetAwaiter()
                .GetResult();
        }
        finally
        {
            Array.Clear(ciphertext);
            Array.Clear(output);
        }

        return suggestedHandle;
    }

    /// <inheritdoc/>
    public byte[]? TryLoad(string custodyHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(custodyHandle);

        var (institutionCode, keyVersion) = ParseHandle(custodyHandle);
        var objectKey = VersionedObjectKey(institutionCode, keyVersion);

        Stream stream;
        try
        {
            // Read the versioned object keyed off the (institutionCode,
            // keyVersion) pair encoded in the handle. The active key and
            // every historical key live at their own immutable _v<n> key;
            // signing only ever resolves the row's specific (tenant_id,
            // key_version), so this is a direct lookup — no mirror needed.
            stream = _storage.GetAsync(objectKey).GetAwaiter().GetResult();
        }
        catch (ObjectStorageKeyNotFoundException)
        {
            // Per ISigningKeyStore contract: not-found == null, NOT an exception.
            return null;
        }

        using (stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var blob = ms.ToArray();

            if (blob.Length < HeaderSize + TagSizeBytes
                || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            {
                throw new CryptographicException(
                    $"Sealed-key blob for handle '{custodyHandle}' is not a recognizable SBQR key file.");
            }

            var nonce = blob.AsSpan(Magic.Length, NonceSizeBytes);
            var tag = blob.AsSpan(blob.Length - TagSizeBytes, TagSizeBytes);
            var ciphertext = blob.AsSpan(HeaderSize, blob.Length - HeaderSize - TagSizeBytes);
            var plaintext = new byte[ciphertext.Length];

            try
            {
                using var aes = new AesGcm(_kek, TagSizeBytes);
                // Throws CryptographicException on tag mismatch: wrong KEK,
                // tampered blob, or a handle/object swap. Plaintext is
                // zeroed in the catch arm so the failed-secret never
                // lingers on the GC heap.
                aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(custodyHandle));
                return plaintext;
            }
            catch
            {
                Array.Clear(plaintext);
                throw;
            }
        }
    }

    /// <summary>
    /// Parse the institution code and key version out of the handle
    /// composed by <c>CryptoKey.ComposeCustodyHandle</c>. Throws on a
    /// malformed handle so the S3 vault fails closed (never silently
    /// produces a wrong object key).
    /// </summary>
    private static (string InstitutionCode, int KeyVersion) ParseHandle(string handle)
    {
        var match = HandlePattern.Match(handle);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"S3SigningKeyStore received a custody handle that does not match the expected " +
                $"tenant:<guid>:institution:<6digit>:<keyId>:v<n> shape: '{handle}'.");
        }

        return (
            match.Groups[1].Value,
            int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// <c>keys/&lt;institutionCode&gt;_v&lt;n&gt;_private.pem</c> — the
    /// only artifact this vault ever writes or returns. The version token
    /// is part of the object name so the active key and every historical
    /// key each live at their own immutable S3 object. Operators reading
    /// <c>mc ls localstack/sbqr-dev/keys/</c> see one entry per
    /// (institution, version) pair, and <c>aws s3 ls</c> is a complete
    /// audit trail of every key that has ever existed for that
    /// institution. Adopt and Generate share this object key — Adopt
    /// overwrites Generate in place at the same (institution, version).
    /// </summary>
    private static string VersionedObjectKey(string institutionCode, int keyVersion) =>
        $"{KeyPrefix}{institutionCode}{VersionedSuffix}{keyVersion}{PrivateSuffix}";
}
