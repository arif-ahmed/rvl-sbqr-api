using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.SharedKernel.Cryptography;

namespace SBQR.Modules.KeyCustody.Infrastructure.Vault;

/// <summary>
/// <see cref="IKeyVaultProvider"/> for the Phase-1 software vault:
/// per-handle AES-256-GCM blobs under <c>KeyCustody:KeyStoreDirectory</c>,
/// sealed by the operator-supplied KEK (<c>KeyCustody:VaultKek</c>).
///
/// <para>
/// Composes the existing <see cref="EncryptedFileSigningKeyStore"/>; it does
/// NOT replace it. The blob layout (magic header, nonce, ciphertext+tag)
/// stays untouched so future migrations remain binary-compatible. A future
/// Azure Key Vault / AWS KMS / HSM provider implements
/// <see cref="IKeyVaultProvider"/> directly and supplies its own
/// <see cref="ISigningKeyStore"/>.
/// </para>
///
/// <para>
/// KEK resolution mirrors <c>KeyCustodyModule.ResolveKek</c>: prefer the
/// configured base64 KEK, fall back to the deterministic Development key
/// when none is configured. The host's Production guard refuses to start
/// without an explicit KEK; this class does not re-implement that policy.
/// </para>
/// </summary>
public sealed class LocalKeyVaultProvider : IKeyVaultProvider
{
    private readonly IConfiguration _configuration;
    private readonly Lazy<ISigningKeyStore> _store;

    public LocalKeyVaultProvider(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _store = new Lazy<ISigningKeyStore>(BuildStore, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public string Name => "Local";

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>
    {
        ["backend"] = "encrypted-file",
        ["cipher"] = "aes-256-gcm",
        ["kek_source"] = "config:KeyCustody:VaultKek",
    };

    /// <inheritdoc/>
    public ISigningKeyStore GetSigningKeyStore() => _store.Value;

    private EncryptedFileSigningKeyStore BuildStore()
    {
        var directory = _configuration["KeyCustody:KeyStoreDirectory"] ?? DefaultKeyStoreDirectory();
        var kek = ResolveKek(_configuration);
        return new EncryptedFileSigningKeyStore(directory, kek);
    }

    private static byte[] ResolveKek(IConfiguration configuration)
    {
        var configured = configuration["KeyCustody:VaultKek"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var kek = Convert.FromBase64String(configured);
            if (kek.Length != 32)
            {
                throw new InvalidOperationException(
                    $"KeyCustody:VaultKek must decode to exactly 32 bytes; got {kek.Length}.");
            }

            return kek;
        }

        // SHA-256 of a fixed dev label — deterministic, 32 bytes, obviously
        // not a production secret. The host's Production guard rejects this
        // fallback path before it can ever run.
        return SHA256.HashData("sbqr-dev-vault-kek-v1"u8);
    }

    private static string DefaultKeyStoreDirectory() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "sbqr", "key-vault")
            : "/var/lib/sbqr/key-vault";
}
