using FluentAssertions;
using MediatR;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using SBQR.Modules.IdentityAccess.Contracts;
using SBQR.Modules.InstitutionTrust.Contracts;
using SBQR.Modules.KeyCustody.Contracts;
using SBQR.Modules.Tenancy.Application.Commands.ActivateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Interfaces;
using SBQR.SharedKernel.Application;
using SBQR.SharedKernel.Persistence;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ActivateTenantCommandHandler"/>. The handler is
/// a state-machine entry: load the tenant, evaluate the activate-gate
/// (FR-TENANT-001), call <see cref="Tenant.Activate"/>, save, audit. It
/// must NOT cascade onto the tenant's <c>tenant_configurations</c> rows —
/// that's <c>ReactivateTenantCommandHandler</c>'s job for the
/// <c>Suspended → Active</c> path. The cascade pin is asserted below.
///
/// <para>
/// The activate-gate reads three preconditions from BC Contracts seams:
/// <c>HasActiveAsync</c> on <see cref="ITenantConfigurationProvisioner"/>
/// (IdentityAccess), <c>HasActiveSigningKeyQuery</c> (KeyCustody), and
/// <c>GetInstitutionPublicKeyQuery</c> (InstitutionTrust). The
/// <see cref="StubReadyGate"/> helper primes all three with truthy values
/// for the happy-path tests; the failing-gate tests deliberately un-prime
/// them to verify the gate is wired correctly.
/// </para>
///
/// <para>
/// Test arrangement note: <see cref="Tenant.Register(string, string)"/>
/// generates the aggregate's id internally, so the request id must match
/// the aggregate's <c>tenant.Id</c> (not a separately-created
/// <c>TenantId(Guid.NewGuid())</c>). The handler dispatches the gate
/// queries with the aggregate's id, and NSubstitute substitutes match
/// against the actual id used.
/// </para>
/// </summary>
public sealed class ActivateTenantCommandHandlerTests
{
    private readonly ITenantRepository _tenants = Substitute.For<ITenantRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IActorProvider _actor = Substitute.For<IActorProvider>();
    private readonly ITenantConfigurationProvisioner _configurations =
        Substitute.For<ITenantConfigurationProvisioner>();
    private readonly ISender _mediator = Substitute.For<ISender>();

