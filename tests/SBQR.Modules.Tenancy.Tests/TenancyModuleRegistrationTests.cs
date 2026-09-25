using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Api;
using SBQR.Modules.Tenancy.Contracts;
using SBQR.Modules.Tenancy.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests;

/// <summary>
/// Guards the module's DI registrations. <see cref="ITenantAdmissionDirectory"/>
/// was implemented but never registered — <c>POST /oauth/token</c> (IdentityAccess)
/// failed to resolve its handler at runtime until the seam was wired in
/// <see cref="TenancyModule.RegisterServices"/>.
/// </summary>
public sealed class TenancyModuleRegistrationTests
{
    [Fact]
    public void RegisterServices_registers_the_tenant_admission_directory_seam()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:sbqr_app"] =
                    "Host=localhost;Database=sbqr_app;Username=postgres;Password=postgres",
            })
            .Build();

        new TenancyModule().RegisterServices(services, configuration);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(ITenantAdmissionDirectory)
            && descriptor.ImplementationType == typeof(TenantAdmissionDirectory));
    }
}
