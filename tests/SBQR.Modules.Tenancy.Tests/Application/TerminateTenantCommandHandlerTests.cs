using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.TerminateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="TerminateTenantCommandHandler"/>. Mirrors
/// <see cref="SuspendTenantCommandHandlerTests"/> line-for-line with the
/// terminal-state substitutions: <c>Tenant.Deactivate</c> instead of
/// <c>Tenant.Suspend</c>, <c>tenant.terminated</c> instead of
/// <c>tenant.suspended</c>, and <see cref="TenantStatus.Terminated"/> as the
/// expected post-transition status.
///
/// <para>One-way trip: re-terminating throws and surfaces 409. The cascade
/// shape is identical to Suspend — the signing-key cascade fires through
/// <see cref="SuspendTenantSigningKeysCommand"/> (KeyCustody), and
/// <see cref="ITenantConfigurationProvisioner.SuspendAllForTenantAsync"/> fires
/// through the IdentityAccess Contracts seam.</para>
/// </summary>
public sealed class TerminateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly ITenantConfigurationProvisioner _configurations =
        Substitute.For<ITenantConfigurationProvisioner>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    private TerminateTenantCommandHandler CreateSut() => new(
        _tenants, _uow, _audit, _actor, _configurations, _mediator);

    [Fact]
    public async Task Handle_should_terminate_tenant_and_fire_both_cascades()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .SuspendAllForTenantAsync(tenant.Id.Value, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ConfigurationSnapshot(CredentialId: Guid.NewGuid(), ClientId: "mtb-7c1b4d88"),
            });
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _actor.CurrentActor().Returns("ops-bob");

        var sut = CreateSut();
        var result = await sut.Handle(
            new TerminateTenantCommand(tenant.Id, Reason: "Offboarded"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(tenant.Id);
        result.Value.Status.Should().Be(TenantStatus.Terminated);

        // Terminal state: Status=Terminated AND IsActive=false, atomically.
        tenant.Status.Should().Be(TenantStatus.Terminated);
        tenant.IsActive.Should().BeFalse();

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _configurations.Received(1).SuspendAllForTenantAsync(
            Arg.Is<Guid>(g => g == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _mediator.Received(1).Send(
            Arg.Is<SuspendTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "tenant.terminated"
                                    && e.ActorId == "ops-bob"
                                    && e.ResourceType == "Tenant"
                                    && e.Metadata!.Contains("Offboarded")),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "tenant.credentials.cascade.suspended"
                                    && e.ResourceType == "Tenant"
                                    && e.Metadata!.Contains("\"count\":1")),
            Arg.Any<CancellationToken>());

        await _audit.Received(2).LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_NotFound_when_tenant_is_missing()
    {
        var missing = new TenantId(Guid.NewGuid());
        _tenants.GetByIdAsync(missing, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();
        var result = await sut.Handle(new TerminateTenantCommand(missing), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(missing.Value.ToString("D"));

        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().SuspendAllForTenantAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_already_terminated()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", "first offboarding");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new TerminateTenantCommand(tenant.Id, Reason: "duplicate"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("already Terminated");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().SuspendAllForTenantAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_pass_cancellation_token_through_to_all_dependencies()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _actor.CurrentActor().Returns("ops-bob");

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        await sut.Handle(new TerminateTenantCommand(tenant.Id, Reason: null), cts.Token);

        await _tenants.Received(1).GetByIdAsync(tenant.Id, cts.Token);
        await _tenants.Received(1).UpdateAsync(tenant, cts.Token);
        await _uow.Received(1).SaveChangesAsync(cts.Token);
        await _configurations.Received(1).SuspendAllForTenantAsync(tenant.Id.Value, cts.Token);
        await _mediator.Received(1).Send(
            Arg.Is<SuspendTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            cts.Token);
    }
}
