using FluentAssertions;
using NSubstitute;
using SBQR.Modules.Tenancy.Application.Queries.ListTenants;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ListTenantsQueryHandler"/>. The handler is a
/// pure projection: load a page of aggregates via
/// <see cref="ITenantRepository.ListAsync"/>, map to
/// <see cref="Application.Contracts.TenantResponse"/>, wrap in
/// <see cref="Application.Contracts.PagedTenantResponse"/>.
///
/// <para>
/// We pin the repository's contract here by stubbing the list call to return
/// pre-built aggregates, so the test asserts the handler's projection +
/// paging-math logic (not the EF Core query translation).
/// </para>
/// </summary>
public sealed class ListTenantsQueryHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();

    private ListTenantsQueryHandler CreateSut() => new(_tenants);

    [Fact]
    public async Task Handle_should_return_paged_results_with_total_count()
    {
        // Arrange: 2 tenants on page 1, total count 25 (so hasMore=true —
        // 1*20 = 20 < 25).
        var pageItems = new[]
        {
            Tenant.Register("Acme Bank Ltd", "010100"),
            Tenant.Register("Mutual Trust Bank", "010101"),
        };
        _tenants
            .ListAsync(Arg.Any<TenantListQuery>(), Arg.Any<CancellationToken>())
            .Returns((pageItems, 25));

        var sut = CreateSut();
        var query = new ListTenantsQuery(Status: null, IsActive: null, Page: 1, PageSize: 20);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert: success + projection + paging math.
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Page.Should().Be(1);
        result.Value.PageSize.Should().Be(20);
        result.Value.TotalCount.Should().Be(25);
        result.Value.HasMore.Should().BeTrue();
        result.Value.Items[0].InstitutionCode.Should().Be("010100");
        result.Value.Items[1].InstitutionCode.Should().Be("010101");
    }

    [Fact]
    public async Task Handle_should_pass_filters_through_to_repository()
    {
        // Pin that the filter parameters are forwarded to the repository
        // unchanged — Status, IsActive, Page, PageSize all flow through.
        _tenants
            .ListAsync(Arg.Any<TenantListQuery>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Tenant>(), 0));

        var sut = CreateSut();
        var query = new ListTenantsQuery(
            Status: TenantStatus.Suspended,
            IsActive: false,
            Page: 3,
            PageSize: 50);

        await sut.Handle(query, CancellationToken.None);

        await _tenants.Received(1).ListAsync(
            Arg.Is<TenantListQuery>(q =>
                q.Status == TenantStatus.Suspended
                && q.IsActive == false
                && q.Page == 3
                && q.PageSize == 50),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_HasMore_false_on_last_page()
    {
        // Arrange: 1 item on page 3 of 3, total count 5.
        var pageItems = new[] { Tenant.Register("Last Bank", "030303") };
        _tenants
            .ListAsync(Arg.Any<TenantListQuery>(), Arg.Any<CancellationToken>())
            .Returns((pageItems, 5));

        var sut = CreateSut();
        var query = new ListTenantsQuery(Status: null, IsActive: null, Page: 3, PageSize: 2);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert: page 3 × size 2 = 6 ≥ 5, so HasMore is false.
        result.IsSuccess.Should().BeTrue();
        result.Value.Page.Should().Be(3);
        result.Value.PageSize.Should().Be(2);
        result.Value.TotalCount.Should().Be(5);
        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_should_return_empty_page_when_no_matches()
    {
        _tenants
            .ListAsync(Arg.Any<TenantListQuery>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Tenant>(), 0));

        var sut = CreateSut();

        var result = await sut.Handle(
            new ListTenantsQuery(Status: TenantStatus.Active, IsActive: null, Page: 1, PageSize: 20),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
        result.Value.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_should_pass_cancellation_token_to_repository()
    {
        _tenants
            .ListAsync(Arg.Any<TenantListQuery>(), Arg.Any<CancellationToken>())
            .Returns((Array.Empty<Tenant>(), 0));

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        await sut.Handle(new ListTenantsQuery(null, null, 1, 20), cts.Token);

        await _tenants.Received(1).ListAsync(Arg.Any<TenantListQuery>(), cts.Token);
    }
}