using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.Storage;

namespace SBQR.Modules.KeyCustody.Infrastructure.Vault;

/// <summary>
/// S3-backed <see cref="IKeyVaultProvider"/>. Same shape and configuration
/// conventions as <see cref="LocalKeyVaultProvider"/>: reads
/// <c>KeyCustody:VaultKek</c> (base64, 32 bytes) for the AES-256 KEK.
///
/// <para>
/// Object storage itself is <b>not</b> built here — this module resolves
/// the shared <see cref="IObjectStorageFactory"/> from DI (registered once
/// at the host composition root) and asks it for a store scoped to its own
/// <c>Storage:VaultFolder</c>. Any other module gets the same reuse by
/// resolving the same factory with its own folder name; none of them need
/// to know the backend is S3.
/// </para>
/// </summary>
public sealed class S3KeyVaultProvider : IKeyVaultProvider
{
    private readonly IObjectStorageFactory _storageFactory;
    private readonly IConfiguration _configuration;
    private readonly Lazy<ISigningKeyStore> _store;

    public S3KeyVaultProvider(IObjectStorageFactory storageFactory, IConfiguration configuration)
    {
        _storageFactory = storageFactory ?? throw new ArgumentNullException(nameof(storageFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _store = new Lazy<ISigningKeyStore>(BuildStore, isThreadSafe: true);
    }

    /// <inheritdoc/>
    public string Name => "S3";

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string> Metadata { get; } = new Dictionary<string, string>
    {
        ["backend"] = "s3",
        ["encryption"] = "sse-s3",
        ["kek_source"] = "config:KeyCustody:VaultKek",
        ["bucket_source"] = "config:Storage:BucketName",
        ["vault_folder_source"] = "config:Storage:VaultFolder",
    };

    /// <inheritdoc/>
    public ISigningKeyStore GetSigningKeyStore() => _store.Value;

    private S3SigningKeyStore BuildStore()
    {
        var vaultFolder = _configuration["Storage:VaultFolder"] ?? "keycustody";
        var storage = _storageFactory.Create(vaultFolder);
        var kek = ResolveKek(_configuration);
        return new S3SigningKeyStore(storage, kek);
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

        // Same deterministic dev key LocalKeyVaultProvider uses — keeps the
        // two providers interchangeable for tests (a blob minted by Local
        // decrypts under S3 with the same KEK, and vice versa).
        return SHA256.HashData("sbqr-dev-vault-kek-v1"u8);
    }
}
