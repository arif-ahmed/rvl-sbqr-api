using FluentAssertions;
using SBQR.Modules.Tenancy.Api;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests;

/// <summary>
/// Smoke tests for the Tenancy placeholder. Real Tenant/TenantConfiguration
/// and persistence tests land in later epic-3 stories (02+).
/// </summary>
public sealed class TenancyModuleSmokeTests
{
    [Fact]
    public void TenancyModule_should_report_its_name()
    {
        var module = new TenancyModule();

        module.Name.Should().Be("tenancy");
    }

    [Fact]
    public void TenancyModule_should_expose_the_application_assembly_for_mediatr_scan()
    {
        // MediatR's RegisterServicesFromAssembly discovers IRequestHandler<>
        // implementations. They live in SBQR.Modules.Tenancy.Application
        // (the Clean-Architecture Application layer); the API layer only
        // holds controllers. Earlier revisions of the module pointed
        // ApplicationPartAssembly at the API assembly and MediatR
        // silently missed every Tenancy handler.
        var module = new TenancyModule();

        module.ApplicationPartAssembly.Should().BeSameAs(typeof(CreateTenantCommandHandler).Assembly);
    }
}
