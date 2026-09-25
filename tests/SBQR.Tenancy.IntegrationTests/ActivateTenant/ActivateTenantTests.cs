using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Queries.GetTenantById;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Infrastructure.Persistence;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.ActivateTenant;

/// <summary>
/// End-to-end coverage for the <c>POST /admin/tenants/{id}/activate</c>
/// endpoint. Closes the gap that previously blocked a tenant from ever
/// reaching the <see cref="TenantStatus.Active"/> state via the API.
///
/// <para>
/// Wire-shape integration coverage (HTTP round-trip) is out of scope here —
/// that's a <c>WebApplicationFactory</c> concern. These tests prove the
/// MediatR pipeline + EF Core persistence + audit interceptor all wire up
/// correctly for the Activate path.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ActivateTenantTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;

    public ActivateTenantTests(PostgreSqlFixture pg)
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
    public async Task Activate_pending_tenant_succeeds()
    {
        // Arrange: register a fresh tenant (Pending by default).
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var create = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "Acme Bank Ltd",
            InstitutionCode: "010101"));
        create.IsSuccess.Should().BeTrue();
        var tenantId = create.Value.TenantId;

        // Act
        var result = await mediator.Send(new ActivateTenantCommand(tenantId));

        // Assert: success.
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TenantStatus.Active);

        // Read-back via the same GetTenantById path the controller uses.
        var read = await mediator.Send(new GetTenantByIdQuery(tenantId));
        read.IsSuccess.Should().BeTrue();
        read.Value.Status.Should().Be("Active");
        read.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Activate_terminated_tenant_returns_InvariantViolation()
    {
        // Arrange: register, then manually flip status to Terminated via a
        // direct DB write. We can't go through the API because the Terminate
        // endpoint lands in the next commit — this test pre-stages the
        // 409 path that the controller will surface.
        Guid tenantId;
        await using (var setupScope = _sp.CreateAsyncScope())
        {
            var setupMediator = setupScope.ServiceProvider.GetRequiredService<IMediator>();
            var create = await setupMediator.Send(new CreateTenantCommand(
                InstitutionName: "Done Bank Ltd",
                InstitutionCode: "020202"));
            create.IsSuccess.Should().BeTrue();
            tenantId = create.Value.TenantId.Value;
        }

        // Direct DB write to Terminated + is_active=false. Bypasses the API
        // because there's no public Terminate endpoint yet.
        await using (var dbScope = _sp.CreateAsyncScope())
        {
            var db = dbScope.ServiceProvider.GetRequiredService<TenancyDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE public.tenants SET status = 'TERMINATED', is_active = FALSE WHERE tenant_id = {0}",
                tenantId);
        }

        // Act
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var result = await mediator.Send(new ActivateTenantCommand(new TenantId(tenantId)));

        // Assert: InvariantViolation surfaces 409 in the controller mapping.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("Terminated");
    }
}