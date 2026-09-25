using System.Reflection;
using System.Collections.Immutable;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace SBQR.ArchitectureTests;

/// <summary>
/// Module-boundary architecture tests. These rules encode the
/// "module boundaries carry real design weight; layer boundaries
/// inside a module are convention" invariant from
/// tactical-design.md §1.
///
/// A failure here means a developer has introduced a forbidden
/// cross-module dependency. The build must not proceed.
/// </summary>
public sealed class ModuleBoundaryTests
{
    /// <summary>
    /// The QR payload codec lives inside the shared kernel under the
    /// SBQR.SharedKernel.QrCodec namespace. It must remain a pure
    /// published-language library: no EF Core, Dapper, NSec, BouncyCastle,
    /// MediatR, FluentValidation, or AutoMapper usage — that would
    /// re-introduce the classic "codec is coupled to its caller" bug
    /// tactical-design.md §2 is designed to prevent. The rule is
    /// namespace-scoped because the codec shares the kernel's assembly.
    /// </summary>
    [Fact]
    public void QrCodec_types_should_not_reference_infrastructure()
    {
        var result = Types.InNamespace("SBQR.SharedKernel.QrCodec")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Npgsql",
                "Dapper",
                "NSec.Cryptography",
                "BouncyCastle.Cryptography",
                "MediatR",
                "FluentValidation",
                "AutoMapper",
                "Microsoft.Extensions")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"QrCodec must remain pure. Failed types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");

        QrCodecTypes().Length.Should().BeGreaterThan(0,
            "the namespace filter found no codec types — if the codec moved namespaces, update QrCodecTypes().");
    }

    /// <summary>
    /// The codec must not depend on any module. Modules depend on the
    /// codec, never the other way around (Published Language).
    /// </summary>
    [Fact]
    public void QrCodec_types_should_not_depend_on_other_modules()
    {
        var result = Types.InNamespace("SBQR.SharedKernel.QrCodec")
            .ShouldNot()
            .HaveDependencyOnAny(
                "SBQR.Modules.Tenancy",
                "SBQR.Modules.InstitutionTrust",
                "SBQR.Modules.KeyCustody",
                "SBQR.Modules.QrGeneration",
                "SBQR.Modules.Verification",
                "SBQR.Modules.Audit",
                "SBQR.Modules.IdentityAccess")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"QrCodec must not depend on other modules. Failed types: {string.Join(", ", result.FailingTypeNames ?? Array.Empty<string>())}");
    }

    private static System.Collections.Immutable.ImmutableArray<Type> QrCodecTypes() =>
        typeof(SBQR.SharedKernel.Domain.Entity<>).Assembly
            .GetTypes()
            .Where(t => t.Namespace?.StartsWith("SBQR.SharedKernel.QrCodec", StringComparison.Ordinal) == true)
            .ToImmutableArray();

    /// <summary>
    /// Shared kernel has zero project references — it is the pure kernel.
    /// </summary>
    [Fact]
    public void SharedKernel_should_have_zero_project_references()
    {
        var kernelAssembly = typeof(SBQR.SharedKernel.Domain.Entity<>).Assembly;

        var referenced = kernelAssembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("SBQR.", StringComparison.Ordinal))
            .ToList();

        referenced.Should().BeEmpty(
            $"SBQR.SharedKernel must have zero SBQR.* project references. Found: {string.Join(", ", referenced)}");
    }

    /// <summary>
    /// Private-key and signing-provider types must live ONLY inside the
    /// KeyCustody module's <c>Infrastructure/Custody/</c> namespace.
    /// Encodes puku.md §1 invariant in the modular-monolith topology.
    /// </summary>
    [Fact]
    public void Signing_types_must_only_live_in_KeyCustody_Infrastructure_Custody()
    {
        var assembly = typeof(SBQR.Modules.KeyCustody.Api.KeyCustodyModule).Assembly;

        var offenders = assembly
            .GetTypes()
            .Where(t => t.Name.Contains("SigningProvider", StringComparison.Ordinal)
                     || t.Name.Contains("SigningKeyStore", StringComparison.Ordinal)
                     || t.Name.Contains("SigningKey", StringComparison.Ordinal))
            .Where(t => t.Namespace is not null
                     && !t.Namespace.StartsWith("SBQR.Modules.KeyCustody.Infrastructure.Custody", StringComparison.Ordinal)
                     && !t.Namespace.StartsWith("SBQR.Modules.KeyCustody.Domain", StringComparison.Ordinal)
                     && !t.Namespace.StartsWith("SBQR.Modules.KeyCustody.Application", StringComparison.Ordinal))
            .Select(t => t.FullName ?? t.Name)
            .ToList();

        offenders.Should().BeEmpty(
            "Signing-related types must live in SBQR.Modules.KeyCustody.Infrastructure.Custody. " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// NSec namespace import must only appear inside KeyCustody. No other
    /// module may import cryptographic primitives directly — they must go
    /// through the ISigningProvider abstraction in SBQR.SharedKernel.
    /// </summary>
    [Fact]
    public void NSec_should_only_be_referenced_from_KeyCustody()
    {
        var moduleAssemblies = new[]
        {
            typeof(SBQR.Modules.Tenancy.Api.TenancyModule).Assembly,
            typeof(SBQR.Modules.InstitutionTrust.Api.InstitutionTrustModule).Assembly,
            typeof(SBQR.Modules.QrGeneration.Api.QrGenerationModule).Assembly,
            typeof(SBQR.Modules.Verification.Api.VerificationModule).Assembly,
            typeof(SBQR.Modules.Audit.Api.AuditModule).Assembly,
            typeof(SBQR.Modules.IdentityAccess.Api.IdentityAccessModule).Assembly,
        };

        foreach (var asm in moduleAssemblies)
        {
            Types.InAssembly(asm)
                .ShouldNot()
                .HaveDependencyOn("NSec.Cryptography")
                .GetResult()
                .IsSuccessful
                .Should().BeTrue(
                    $"Assembly {asm.GetName().Name} must not reference NSec.Cryptography directly. " +
                    "Use SBQR.SharedKernel.Cryptography.ISigningProvider instead.");
        }
    }

    /// <summary>
    /// Modules may reference SBQR.SharedKernel (which now also carries the
    /// QR codec as the published language), but they must NOT reference any
    /// other module's Domain or Infrastructure layer. Cross-module calls go
    /// through MediatR contracts in Application/Contracts/.
    ///
    /// NOTE: This rule is checked manually per-pair in this scaffolding
    /// pass. A future epic should parameterize it.
    /// </summary>
    [Fact]
    public void Modules_should_not_reference_other_modules_Domain_or_Infrastructure()
    {
        // QrGeneration may use the codec (Published Language, via the shared
        // kernel) but must not reference any other module's layers.
        var qrGenerationResult = Types
            .InAssembly(typeof(SBQR.Modules.QrGeneration.Api.QrGenerationModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "SBQR.Modules.Tenancy.Domain",
                "SBQR.Modules.Tenancy.Infrastructure",
                "SBQR.Modules.InstitutionTrust.Domain",
                "SBQR.Modules.InstitutionTrust.Infrastructure",
                "SBQR.Modules.KeyCustody.Domain",
                "SBQR.Modules.KeyCustody.Infrastructure",
                "SBQR.Modules.Verification.Domain",
                "SBQR.Modules.Verification.Infrastructure",
                "SBQR.Modules.Audit.Domain",
                "SBQR.Modules.Audit.Infrastructure",
                "SBQR.Modules.IdentityAccess.Domain",
                "SBQR.Modules.IdentityAccess.Infrastructure")
            .GetResult();

        qrGenerationResult.IsSuccessful.Should().BeTrue(
            $"QrGeneration must not reference other modules' Domain or Infrastructure layers. " +
            $"Failed types: {string.Join(", ", qrGenerationResult.FailingTypeNames ?? Array.Empty<string>())}");

        // Same rule for Verification.
        var verificationResult = Types
            .InAssembly(typeof(SBQR.Modules.Verification.Api.VerificationModule).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "SBQR.Modules.Tenancy.Domain",
                "SBQR.Modules.Tenancy.Infrastructure",
                "SBQR.Modules.InstitutionTrust.Domain",
                "SBQR.Modules.InstitutionTrust.Infrastructure",
                "SBQR.Modules.KeyCustody.Domain",
                "SBQR.Modules.KeyCustody.Infrastructure",
                "SBQR.Modules.QrGeneration.Domain",
                "SBQR.Modules.QrGeneration.Infrastructure",
                "SBQR.Modules.Audit.Domain",
                "SBQR.Modules.Audit.Infrastructure",
                "SBQR.Modules.IdentityAccess.Domain",
                "SBQR.Modules.IdentityAccess.Infrastructure")
            .GetResult();

        verificationResult.IsSuccessful.Should().BeTrue(
            $"Verification must not reference other modules' Domain or Infrastructure layers. " +
            $"Failed types: {string.Join(", ", verificationResult.FailingTypeNames ?? Array.Empty<string>())}");
    }

    /// <summary>
    /// <see cref="SBQR.SharedKernel.Application.ICurrentTenant"/> implementations
    /// must live ONLY inside the host's <c>SBQR.Api.Infrastructure</c> namespace
    /// (the production resolver reads the JWT <c>tenant_id</c> claim from
    /// <see cref="IHttpContextAccessor"/>). Test-only stubs in the
    /// <c>*.Tests.Infrastructure</c> / <c>*.IntegrationTests.Infrastructure</c>
    /// namespaces are explicitly carved out so the Qr/Tenancy integration
    /// tests can swap the seam without tripping this rule.
    ///
    /// Encodes the architectural decision that "which tenant is this request
    /// for?" is a host-level cross-cutting concern — modules depend on the
    /// <see cref="SBQR.SharedKernel.Application.ICurrentTenant"/> contract but
    /// do not own its implementation. A module that ships its own
    /// implementation would silently bypass the host wiring.
    /// </summary>
    [Fact]
    public void CurrentTenant_implementations_must_only_live_in_Host_Infrastructure()
    {
        var contractAssembly = typeof(SBQR.SharedKernel.Application.ICurrentTenant).Assembly;

        // Every SBQR.* assembly that could plausibly host a resolver,
        // excluding the architecture-test harness itself (which references
        // NetArchTest and is irrelevant to this rule).
        var assembliesToScan = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => IsSbqrAssembly(a.GetName().Name))
            .Where(a => !a.GetReferencedAssemblies().Any(an =>
                (an.Name ?? string.Empty).StartsWith("NetArchTest", StringComparison.Ordinal)))
            .ToList();

        var contractType = contractAssembly.GetType("SBQR.SharedKernel.Application.ICurrentTenant")
            ?? throw new InvalidOperationException("ICurrentTenant contract not found in SharedKernel.");

        var offenders = new List<string>();

        foreach (var assembly in assembliesToScan)
        {
            var assemblyName = assembly.GetName().Name ?? string.Empty;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || type.IsInterface)
                {
                    continue;
                }

                if (!contractType.IsAssignableFrom(type))
                {
                    continue;
                }

                var ns = type.Namespace ?? string.Empty;
                var isTestNamespace = ns.Contains(".Tests.Infrastructure", StringComparison.Ordinal)
                                   || ns.Contains(".IntegrationTests.Infrastructure", StringComparison.Ordinal);

                // Test seams are allowed anywhere inside a Tests/IntegrationTests
                // assembly — the rule is "no production impl outside the host".
                if (isTestNamespace || assemblyName.EndsWith(".Tests", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ns.StartsWith("SBQR.Api.Infrastructure", StringComparison.Ordinal))
                {
                    offenders.Add($"{type.FullName} [{assemblyName}]");
                }
            }
        }

        offenders.Should().BeEmpty(
            "ICurrentTenant implementations must live in SBQR.Api.Infrastructure.* " +
            "(production) or *.Tests.Infrastructure / *.IntegrationTests.Infrastructure (test stubs). " +
            $"Offenders: {string.Join(", ", offenders)}");
    }

    private static bool IsSbqrAssembly(string? assemblyName) =>
        assemblyName is not null && assemblyName.StartsWith("SBQR.", StringComparison.Ordinal);
}
