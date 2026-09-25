using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SBQR.Modules.IdentityAccess.Api;
using SBQR.Modules.IdentityAccess.Infrastructure.Cryptography;
using SBQR.Modules.InstitutionTrust.Api;
using SBQR.Modules.QrGeneration.Api;
using SBQR.Modules.KeyCustody.Api;
using SBQR.Modules.Tenancy.Api;
using SBQR.Modules.Verification.Api;
using SBQR.Qr.IntegrationTests.Infrastructure;
using SBQR.SharedKernel.Application;

namespace SBQR.Qr.IntegrationTests.Infrastructure;

/// <summary>
/// Composes the REAL module graph (Tenancy, IdentityAccess, KeyCustody with
/// the PemVault provider + encrypted file key store, InstitutionTrust,
/// QrGeneration, Verification) against the ephemeral Postgres container — the
/// same composition SBQR.Api/Program.cs runs, minus the HTTP pipeline. The
/// audit logger and current-tenant seams are test doubles; everything else
/// (Ed25519 crypto, AES-GCM vault, CRC, TLV, PostgreSQL persistence) is
/// production code.
/// </summary>
internal sealed class QrFlowHostBuilder : IDisposable
{
    /// <summary>Deterministic 32-byte test KEK (base64) — never a production secret.</summary>
    public static readonly string TestVaultKek =
        Convert.ToBase64String(SHA256.HashData("sbqr-qr-e2e-test-kek"u8));

    /// <summary>
    /// The plaintext bootstrap secret the e2e tests authenticate with. The
    /// matching Argon2id PHC is computed at composition time and injected as
    /// <c>Auth:Bootstrap:ClientSecretHash</c> — the host has no plaintext
    /// code path, so the plaintext itself never reaches configuration.
    /// </summary>
    public const string BootstrapSecret = "e2e-bootstrap-secret";

    private readonly string _keyStoreDirectory =
        Path.Combine(Path.GetTempPath(), "sbqr-e2e-keys-" + Guid.NewGuid().ToString("N"));

    public (ServiceProvider Services, MutableCurrentTenant CurrentTenant, CapturingAuditLogger Audit)
        Build(PostgreSqlFixture postgres)
    {
        var services = new ServiceCollection();

        // Compute the Argon2id PHC of the e2e bootstrap plaintext at
        // composition time. The PHC — not the plaintext — is what the host
        // receives via configuration. Mirrors the production flow exactly:
        // an operator runs `--generate-bootstrap-secret` once, pastes the
        // PHC into configuration, and the plaintext lives only in their
        // personal secret manager.
        var bootstrapHash = new Argon2idSecretHasher().Hash(BootstrapSecret);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:sbqr_app"] = postgres.ConnectionString,

                // KeyCustody: the real Phase-1 provider against the real
                // encrypted file store (temp dir + deterministic test KEK).
                ["KeyCustody:ActiveProvider"] = "PemVault",
                ["KeyCustody:KeyStoreDirectory"] = _keyStoreDirectory,
                ["KeyCustody:VaultKek"] = TestVaultKek,

                // IdentityAccess bootstrap: hashed form (same shape as
                // Production / Staging / RC). The plaintext that produced
                // this PHC is the constant <see cref="BootstrapSecret"/>
                // above — used by the bootstrap leg of the tenant-
                // credential flow tests (see TenantConfigurationFlowTests).
                ["Auth:Bootstrap:ClientId"] = "platform-bootstrap",
                ["Auth:Bootstrap:ClientSecretHash"] = bootstrapHash,
                ["Jwt:SigningKey"] = "e2e-test-signing-key-0123456789abcdef",
                ["Jwt:Issuer"] = "sbqr",
                ["Jwt:Audience"] = "sbqr-api",
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        var moduleTypes = new[]
        {
            typeof(TenancyModule),
            typeof(IdentityAccessModule),
            typeof(KeyCustodyModule),
            typeof(InstitutionTrustModule),
            typeof(QrGenerationModule),
            typeof(VerificationModule),
        };

        var moduleInstances = moduleTypes
            .Where(t => typeof(IModule).IsAssignableFrom(t))
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToArray();

        var handlerAssemblies = moduleTypes
            .Select(t => t.Assembly)
            .Concat(moduleInstances.Select(m => m.ApplicationPartAssembly))
            .Distinct()
            .ToArray();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblies(handlerAssemblies));
        foreach (var assembly in handlerAssemblies)
        {
            services.AddValidatorsFromAssembly(assembly);
        }

        services.AddAutoMapper(cfg => cfg.AddMaps(handlerAssemblies));

        foreach (var module in moduleInstances)
        {
            module.RegisterServices(services, configuration);
        }

        // Test seams.
        var audit = new CapturingAuditLogger();
        var currentTenant = new MutableCurrentTenant();
        services.Replace(ServiceDescriptor.Singleton<IAuditLogger>(audit));
        services.Replace(ServiceDescriptor.Singleton<ICurrentTenant>(currentTenant));
        services.AddSingleton<IHttpContextAccessor>(_ => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        });

        return (services.BuildServiceProvider(validateScopes: true), currentTenant, audit);
    }

    public void Dispose()
    {
        if (Directory.Exists(_keyStoreDirectory))
        {
            Directory.Delete(_keyStoreDirectory, recursive: true);
        }
    }
}
