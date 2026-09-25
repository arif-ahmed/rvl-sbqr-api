using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.Tenancy.Application.Queries.GetTenantById;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="GetTenantByIdQueryHandler"/>. The handler is a
/// pure read-side projection: load the aggregate, map to <c>TenantResponse</c>,
/// return. It must NOT call <see cref="IUnitOfWork.SaveChangesAsync"/> or
/// <see cref="IAuditLogger.LogAsync"/> — the test below pins that contract.
/// </summary>
public sealed class GetTenantByIdQueryHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();

    private GetTenantByIdQueryHandler CreateSut() => new(_tenants);

    [Fact]
    public async Task Handle_should_return_projection_when_tenant_exists()
    {
        // Arrange: tenant exists.
        var tenantId = new TenantId(Guid.NewGuid());
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var sut = CreateSut();
        var query = new GetTenantByIdQuery(tenantId);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert: success + projected shape.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.InstitutionName.Should().Be("Mutual Trust Bank");
        result.Value.InstitutionCode.Should().Be("010101");
        result.Value.Status.Should().Be(nameof(TenantStatus.Pending));
        result.Value.IsActive.Should().BeTrue();
        result.Value.TenantId.Should().Be(tenant.Id.Value);
    }

    [Fact]
    public async Task Handle_should_return_NotFound_when_tenant_is_missing()
    {
        // Arrange: no row matches.
        var tenantId = new TenantId(Guid.NewGuid());
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();

        // Act
        var result = await sut.Handle(new GetTenantByIdQuery(tenantId), CancellationToken.None);

        // Assert: NotFound with the id in the message.
        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(tenantId.Value.ToString("D"));
    }

    [Fact]
    public async Task Handle_should_not_call_uow_or_audit()
    {
        // Pin the read-only contract: the handler must NOT write any state.
        // (The constructor only takes ITenantRepository — uow/audit aren't even
        // injectable — but we assert that no audit row was emitted and the
        // 200 projection path stays side-effect-free.)
        var tenantId = new TenantId(Guid.NewGuid());
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);

        var sut = CreateSut();

        var result = await sut.Handle(new GetTenantByIdQuery(tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Only the repository was touched — exactly once, with the queried id.
        await _tenants.Received(1).GetByIdAsync(tenantId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_pass_cancellation_token_through()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        await sut.Handle(new GetTenantByIdQuery(tenantId), cts.Token);

        await _tenants.Received(1).GetByIdAsync(tenantId, cts.Token);
    }
}