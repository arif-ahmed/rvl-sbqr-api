using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Queries.GetTenantById;
using SBQR.SharedKernel.Application;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.GetTenant;

/// <summary>
/// End-to-end coverage for the read-side <c>GET /admin/tenants/{id}</c>
/// endpoint (replaces the 501 placeholder that lived there since Story 2/3).
///
/// <para>
/// We exercise the controller path by dispatching <see cref="GetTenantByIdQuery"/>
/// through the same MediatR pipeline the controller uses — integration coverage
/// of the wire shape itself is out of scope here (that's a WebApplicationFactory
/// concern, which Story 6 will land for the entire module).
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GetTenantByIdTests : IAsyncLifetime
{
    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;

    public GetTenantByIdTests(PostgreSqlFixture pg)
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
    public async Task Get_by_id_returns_full_tenant_response()
    {
        // Arrange: register a tenant first.
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var createResult = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "Acme Bank Ltd",
            InstitutionCode: "010101"));
        createResult.IsSuccess.Should().BeTrue();
        var tenantId = createResult.Value.TenantId;

        // Act: GET by id through the same MediatR pipeline the controller uses.
        var query = new GetTenantByIdQuery(tenantId);
        var result = await mediator.Send(query);

        // Assert: shape matches what POST /admin/tenants returns.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.TenantId.Should().Be(tenantId.Value);
        result.Value.InstitutionName.Should().Be("Acme Bank Ltd");
        result.Value.InstitutionCode.Should().Be("010101");
        result.Value.Status.Should().Be("Pending");
        result.Value.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Get_by_id_returns_NotFound_when_tenant_does_not_exist()
    {
        // Arrange: random id, no row.
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        var missingId = new SBQR.Modules.Tenancy.Domain.Aggregates.TenantId(Guid.NewGuid());

        // Act
        var result = await mediator.Send(new GetTenantByIdQuery(missingId));

        // Assert: NotFound.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(missingId.Value.ToString("D"));
    }
}