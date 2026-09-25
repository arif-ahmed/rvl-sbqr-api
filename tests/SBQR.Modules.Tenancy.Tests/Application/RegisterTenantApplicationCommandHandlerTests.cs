using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SBQR.Modules.Tenancy.Application.Commands.RegisterTenantApplication;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Events;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="RegisterTenantApplicationCommandHandler"/>.
///
/// <para>
/// Contract pinned by these tests:
/// </para>
/// <list type="number">
///   <item>Unknown owning tenant → <see cref="ErrorCode.NotFound"/>.</item>
///   <item>Happy path → builds a <see cref="TenantApplication"/> via
///         <see cref="TenantApplication.Register"/> in
///         <see cref="TenantApplicationStatus.Active"/>, raises exactly one
///         <see cref="TenantApplicationRegistered"/> domain event, persists
///         through <see cref="ITenantApplicationRepository"/>, commits via
///         <see cref="IUnitOfWork"/>.</item>
///   <item>Actor is sourced from <see cref="IActorProvider.CurrentActor"/>.</item>
///   <item>Non-unique-violation exceptions from <see cref="IUnitOfWork"/> are
///         NOT swallowed (the production handler only catches PG SQLSTATE 23505).
///         The 23505 → <see cref="ErrorCode.InvariantViolation"/> translation is
///         covered end-to-end by tests/SBQR.Tenancy.IntegrationTests/
///         TenantApplications because simulating an Npgsql exception here would
///         require a direct Npgsql reference that this project avoids.</item>
///   <item>Cancellation tokens are propagated to GetByIdAsync, AddAsync and
///         SaveChangesAsync.</item>
/// </list>
/// </summary>
public sealed class RegisterTenantApplicationCommandHandlerTests
{
    private const string TestActor = "platform:bootstrap";

    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly ITenantApplicationRepository _applications = Substitute.For<ITenantApplicationRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();

    private RegisterTenantApplicationCommandHandler CreateSut()
    {
        _actor.CurrentActor().Returns(TestActor);
        return new RegisterTenantApplicationCommandHandler(_tenants, _applications, _uow, _actor);
    }

    private static Tenant ExistingTenant() => Tenant.Register(
        institutionName: "Mutual Trust Bank",
        institutionCode: "010101");

    [Fact]
    public async Task Handle_should_return_NotFound_when_tenant_does_not_exist()
    {
        var sut = CreateSut();
        var cmd = new RegisterTenantApplicationCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            Platform: TenantApplicationPlatform.Android,
            PackageId: "com.example.bank");

        _tenants.GetByIdAsync(cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns((Tenant?)null);

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(cmd.TenantId.Value.ToString("D"));

        await _applications.DidNotReceiveWithAnyArgs()
            .AddAsync(default!, default);
        await _uow.DidNotReceiveWithAnyArgs()
            .SaveChangesAsync(default);
    }

    [Fact]
    public async Task Handle_should_persist_active_row_and_raise_registered_event()
    {
        var sut = CreateSut();
        var tenant = ExistingTenant();
        var cmd = new RegisterTenantApplicationCommand(
            TenantId: tenant.Id,
            Platform: TenantApplicationPlatform.Android,
            PackageId: "com.dhakabank.consumer");

        _tenants.GetByIdAsync(cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);

        TenantApplication? persisted = null;
        await _applications.AddAsync(Arg.Do<TenantApplication>(a => persisted = a), Arg.Any<CancellationToken>());

        var result = await sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(cmd.TenantId);
        result.Value.Platform.Should().Be(TenantApplicationPlatform.Android);
        result.Value.PackageId.Should().Be("com.dhakabank.consumer");

        await _applications.Received(1)
            .AddAsync(Arg.Any<TenantApplication>(), Arg.Any<CancellationToken>());
        await _uow.Received(1)
            .SaveChangesAsync(Arg.Any<CancellationToken>());

        persisted.Should().NotBeNull();
        persisted!.Status.Should().Be(TenantApplicationStatus.Active);
        persisted.IsActive.Should().BeTrue();
        persisted.DomainEvents.OfType<TenantApplicationRegistered>()
            .Should().HaveCount(1);
    }

    [Fact]
    public async Task Handle_should_source_actor_from_ActorProvider()
    {
        var sut = CreateSut();
        var tenant = ExistingTenant();
        var cmd = new RegisterTenantApplicationCommand(
            TenantId: tenant.Id,
            Platform: TenantApplicationPlatform.Ios,
            PackageId: "com.dhakabank.consumer");

        _tenants.GetByIdAsync(cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);

        TenantApplication? persisted = null;
        await _applications.AddAsync(Arg.Do<TenantApplication>(a => persisted = a), Arg.Any<CancellationToken>());

        await sut.Handle(cmd, CancellationToken.None);

        _actor.Received(1).CurrentActor();
        persisted!.DomainEvents.OfType<TenantApplicationRegistered>().Single()
            .Actor.Should().Be(TestActor);
    }

    [Fact]
    public async Task Handle_should_propagate_non_unique_violation_exceptions()
    {
        // A non-23505 exception must NOT be swallowed as InvariantViolation —
        // only the PG unique-constraint violation translates. Otherwise the
        // admin endpoint would 409 on real database faults. The 23505 path is
        // covered end-to-end by tests/SBQR.Tenancy.IntegrationTests/
        // TenantApplications (it cannot be unit-tested without referencing
        // Npgsql, which Tenancy.Application deliberately avoids).
        var sut = CreateSut();
        var tenant = ExistingTenant();
        var cmd = new RegisterTenantApplicationCommand(
            TenantId: tenant.Id,
            Platform: TenantApplicationPlatform.Android,
            PackageId: "com.dhakabank.consumer");

        _tenants.GetByIdAsync(cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);

        _uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("connection dropped"));

        var act = async () => await sut.Handle(cmd, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("connection dropped");
    }

    [Fact]
    public async Task Handle_should_propagate_cancellation_token_to_repo_and_uow()
    {
        var sut = CreateSut();
        var tenant = ExistingTenant();
        var cmd = new RegisterTenantApplicationCommand(
            TenantId: tenant.Id,
            Platform: TenantApplicationPlatform.Android,
            PackageId: "com.dhakabank.consumer");

        _tenants.GetByIdAsync(cmd.TenantId, Arg.Any<CancellationToken>())
            .Returns(tenant);

        using var cts = new CancellationTokenSource();

        await sut.Handle(cmd, cts.Token);

        await _tenants.Received(1).GetByIdAsync(cmd.TenantId, cts.Token);
        await _applications.Received(1)
            .AddAsync(Arg.Any<TenantApplication>(), cts.Token);
        await _uow.Received(1).SaveChangesAsync(cts.Token);
    }
}
