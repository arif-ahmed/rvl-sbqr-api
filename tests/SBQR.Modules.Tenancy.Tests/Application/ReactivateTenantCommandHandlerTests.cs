using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ReactivateTenantCommandHandler"/>. Mirrors the
/// structure of <see cref="SuspendTenantCommandHandlerTests"/>: covers happy
/// path, NotFound, and InvariantViolation on self-transition / terminal
/// state. The configuration reinstate cascade fires through the
/// <see cref="ITenantConfigurationProvisioner.ReinstateAllForTenantAsync"/> seam
/// (IdentityAccess owns the tenant_configurations aggregate now); the
/// signing-key cascade fires through
/// <see cref="ReinstateTenantSigningKeysCommand"/> (KeyCustody) — what that
/// cascade actually does (tolerant no-op vs. reinstate) is KeyCustody's own
/// responsibility, covered by its own test suite.
///
/// <para>
/// The handler evaluates the same activate-gate (FR-TENANT-001) as
/// <see cref="ActivateTenant.ActivateTenantCommandHandler"/>: three
/// preconditions across IdentityAccess, KeyCustody, and InstitutionTrust
/// Contracts seams. The <see cref="StubReadyGate"/> helper primes all
/// three with truthy values; the gate-failure tests deliberately
/// un-prime them.
/// </para>
/// </summary>
public sealed class ReactivateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly ITenantConfigurationProvisioner _configurations =
        Substitute.For<ITenantConfigurationProvisioner>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    private ReactivateTenantCommandHandler CreateSut() => new(
        _tenants, _uow, _audit, _actor, _configurations, _mediator);

    /// <summary>
    /// Prime the three gate seams so the activate-gate evaluates to
    /// <c>IsReady == true</c>. Called by every happy-path test before
    /// invoking the handler.
    /// </summary>
    private void StubReadyGate(Tenant tenant)
    {
        var tenantId = tenant.Id.Value;
        var institutionCode = tenant.InstitutionCode;
        _configurations.HasActiveAsync(tenantId, Arg.Any<CancellationToken>()).Returns(true);
        _mediator.Send(Arg.Is<HasActiveSigningKeyQuery>(q => q.TenantId == tenantId),
                       Arg.Any<CancellationToken>()).Returns(true);
        _mediator.Send(Arg.Is<GetInstitutionPublicKeyQuery>(q => q.InstitutionCode == institutionCode),
                       Arg.Any<CancellationToken>())
            .Returns(new InstitutionPublicKeyView(
                InstitutionCode: institutionCode,
                KeyVersion: 1,
                PublicKeyPem: "-----BEGIN PUBLIC KEY-----STUB-----END PUBLIC KEY-----",
                Status: "Active"));
    }

    /// <summary>
    /// Build a tenant that's already gone through Suspend → ready for
    /// Reactivate. The test path mirrors the real handler's preconditions.
    /// </summary>
    private static Tenant BuildSuspendedTenant()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.Suspend("platform-staff", "KYC review");
        tenant.ClearDomainEvents();
        return tenant;
    }

    [Fact]
    public async Task Handle_should_reactivate_tenant_and_fire_both_cascades()
    {
        var tenant = BuildSuspendedTenant();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .ReinstateAllForTenantAsync(tenant.Id.Value, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ConfigurationSnapshot(CredentialId: Guid.NewGuid(), ClientId: "mtb-7c1b4d88"),
            });
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _actor.CurrentActor().Returns("ops-bob");
        StubReadyGate(tenant);

        var sut = CreateSut();
        var result = await sut.Handle(
            new ReactivateTenantCommand(tenant.Id),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(tenant.Id);
        result.Value.Status.Should().Be(TenantStatus.Active);
        tenant.Status.Should().Be(TenantStatus.Active);

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _configurations.Received(1).ReinstateAllForTenantAsync(
            Arg.Is<Guid>(g => g == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _mediator.Received(1).Send(
            Arg.Is<ReinstateTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "tenant.reactivated"
                                    && e.ActorId == "ops-bob"
                                    && e.ResourceType == "Tenant"),
            Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e => e.Action == "tenant.credentials.cascade.reinstated"
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
        var result = await sut.Handle(new ReactivateTenantCommand(missing), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(missing.Value.ToString("D"));

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().ReinstateAllForTenantAsync(default, default);
        // Gate is NOT evaluated on NotFound.
        await _configurations.DidNotReceiveWithAnyArgs().HasActiveAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_already_active()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");
        StubReadyGate(tenant);

        var sut = CreateSut();
        var result = await sut.Handle(new ReactivateTenantCommand(tenant.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("already Active");

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        // Gate-failure path must NOT fire cascades.
        await _configurations.DidNotReceiveWithAnyArgs().ReinstateAllForTenantAsync(default, default);
        await _mediator.DidNotReceive().Send(
            Arg.Any<ReinstateTenantSigningKeysCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_terminated()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", "offboarded");
        tenant.ClearDomainEvents();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");
        StubReadyGate(tenant);

        var sut = CreateSut();
        var result = await sut.Handle(new ReactivateTenantCommand(tenant.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        result.ErrorMessage.Should().Contain("Terminated");
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_gate_fails()
    {
        // Gate fails: HasActiveAsync returns false (default NSubstitute).
        // The Suspended tenant stays Suspended; no cascades fire.
        var tenant = BuildSuspendedTenant();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("platform-staff");
        // _configurations.HasActiveAsync NOT primed -> returns false.

        var sut = CreateSut();
        var result = await sut.Handle(new ReactivateTenantCommand(tenant.Id), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        tenant.Status.Should().Be(TenantStatus.Suspended);

        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceiveWithAnyArgs().ReinstateAllForTenantAsync(default, default);
        await _mediator.DidNotReceive().Send(
            Arg.Any<ReinstateTenantSigningKeysCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_skip_credential_breadcrumb_when_none_cascaded()
    {
        var tenant = BuildSuspendedTenant();

        _tenants.GetByIdAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _configurations
            .ReinstateAllForTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ConfigurationSnapshot>());
        _uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        _actor.CurrentActor().Returns("platform-staff");
        StubReadyGate(tenant);

        var sut = CreateSut();
        var result = await sut.Handle(new ReactivateTenantCommand(tenant.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        tenant.Status.Should().Be(TenantStatus.Active);

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<ReinstateTenantSigningKeysCommand>(c => c.TenantId == tenant.Id.Value),
            Arg.Any<CancellationToken>());
        await _audit.Received(1).LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }
}
