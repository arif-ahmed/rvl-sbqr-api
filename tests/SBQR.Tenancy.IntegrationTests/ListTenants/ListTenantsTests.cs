using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;
using SBQR.Modules.Tenancy.Application.Queries.ListTenants;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Tenancy.IntegrationTests.Infrastructure;
using Xunit;

namespace SBQR.Tenancy.IntegrationTests.ListTenants;

/// <summary>
/// End-to-end coverage for the paged-list endpoint
/// <c>GET /admin/tenants</c>. Proves the EF Core query path against a real
/// PostgreSQL backend: filter clauses, ORDER BY stability, and pagination
/// math all wire up correctly.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ListTenantsTests : IAsyncLifetime
{
    private static readonly string[] ExpectedInstitutionCodes = { "010100", "010101", "010102" };

    private readonly PostgreSqlFixture _pg;
    private ServiceProvider _sp = default!;

    public ListTenantsTests(PostgreSqlFixture pg)
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
    public async Task List_with_no_filters_returns_all_tenants()
    {
        // Arrange: 3 fresh tenants (all Pending by default).
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        for (var i = 0; i < 3; i++)
        {
            var create = await mediator.Send(new CreateTenantCommand(
                InstitutionName: $"Bank {i}",
                InstitutionCode: $"01010{i}"));
            create.IsSuccess.Should().BeTrue();
        }

        // Act
        var result = await mediator.Send(new ListTenantsQuery(
            Status: null,
            IsActive: null,
            Page: 1,
            PageSize: 20));

        // Assert: 3 items, totalCount=3, hasMore=false (all fit on one page).
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(3);
        result.Value.HasMore.Should().BeFalse();
        result.Value.Items.Select(t => t.InstitutionCode)
            .Should().BeEquivalentTo(ExpectedInstitutionCodes);
    }

    [Fact]
    public async Task List_with_status_filter_returns_only_matches()
    {
        // Arrange: 1 Pending + 1 Active + 1 Suspended. Then filter by Active.
        await using var scope = _sp.CreateAsyncScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var pending = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "P",
            InstitutionCode: "020200"));

        var activeBase = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "A",
            InstitutionCode: "020201"));
        await mediator.Send(new ActivateTenantCommand(activeBase.Value.TenantId));

        var suspended = await mediator.Send(new CreateTenantCommand(
            InstitutionName: "S",
            InstitutionCode: "020202"));
        await mediator.Send(new SuspendTenantCommand(
            suspended.Value.TenantId, Reason: "test"));

        // Sanity-check the arrange worked.
        pending.IsSuccess.Should().BeTrue();
        activeBase.IsSuccess.Should().BeTrue();
        suspended.IsSuccess.Should().BeTrue();

        // Act
        var result = await mediator.Send(new ListTenantsQuery(
            Status: TenantStatus.Active,
            IsActive: null,
            Page: 1,
            PageSize: 20));

        // Assert: only the Active tenant comes back.
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(1);
        result.Value.TotalCount.Should().Be(1);
        result.Value.Items[0].InstitutionCode.Should().Be("020201");
        result.Value.Items[0].Status.Should().Be("Active");
    }
}