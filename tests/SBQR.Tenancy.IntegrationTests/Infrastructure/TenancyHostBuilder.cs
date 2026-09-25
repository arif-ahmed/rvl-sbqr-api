using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Api;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// Builds the service provider the integration tests use to dispatch MediatR
/// commands against a real PostgreSQL-backed TenancyDbContext. Mirrors the
/// composition <c>SBQR.Api/Program.cs</c> runs at startup, but:
/// <list type="bullet">
///   <item>skips all other <c>IModule</c> implementations (Tenancy only);</item>
///   <item>overrides <c>IAuditLogger</c> with <see cref="CapturingAuditLogger"/>
///         so the test can assert on entries;</item>
///   <item>registers a stub <c>IHttpContextAccessor</c> backed by a default
///         (empty) <c>HttpContext</c> so <c>HttpContextActorProvider</c>
///         resolves to <c>"system"</c>.</item>
/// </list>
///
/// <para>
/// The signing-key lifecycle (mint/rotate/suspend/reinstate/retire) now lives
/// entirely in the KeyCustody module, which this Tenancy-only host
/// deliberately does not register. Suspend/Reactivate/TerminateTenantCommandHandler
/// still dispatch <see cref="SuspendTenantSigningKeysCommand"/> /
/// <see cref="ReinstateTenantSigningKeysCommand"/> via <c>ISender</c> as part
/// of their cascade, so this host registers no-op stub handlers for those two
/// commands — mirroring <see cref="StubConfigurationProvisioner"/> for the
/// IdentityAccess seam.
/// </para>
///
/// <para>
/// The FR-TENANT-001 activate-gate reads three preconditions from cross-BC
/// Contracts seams: <c>HasActiveAsync</c> on the IdentityAccess provisioner
/// (implemented below), <see cref="HasActiveSigningKeyQuery"/> (KeyCustody),
/// and <see cref="GetInstitutionPublicKeyQuery"/> (InstitutionTrust). The
/// latter two are no-op stub handlers — <see cref="StubHasActiveSigningKeyHandler"/>
/// returns <c>true</c> by default, <see cref="StubGetInstitutionPublicKeyHandler"/>
/// returns a non-null view by default. The provisioner's
/// <c>HasActiveAsync</c> returns <c>true</c> by default. Tests that need
/// gate-failure behaviour can swap the stubs or use a NSubstitute substitute
/// in a per-test scope.
/// </para>
/// </summary>
internal static class TenancyHostBuilder
{
    /// <summary>
    /// Build a scoped-ready <see cref="ServiceProvider"/> around
    /// <paramref name="postgres"/>'s connection string. The caller is
    /// expected to wrap the returned provider in <c>await using</c> /
    /// <c>using</c> and to create per-test scopes as needed.
    /// </summary>
    /// <param name="postgres">The shared <see cref="PostgreSqlFixture"/>
    /// providing the connection string.</param>
    /// <param name="auditLogger">Optional pre-built capturing audit logger;
    /// if <c>null</c>, a fresh instance is created so the caller can still
    /// assert on <see cref="CapturingAuditLogger.Entries"/>.</param>
    public static (ServiceProvider Services, CapturingAuditLogger Audit) BuildForPostgres(
        PostgreSqlFixture postgres,
        CapturingAuditLogger? auditLogger = null)
    {
        ArgumentNullException.ThrowIfNull(postgres);

        var audit = auditLogger ?? new CapturingAuditLogger();
        var services = new ServiceCollection();

        // ---- Logging (minimal; raise verbosity to Debug if a test fails) ----
        services.AddLogging(b => b
            .AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss.fff ";
            })
            .SetMinimumLevel(LogLevel.Warning));

        // ---- Configuration — surface ONLY the connection string; nothing
        //      else from the host's appsettings.json is relevant to a single
        //      module under test. ----
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:sbqr_app"] = postgres.ConnectionString,
            })
            .Build();
        services.AddSingleton<IConfiguration>(configuration);

        // ---- MediatR: register handlers from the Tenancy Application assembly
        //      (where the IRequestHandler implementations actually live, per
        //      TenancyModule.ApplicationPartAssembly). The CompositionRoot in
        //      Program.cs uses TenancyModule.ApplicationPartAssembly — the
        //      test mirror narrows the scan to that same assembly. ----
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(
            typeof(SBQR.Modules.Tenancy.Application.Commands.CreateTenant.CreateTenantCommandHandler).Assembly));

        // ---- Tenancy module. RegisterServices reads the connection string
        //      from IConfiguration (set above), wires TenancyDbContext +
        //      repositories + audit interceptor + crypto (kept registered
        //      for the future crypto-key endpoint). No current scenario here
        //      exercises the crypto primitives, so we don't override them. ----
        // The composition root in Program.cs discovers IModule instances
        // reflectively and calls RegisterServices on each. We mirror that
        // here for the Tenancy module only.
        new TenancyModule().RegisterServices(services, configuration);

        // ---- Cross-cutting stubs (override the module's own choices where
        //      a test-only substitute is needed) ----
        services.Replace(ServiceDescriptor.Singleton<IAuditLogger>(audit));
        services.AddSingleton<ICurrentTenant, NoopCurrentTenant>();

        // Stub configuration provisioner. The real implementation is registered
        // by IdentityAccessModule, which this Tenancy-only host deliberately
        // skips. The TerminateTenantCommandHandler's configuration cascade (and
        // any future lifecycle action that drives tenant_configurations through the
        // Contracts seam) needs this stub so the cascade resolves without
        // pulling IdentityAccess into the test host. Activate intentionally does
        // NOT call into this seam — it's a state-machine entry, not a
        // suspend-reversal.
        services.AddSingleton<SBQR.Modules.IdentityAccess.Contracts.ITenantConfigurationProvisioner,
            StubConfigurationProvisioner>();

        // Stub signing-key cascade handlers. The real handlers live in
        // KeyCustody.Application, which this Tenancy-only host deliberately
        // skips (no KeyCustodyDbContext here); these no-ops let ISender.Send
        // resolve without pulling KeyCustody's persistence into this host.
        services.AddSingleton<IRequestHandler<SuspendTenantSigningKeysCommand>, NoopSuspendSigningKeysHandler>();
        services.AddSingleton<IRequestHandler<ReinstateTenantSigningKeysCommand>, NoopReinstateSigningKeysHandler>();

        // FR-TENANT-001 activate-gate query stubs. The real handlers live in
        // KeyCustody.Application and InstitutionTrust.Application respectively;
        // this Tenancy-only host does not register those modules. The stubs
        // return truthy defaults so the gate passes; tests that need
        // gate-failure behaviour can replace the registrations in a per-test
        // scope before invoking the handler.
        services.AddSingleton<IRequestHandler<HasActiveSigningKeyQuery, bool>, StubHasActiveSigningKeyHandler>();
        services.AddSingleton<IRequestHandler<GetInstitutionPublicKeyQuery, InstitutionPublicKeyView?>,
            StubGetInstitutionPublicKeyHandler>();

        // Stub HTTP context so HttpContextActorProvider.CurrentActor() resolves
        // to "system" without throwing on null HttpContext.
        services.AddSingleton<IHttpContextAccessor>(_ => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext(),
        });

        // Validate scopes so a misconfigured scoped/singleton mismatch shows
        // up at build time instead of at first-request.
        var sp = services.BuildServiceProvider(validateScopes: true);
        return (sp, audit);
    }

    /// <summary>
    /// Run <paramref name="action"/> inside a fresh DI scope. Mirrors the
    /// per-request scope the host pipeline would create.
    /// </summary>
    public static async Task<T> InScope<T>(
        this ServiceProvider sp,
        Func<IServiceProvider, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = sp.CreateAsyncScope();
        return await action(scope.ServiceProvider).ConfigureAwait(false);
    }

    /// <summary>
    /// Deterministic stand-in for the IdentityAccess tenant-configuration
    /// provisioner. The suspend/reinstate cascades are no-ops (the stub never
    /// records state). Re-added when TerminateTenantCommandHandler started
    /// calling <c>SuspendAllForTenantAsync</c> as part of its terminal-state
    /// cascade. The provisioner surface now uses <c>Configuration</c> because
    /// <see cref="ITenantConfigurationProvisioner"/> is the cross-module seam
    /// name; the underlying aggregate is <c>TenantConfiguration</c> (table
    /// <c>public.tenant_configurations</c>).
    /// </summary>
    private sealed class StubConfigurationProvisioner
        : SBQR.Modules.IdentityAccess.Contracts.ITenantConfigurationProvisioner
    {
        public Task<SBQR.Modules.IdentityAccess.Contracts.ProvisionedConfiguration> ProvisionAsync(
            Guid tenantId,
            string institutionCode,
            bool isQrGenerationAllowed,
            bool isQrValidationAllowed,
            CancellationToken cancellationToken = default)
            // The capability flags mirror what the real provisioner
            // would copy onto the tenant_configurations row at mint
            // time. This stub is a no-op for the Tenancy-only host —
            // the per-tenant flags here are accepted to satisfy the
            // ITenantConfigurationProvisioner contract but never
            // persisted (no IdentityAccess DbContext is registered
            // in this host).
            => Task.FromResult(new SBQR.Modules.IdentityAccess.Contracts.ProvisionedConfiguration(
                CredentialId: Guid.NewGuid(),
                ClientId: $"{institutionCode.ToLowerInvariant()}-stub-not-issued",
                ClientSecret: "stub-not-issued-by-this-host",
                ExpiresAt: DateTimeOffset.UtcNow.AddYears(1)));

        public Task<IReadOnlyList<SBQR.Modules.IdentityAccess.Contracts.ConfigurationSnapshot>> SuspendAllForTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SBQR.Modules.IdentityAccess.Contracts.ConfigurationSnapshot>>([]);

        public Task<IReadOnlyList<SBQR.Modules.IdentityAccess.Contracts.ConfigurationSnapshot>> ReinstateAllForTenantAsync(
            Guid tenantId,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SBQR.Modules.IdentityAccess.Contracts.ConfigurationSnapshot>>([]);

        /// <summary>
        /// FR-TENANT-001 activate-gate read: stub returns <c>true</c> so the
        /// gate passes by default in this Tenancy-only host. Tests that need
        /// the gate to fail can replace the <c>ITenantConfigurationProvisioner</c>
        /// registration with a NSubstitute substitute in a per-test scope.
        /// </summary>
        public Task<bool> HasActiveAsync(Guid tenantId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    /// <summary>No-op stand-in for KeyCustody's real suspend-signing-key cascade handler.</summary>
    private sealed class NoopSuspendSigningKeysHandler : IRequestHandler<SuspendTenantSigningKeysCommand>
    {
        public Task Handle(SuspendTenantSigningKeysCommand request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>No-op stand-in for KeyCustody's real reinstate-signing-key cascade handler.</summary>
    private sealed class NoopReinstateSigningKeysHandler : IRequestHandler<ReinstateTenantSigningKeysCommand>
    {
        public Task Handle(ReinstateTenantSigningKeysCommand request, CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    /// <summary>
    /// FR-TENANT-001 activate-gate: returns <c>true</c> by default so the
    /// gate passes. The real <c>HasActiveSigningKeyQueryHandler</c> lives in
    /// KeyCustody.Application; this stub lets the Tenancy-only host resolve
    /// the query without pulling KeyCustodyDbContext into the test host.
    /// </summary>
    private sealed class StubHasActiveSigningKeyHandler : IRequestHandler<HasActiveSigningKeyQuery, bool>
    {
        public Task<bool> Handle(HasActiveSigningKeyQuery request, CancellationToken cancellationToken)
            => Task.FromResult(true);
    }

    /// <summary>
    /// FR-TENANT-001 activate-gate: returns a non-null view by default so the
    /// gate passes. The real <c>GetInstitutionPublicKeyQueryHandler</c> lives
    /// in InstitutionTrust.Application; this stub lets the Tenancy-only host
    /// resolve the query without pulling InstitutionTrustDbContext in.
    /// </summary>
    private sealed class StubGetInstitutionPublicKeyHandler
        : IRequestHandler<GetInstitutionPublicKeyQuery, InstitutionPublicKeyView?>
    {
        public Task<InstitutionPublicKeyView?> Handle(
            GetInstitutionPublicKeyQuery request,
            CancellationToken cancellationToken)
            => Task.FromResult<InstitutionPublicKeyView?>(new InstitutionPublicKeyView(
                InstitutionCode: request.InstitutionCode,
                KeyVersion: 1,
                PublicKeyPem: "-----BEGIN PUBLIC KEY-----STUB-----END PUBLIC KEY-----",
                Status: "Active"));
    }
}