    private ActivateTenantCommandHandler CreateSut() => new(
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
    /// Build a fresh Pending tenant (after Register) whose <c>tenant.Id</c>
    /// is what the request will carry. The handler reads the aggregate from
    /// the repo by the request's TenantId, so the request id MUST equal the
    /// aggregate's id.
    /// </summary>
    private static Tenant BuildPendingTenant() => Tenant.Register("Mutual Trust Bank", "010101");

    [Fact]
    public async Task Handle_should_activate_pending_tenant_when_gate_is_ready_and_audit()
    {
        var tenant = BuildPendingTenant();
        var tenantId = tenant.Id; // Use the aggregate's actual id for the request.
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        StubReadyGate(tenant);

        var sut = CreateSut();

        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantId.Should().Be(tenant.Id);
        result.Value.Status.Should().Be(TenantStatus.Active);
        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.IsActive.Should().BeTrue();

        await _tenants.Received(1).UpdateAsync(tenant, Arg.Any<CancellationToken>());
        await _uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        await _audit.Received(1).LogAsync(
            Arg.Is<AuditEntry>(e =>
                e.Action == "tenant.activated"
                && e.ActorId == "ops-bob"
                && e.ResourceType == "Tenant"
                && e.ResourceId == tenant.Id.Value.ToString()
                && e.Metadata!.Contains("\"institution_code\":\"010101\"")),
            Arg.Any<CancellationToken>());

        // Gate contracts — each seam was consulted exactly once for this request.
        await _configurations.Received(1).HasActiveAsync(tenantId.Value, Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<HasActiveSigningKeyQuery>(q => q.TenantId == tenantId.Value),
            Arg.Any<CancellationToken>());
        await _mediator.Received(1).Send(
            Arg.Is<GetInstitutionPublicKeyQuery>(q => q.InstitutionCode == "010101"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_activate_suspended_tenant_without_firing_credential_cascade()
    {
        // Pin the contract: Activate is NOT a suspend-reversal. A Suspended
        // tenant can be activated via this endpoint, but the credential/key
        // state stays Suspended (those are owned by the dedicated endpoints).
        var tenant = BuildPendingTenant();
        tenant.Suspend(actor: "ops-bob", reason: "test"); // -> Suspended
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        StubReadyGate(tenant);

        var sut = CreateSut();

        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(TenantStatus.Active);

        // The configuration seam was NOT touched for cascades. This is the
        // contract pin (the gate-read HasActiveAsync was, but that's not a
        // cascade). We assert the cascade methods by name.
        await _configurations.DidNotReceive().SuspendAllForTenantAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _configurations.DidNotReceive().ReinstateAllForTenantAsync(
            Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_tenant_is_terminated()
    {
        // Tenant.Deactivate from Active moves to Terminated; calling Activate
        // on a Terminated tenant is the documented "not allowed from a
        // terminal state" path.
        var tenant = BuildPendingTenant();
        tenant.Deactivate(actor: "ops-bob", reason: "test"); // -> Terminated
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        StubReadyGate(tenant);

        var sut = CreateSut();

        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        // The readiness summary rides through the InvalidOperationException's
        // message; the "Terminated" guard message is part of that exception
        // because the gate fails first (HasActiveAsync returns false), so
        // we accept either the gate-readiness summary OR the "Terminated"
        // text. The simplest invariant is: an InvariantViolation, no state
        // mutation, no persistence, no audit.
        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        tenant.Status.Should().Be(TenantStatus.Terminated);
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_gate_fails_no_credential()
    {
        // Gate fails: HasActiveAsync returns false (default NSubstitute).
        // The handler must refuse the transition before reaching persistence
        // or audit. Tenant stays Pending.
        var tenant = BuildPendingTenant();
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        // Note: _configurations.HasActiveAsync NOT primed -> returns false.
        // The other two gate reads are also unprimed but won't be evaluated
        // since Tenant.Activate short-circuits on the readiness check.

        var sut = CreateSut();
        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        tenant.Status.Should().Be(TenantStatus.Pending);

        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_gate_fails_no_signing_key()
    {
        // Gate fails: HasActiveSigningKeyQuery returns false. HasActiveAsync
        // is true (we prime it); the trust-entry is true. Only the signing-key
        // gate blocks.
        var tenant = BuildPendingTenant();
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        _configurations.HasActiveAsync(tenantId.Value, Arg.Any<CancellationToken>()).Returns(true);
        // HasActiveSigningKeyQuery returns false (NSubstitute default).
        _mediator.Send(Arg.Is<GetInstitutionPublicKeyQuery>(q => q.InstitutionCode == tenant.InstitutionCode),
                       Arg.Any<CancellationToken>())
            .Returns(new InstitutionPublicKeyView(
                InstitutionCode: tenant.InstitutionCode,
                KeyVersion: 1,
                PublicKeyPem: "-----BEGIN PUBLIC KEY-----STUB-----END PUBLIC KEY-----",
                Status: "Active"));

        var sut = CreateSut();
        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        tenant.Status.Should().Be(TenantStatus.Pending);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_InvariantViolation_when_gate_fails_no_trust_entry()
    {
        // Gate fails: GetInstitutionPublicKeyQuery returns null (no ACTIVE row).
        var tenant = BuildPendingTenant();
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        _configurations.HasActiveAsync(tenantId.Value, Arg.Any<CancellationToken>()).Returns(true);
        _mediator.Send(Arg.Is<HasActiveSigningKeyQuery>(q => q.TenantId == tenantId.Value),
                       Arg.Any<CancellationToken>()).Returns(true);
        // GetInstitutionPublicKeyQuery returns null (NSubstitute default).

        var sut = CreateSut();
        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.InvariantViolation);
        tenant.Status.Should().Be(TenantStatus.Pending);

        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_should_return_NotFound_when_tenant_is_missing()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();

        var result = await sut.Handle(new ActivateTenantCommand(tenantId), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(ErrorCode.NotFound);
        result.ErrorMessage.Should().Contain(tenantId.Value.ToString("D"));

        await _tenants.DidNotReceive().UpdateAsync(Arg.Any<Tenant>(), Arg.Any<CancellationToken>());
        await _uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _audit.DidNotReceive().LogAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>());
        // Gate is NOT evaluated on NotFound — short-circuit before the reads.
        await _configurations.DidNotReceiveWithAnyArgs().HasActiveAsync(default, default);
    }

    [Fact]
    public async Task Handle_should_pass_cancellation_token_through()
    {
        var tenant = BuildPendingTenant();
        var tenantId = tenant.Id;
        _tenants.GetByIdAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
        _actor.CurrentActor().Returns("ops-bob");
        StubReadyGate(tenant);

        var sut = CreateSut();
        using var cts = new CancellationTokenSource();

        await sut.Handle(new ActivateTenantCommand(tenantId), cts.Token);

        await _tenants.Received(1).GetByIdAsync(tenantId, cts.Token);
        await _tenants.Received(1).UpdateAsync(tenant, cts.Token);
        await _uow.Received(1).SaveChangesAsync(cts.Token);
        await _audit.Received(1).LogAsync(Arg.Any<AuditEntry>(), cts.Token);
    }
}
