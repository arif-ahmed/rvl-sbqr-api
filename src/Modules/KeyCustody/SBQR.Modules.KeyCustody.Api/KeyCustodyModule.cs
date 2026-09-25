using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using SBQR.Modules.KeyCustody.Domain.Interfaces;
using SBQR.Modules.KeyCustody.Infrastructure.Cryptography;
using SBQR.Modules.KeyCustody.Infrastructure.Custody;
using SBQR.Modules.KeyCustody.Infrastructure.Persistence;
using SBQR.Modules.KeyCustody.Infrastructure.Persistence.Repositories;
using SBQR.Modules.KeyCustody.Infrastructure.Persistence.UnitOfWork;
using SBQR.Modules.KeyCustody.Infrastructure.Vault;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Cryptography;
using SBQR.SharedKernel.Storage;

namespace SBQR.Modules.KeyCustody.Api;

/// <summary>
/// Composition root for the KeyCustody module. Registers the active
/// <see cref="ISigningProvider"/> (dev = <see cref="PlainFileSigningProvider"/>;
/// Phase 1 = <see cref="PemVaultSigningProvider"/>; Phase 2 = <see cref="HsmSigningProvider"/>)
/// based on configuration.
///
/// Epic-5 (KeyCustody stories) fills in key lifecycle, sign payload
/// handlers, and the trust-store contract consumed by Verification.
/// </summary>
public sealed class KeyCustodyModule : IModule
{
    public string Name => "key-custody";

    public Assembly ApplicationPartAssembly =>
        typeof(SBQR.Modules.KeyCustody.Application.Queries.GetSigningKeyQueryHandler).Assembly;

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ---------------------------------------------------------------------
        // 1. Vault backend selector (IKeyVaultProvider) — picks which physical
        //    store backs the ISigningKeyStore the rest of this module uses.
        //    Selection is config-driven (Crypto:VaultProvider; default "Local").
        //    The single shipped implementation is LocalKeyVaultProvider,
        //    which composes the existing Phase-1 software vault
        //    (EncryptedFileSigningKeyStore — AES-256-GCM blobs sealed by the
        //    operator-supplied KEK at KeyCustody:VaultKek). The host refuses
        //    to start in Production with an unknown provider name (see
        //    SBQR.Api/Program.cs §10a — added when this seam landed).
        //    Future Azure Key Vault / AWS KMS / HSM backends ship as new
        //    IKeyVaultProvider implementations registered in the switch below.
        //
        //    Registered via a DI factory delegate (not an eagerly-built
        //    instance) so the "s3" branch can resolve the host-registered
        //    IObjectStorageFactory singleton lazily, at first resolution —
        //    RegisterServices runs before the container is built, so there
        //    is no IServiceProvider to resolve from yet at this point.
        // ---------------------------------------------------------------------
        var providerName = configuration["Crypto:VaultProvider"] ?? "Local";
        services.AddSingleton<IKeyVaultProvider>(sp => providerName.ToLowerInvariant() switch
        {
            "local" => new LocalKeyVaultProvider(configuration),
            "s3" => new S3KeyVaultProvider(sp.GetRequiredService<IObjectStorageFactory>(), configuration),
            _ => throw new InvalidOperationException(
                $"Unknown Crypto:VaultProvider '{providerName}'. Known providers: Local, S3. " +
                "Additional providers (Azure Key Vault, AWS KMS, HSM) ship as additional IKeyVaultProvider implementations."),
        });
        services.AddSingleton<ISigningKeyStore>(sp => sp.GetRequiredService<IKeyVaultProvider>().GetSigningKeyStore());

        // ---------------------------------------------------------------------
        // 1b. DbContext over crypto_keys — KeyCustody owns both reads (key
        //     resolution at signing time, GetSigningKeyQuery) and the full
        //     write-side lifecycle (generate/adopt/rotate/suspend/reinstate/
        //     retire). Schema owned by db/migrations, never EF migrations.
        // ---------------------------------------------------------------------
        services.AddDbContext<KeyCustodyDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("sbqr_app")
                ?? throw new InvalidOperationException(
                    "Missing connection string 'sbqr_app' for KeyCustodyDbContext. " +
                    "Configure ConnectionStrings:sbqr_app in appsettings.json or environment variables.");
            options.UseNpgsql(connectionString);
        });

        // ---------------------------------------------------------------------
        // 1c. Signature verification — public-key-only, stateless.
        // ---------------------------------------------------------------------
        services.AddSingleton<SBQR.SharedKernel.Cryptography.ISignatureVerifier,
            Ed25519SignatureVerifier>();

        // ---------------------------------------------------------------------
        // 1d. Crypto-key write-side ports. Moved here from Tenancy so the full
        //     lifecycle (mint/rotate/suspend/reinstate/retire) is owned by one
        //     module, matching the ownership note in migration
        //     20260903120200. ICryptoKeyRepository and the unit of work are
        //     scoped (share the DbContext's request scope); the
        //     generator/validator are stateless singletons. The UoW registers
        //     under IKeyCustodyUnitOfWork (NOT the shared IUnitOfWork) so it
        //     never collides with Tenancy's bare registration — last
        //     registration wins in the one shared container, and Tenancy
        //     handlers resolving IUnitOfWork would otherwise flush through
        //     KeyCustodyDbContext (or vice versa, by composition order).
        // ---------------------------------------------------------------------
        services.AddScoped<ICryptoKeyRepository, CryptoKeyRepository>();
        services.AddScoped<IKeyCustodyUnitOfWork, KeyCustodyUnitOfWork>();
        services.AddSingleton<IKeyPairGenerator, Ed25519KeyPairGenerator>();
        services.AddSingleton<IKeyPairValidator, Ed25519PEMValidator>();

        // ---------------------------------------------------------------------
        // 2. The active ISigningProvider, selected by configuration. Default
        //    is PlainFile (dev); the host guard refuses PlainFile in
        //    Production. PemVault reads the tenant's ACTIVE crypto_keys row
        //    and signs via the key store above.
        // ---------------------------------------------------------------------
        var activeProvider = configuration["KeyCustody:ActiveProvider"] ?? "PlainFile";
        switch (activeProvider.ToUpperInvariant())
        {
            case "PLAINFILE":
                services.AddSingleton<ISigningProvider, PlainFileSigningProvider>();
                break;
            case "PEMVAULT":
                services.AddScoped<PemVaultSigningProvider>();
                services.AddScoped<ISigningProvider>(sp => sp.GetRequiredService<PemVaultSigningProvider>());
                break;
            default:
                throw new InvalidOperationException(
                    $"KeyCustody:ActiveProvider '{activeProvider}' is unknown. Use PlainFile (dev) or PemVault.");
        }
    }
}
