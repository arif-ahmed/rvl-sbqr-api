using FluentAssertions;
using SBQR.Modules.Verification.Api;
using Xunit;

namespace SBQR.Modules.Verification.Tests;

/// <summary>
/// Smoke tests for the Verification placeholder. Real QR-validation
/// handler tests land in epic-8 (Verification stories).
/// </summary>
public sealed class VerificationModuleSmokeTests
{
    [Fact]
    public void VerificationModule_should_report_its_name()
    {
        var module = new VerificationModule();

        module.Name.Should().Be("verification");
    }

    [Fact]
    public void VerificationModule_should_expose_its_application_assembly_as_application_part()
    {
        var module = new VerificationModule();

        // ApplicationPartAssembly feeds the MediatR/Api part discovery and
        // intentionally points at the APPLICATION assembly (where the
        // handlers live), not the Api composition-root assembly.
        module.ApplicationPartAssembly.Should().BeSameAs(
            typeof(SBQR.Modules.Verification.Application.Commands.ValidateQrCommand).Assembly);
    }
}
