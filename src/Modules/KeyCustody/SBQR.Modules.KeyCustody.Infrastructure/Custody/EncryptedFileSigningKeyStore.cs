using System.Security.Cryptography;
using System.Text;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Infrastructure.Custody;

/// <summary>
/// Phase-1 software vault: per-handle files under a configurable directory,
/// each blob AES-256-GCM-sealed with an operator-supplied 32-byte KEK
/// (<c>KeyCustody:VaultKek</c>, supplied out-of-band — never in a database).
/// The handle string is the GCM associated data, so a blob is
/// cryptographically bound to its custody handle: renaming or swapping files
/// between handles fails authentication.
///
/// File layout: <c>"SBQRKEY1"</c> magic (8) ‖ nonce (12) ‖ ciphertext+tag.
/// The filename is the hex SHA-256 of the handle (handles contain ':' and
/// must not be used as filenames verbatim). Writes are temp-file + atomic
/// move so a crash never leaves a partial blob.
///
/// Plaintext exists only inside <see cref="Store"/> and <see cref="TryLoad"/>
/// call frames — never on disk, never in any database.
/// </summary>
public sealed class EncryptedFileSigningKeyStore : ISigningKeyStore
{
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;
    private const int HeaderSize = 8 /* magic */ + NonceSizeBytes;
    private static readonly byte[] Magic = "SBQRKEY1"u8.ToArray();

    private readonly string _directory;
    private readonly byte[] _kek;

    /// <param name="directory">Directory for sealed blobs; created on first write.</param>
    /// <param name="kek">Exactly 32 bytes (AES-256). The operator-supplied vault KEK.</param>
    public EncryptedFileSigningKeyStore(string directory, ReadOnlySpan<byte> kek)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (kek.Length != 32)
        {
            throw new CryptographicException(
                $"Vault KEK must be exactly 32 bytes (AES-256); got {kek.Length}. " +
                "Set KeyCustody:VaultKek (base64) or run in Development for the deterministic dev key.");
        }

        _directory = directory;
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

        Directory.CreateDirectory(_directory);

        var handleBytes = Encoding.UTF8.GetBytes(suggestedHandle);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var ciphertext = new byte[privateKeyBytes.Length];
        var tag = new byte[TagSizeBytes];
        var output = new byte[HeaderSize + ciphertext.Length + TagSizeBytes];
        var file = PathFor(suggestedHandle);
        var temp = file + ".tmp";
        try
        {
            using var aes = new AesGcm(_kek, TagSizeBytes);
            aes.Encrypt(nonce, privateKeyBytes, ciphertext, tag, handleBytes);

            Magic.CopyTo(output, 0);
            nonce.CopyTo(output, Magic.Length);
            ciphertext.CopyTo(output, HeaderSize);
            tag.CopyTo(output, HeaderSize + ciphertext.Length);

            File.WriteAllBytes(temp, output);
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            Array.Clear(ciphertext);
            Array.Clear(output);
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return suggestedHandle;
    }

    /// <inheritdoc/>
    public byte[]? TryLoad(string custodyHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(custodyHandle);

        var file = PathFor(custodyHandle);
        if (!File.Exists(file))
        {
            return null;
        }

        var blob = File.ReadAllBytes(file);
        if (blob.Length < HeaderSize + TagSizeBytes || !blob.AsSpan(0, Magic.Length).SequenceEqual(Magic))
        {
            throw new CryptographicException(
                $"Sealed-key blob '{Path.GetFileName(file)}' is not a recognizable SBQR key file.");
        }

        var nonce = blob.AsSpan(Magic.Length, NonceSizeBytes);
        var tag = blob.AsSpan(blob.Length - TagSizeBytes, TagSizeBytes);
        var ciphertext = blob.AsSpan(HeaderSize, blob.Length - HeaderSize - TagSizeBytes);
        var plaintext = new byte[ciphertext.Length];
        using var aes = new AesGcm(_kek, TagSizeBytes);
        // Throws CryptographicException on tag mismatch: wrong KEK, tampered
        // blob, or a handle/file swap — fail-closed, no partial plaintext.
        aes.Decrypt(nonce, ciphertext, tag, plaintext, Encoding.UTF8.GetBytes(custodyHandle));
        return plaintext;
    }

    private string PathFor(string handle)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(handle)))
            .ToLowerInvariant();
        return Path.Combine(_directory, hash + ".key");
    }
}
