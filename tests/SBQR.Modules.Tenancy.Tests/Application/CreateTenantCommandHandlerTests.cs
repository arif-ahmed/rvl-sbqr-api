using FluentAssertions;
using NSubstitute;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Events;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="CreateTenantCommandHandler"/>. The handler is a
/// minimal single-table insert: build the <see cref="Tenant"/> aggregate via
/// <c>Tenant.Register</c>, persist it through <see cref="ITenantRepository"/>,
/// commit via <see cref="IUnitOfWork"/>, return the new id.
///
/// <para>
/// Client-credential issuance and cryptographic signing-key generation are out
/// of scope — both live behind dedicated endpoints. The audit trail rides on
/// the row's own <c>created_by</c> / <c>created_at</c> columns, stamped by
/// <c>TenancyAuditColumnInterceptor</c>, so the handler does not call
/// <see cref="IAuditLogger"/> directly.
/// </para>
/// </summary>
public sealed class CreateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    private CreateTenantCommandHandler CreateSut() => new(_tenants, _uow);

    [Fact]
    public async Task Handle_should_persist_tenant_and_return_new_id()
    {
        var sut = CreateSut();
        var cmd = new CreateTenantCommand(
            InstitutionName: "Mutual Trust Bank",
            InstitutionCode: "010101");

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.TenantId.Value.Should().NotBe(Guid.Empty);

        await _tenants.Received(1).AddAsync(
            Arg.Is<Tenant>(t =>
                t.InstitutionName == "Mutual Trust Bank"
                && t.InstitutionCode == "010101"
                && t.Status == TenantStatus.Pending
                && t.IsActive),
            Arg.Any<CancellationToken>());

        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_use_TenantRegister_factory_internals()
    {
        // The Tenant.Register factory is the only place TenantRegistered fires;
        // if the handler ever constructs Tenant via a different path, the
        // domain event would be missing. Pin that contract here.
        var sut = CreateSut();
        var cmd = new CreateTenantCommand("Mutual Trust Bank", "010101");

        Tenant? persisted = null;
        await _tenants.AddAsync(Arg.Do<Tenant>(t => persisted = t), Arg.Any<CancellationToken>());

        await sut.Handle(cmd, CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.DomainEvents.OfType<TenantRegistered>().Should().HaveCount(1);
        persisted.Status.Should().Be(TenantStatus.Pending);
        persisted.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_should_pass_cancellation_token_to_add_and_save()
    {
        var sut = CreateSut();
        var cmd = new CreateTenantCommand("Mutual Trust Bank", "010101");

        using var cts = new CancellationTokenSource();
        await sut.Handle(cmd, cts.Token);

        await _tenants.Received(1).AddAsync(Arg.Any<Tenant>(), cts.Token);
        await _uow.Received(1).SaveChangesAsync(cts.Token);
    }

    [Fact]
    public async Task Handle_should_not_throw_when_input_is_valid()
    {
        // Minimal smoke: valid input returns success and stays side-effect-free
        // beyond a single AddAsync + SaveChangesAsync.
        var sut = CreateSut();
        var cmd = new CreateTenantCommand("Mutual Trust Bank", "010101");

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Value.Should().NotBe(Guid.Empty);
    }
}