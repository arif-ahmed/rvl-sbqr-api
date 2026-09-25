using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;
using SBQR.Modules.Tenancy.Application.Queries.GetTenantById;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.TerminateTenant;

/// <summary>
/// End-to-end coverage for <c>POST /admin/tenants/{id}/terminate</c>. Closes
/// the gap that previously left tenants with no offboarding path (the only way
/// to reach <see cref="TenantStatus.Terminated"/> was through internal
/// compensation paths). Now Terminate is the public, audited entry point.
///
/// <para>The cascade is exercised end-to-end via the
/// <c>StubCredentialProvisioner</c> in the test host — verifies the seam
/// resolves and the cascade fires without blowing up the request.</para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TerminateTenantTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;

    public TerminateTenantTests(PostgreSqlFixture pg)
    {
        _pg = pg ?? throw new ArgumentNullException(nameof(pg));
    }

    public async Task InitializeAsync()
    {
        await _pg.ResetAsync();
        var (sp, _) = TenancyHostBuilder.BuildForPostgres(_pg);
        _sp = sp;
    }

    public async Task DisposeAsync()
    {
        if (_sp is not null)
        {
            await _sp.DisposeAsync();
        }
    }

    [Fact]
    public async Task Terminate_active_tenant_succeeds_and_flips_isActive_to_false()
    {
        // Arrange: register + activate so we have a non-Terminated tenant.
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var create = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "Exit Bank Ltd",
            InstitutionCode: "030303"));
        create.IsSuccess.Should().BeTrue();
        var tenantId = create.Value.TenantId;

        var activate = await mediator.Send(new ActivateTenantCommand(tenantId));
        activate.IsSuccess.Should().BeTrue();

        // Act
        var result = await mediator.Send(new TerminateTenantCommand(
            tenantId, Reason: "Offboarding"));

        // Assert: terminal state reached.
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TenantStatus.Terminated);
        result.Value.Reason.Should().Be("Offboarding");

        // Read-back via GetTenantById confirms the row is in the terminal
        // state — Status="Terminated", IsActive=false.
        var read = await mediator.Send(new GetTenantByIdQuery(tenantId));
        read.IsSuccess.Should().BeTrue();
        read.Value.Status.Should().Be("Terminated");
        read.Value.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Terminate_already_terminated_tenant_returns_409()
    {
        // Arrange: register + activate + terminate (so we're now Terminated).
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var create = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "Dup Bank Ltd",
            InstitutionCode: "040404"));
        create.IsSuccess.Should().BeTrue();
        var tenantId = create.Value.TenantId;

        await mediator.Send(new ActivateTenantCommand(tenantId));
        var firstTerminate = await mediator.Send(
            new TerminateTenantCommand(tenantId, Reason: "first"));
        firstTerminate.IsSuccess.Should().BeTrue();

        // Act: second termination should fail.
        var secondTerminate = await mediator.Send(
            new TerminateTenantCommand(tenantId, Reason: "second"));

        // Assert: 409 mapping (InvariantViolation).
        secondTerminate.IsFailure.Should().BeTrue();
        secondTerminate.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        secondTerminate.ErrorMessage.Should().Contain("already Terminated");
    }
}