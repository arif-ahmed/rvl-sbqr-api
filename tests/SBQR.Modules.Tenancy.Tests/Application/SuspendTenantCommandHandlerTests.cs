using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="SuspendTenantCommandHandler"/>. Covers:
/// <list type="bullet">
///   <item>Happy path: the tenant transitions to Suspended; the configuration
///         cascade fires through the
///         <see cref="ITenantConfigurationProvisioner.SuspendAllForTenantAsync"/>
///         seam (IdentityAccess owns the tenant_configurations aggregate now);
///         the signing-key cascade fires through
///         <see cref="SuspendTenantSigningKeysCommand"/> (KeyCustody); the
///         UoW commits once; one tenant-level audit row + a cascade breadcrumb
///         when at least one configuration was affected.</item>
///   <item><see cref="ErrorCode.NotFound"/> when the tenant row is missing.</item>
///   <item><see cref="ErrorCode.InvariantViolation"/> when the tenant is
///         already Suspended (self-transition) or Terminated.</item>
/// </list>
/// The signing-key cascade itself (tolerant no-op vs. suspend) is KeyCustody's
/// own responsibility now — covered by KeyCustody's test suite — so these
/// tests only assert that Tenancy dispatches the command, not what it does.
/// </summary>
public sealed class SuspendTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly ITenantConfigurationProvisioner _configurations =
        Substitute.For<ITenantConfigurationProvisioner>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    private SuspendTenantCommandHandler CreateSut() => new(
        _tenants, _uow, _audit, _actor, _configurations, _mediator);

    [Fact]
    public async Task Handle_should_suspend_tenant_and_fire_both_cascades()
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
            new SuspendTenantCommand(tenant.Id, Reason: "KYC review"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(tenant.Id);
        result.Value.Status.Should().Be(TenantStatus.Suspended);
        tenant.Status.Should().Be(TenantStatus.Suspended);

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _configurations.Received(1).SuspendAllForTenantAsync(
            Arg.Is<Guid>(g => g == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _mediator.Received(1).Send(
            Arg.Is<SuspendTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "tenant.suspended"
                                    && e.ActorId == "ops-bob"
                                    && e.ResourceType == "Tenant"
                                    && e.Metadata!.Contains("KYC review")),
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
        var result = await sut.Handle(new SuspendTenantCommand(missing), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(missing.Value.ToString("D"));

        // Nothing persisted, nothing audited, no cascade fired.
        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().SuspendAllForTenantAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_already_suspended()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.Suspend("platform-staff", "first");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new SuspendTenantCommand(tenant.Id, Reason: "duplicate"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("already Suspended");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().SuspendAllForTenantAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_terminated()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", "offboarded");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new SuspendTenantCommand(tenant.Id, Reason: "too late"),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("Terminated");
    }

    [Fact]
    public async Task Handle_should_skip_credential_breadcrumb_when_none_cascaded()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .SuspendAllForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConfigurationSnapshot>());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _actor.CurrentActor().Returns("platform-staff");

        var sut = CreateSut();
        var result = await sut.Handle(
            new SuspendTenantCommand(tenant.Id, Reason: null),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Suspended);

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<SuspendTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
