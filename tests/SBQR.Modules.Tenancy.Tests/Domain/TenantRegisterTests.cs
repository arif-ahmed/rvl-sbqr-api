using FluentAssertions;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using SBQR.Modules.Tenancy.Domain.Events;
using SBQR.SharedKernel.Domain;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Domain;

/// <summary>
/// Pure-domain unit tests for the <see cref="Tenant"/> aggregate root. Cover
/// <list type="bullet">
///   <item>The static <see cref="Tenant.Register"/> factory: defaults,
///         required <c>institution_code</c>, and the
///         <see cref="TenantRegistered"/> event.</item>
///   <item>Format validation for <c>institution_code</c>.</item>
///   <item>The full lifecycle state machine — every allowed transition, plus
///         all forbidden self-transitions and forbidden terminal transitions.</item>
/// </list>
/// These tests never touch EF Core, the DbContext, the mediator, or the
/// HTTP stack. They are the safety net the rest of the module builds on.
/// </summary>
public sealed class TenantRegisterTests
{
    // ----- Register() -----------------------------------------------------

    [Fact]
    public void Register_should_create_tenant_in_pending_status_with_active_flag()
    {
        var tenant = Tenant.Register(institutionName: "Mutual Trust Bank", institutionCode: "010101");

        tenant.Status.Should().Be(TenantStatus.Pending);
        tenant.IsActive.Should().BeTrue();
        tenant.InstitutionName.Should().Be("Mutual Trust Bank");
        tenant.InstitutionCode.Should().Be("010101");
    }

    [Fact]
    public void Register_should_assign_a_non_empty_unique_tenant_id()
    {
        var t1 = Tenant.Register("Mutual Trust Bank", "010101");
        var t2 = Tenant.Register("Nagad", "020202");

        t1.Id.Should().NotBe(default(TenantId));
        t1.Id.Value.Should().NotBe(Guid.Empty);
        t1.Id.Should().NotBe(t2.Id, "each Register() call must mint a fresh Guid.");
    }

    [Fact]
    public void Register_should_raise_exactly_one_TenantRegistered_event()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");

        var registered = tenant.DomainEvents.OfType<TenantRegistered>().Single();
        registered.InstitutionName.Should().Be("Mutual Trust Bank");
        registered.InstitutionCode.Should().Be("010101");
        registered.TenantId.Should().Be(tenant.Id);
        registered.OccurredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_should_throw_when_institution_name_is_null_or_whitespace(string? name)
    {
        var act = () => Tenant.Register(name!, "010101");

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_should_throw_when_institution_code_is_null_or_whitespace(string? code)
    {
        // institution_code is the natural unique key and is required at
        // registration (mirrors institution_registries.institution_code).
        var act = () => Tenant.Register("Some Bank", code!);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("010101")]
    [InlineData("999999")]
    public void Register_should_accept_six_digit_institution_code(string good)
    {
        var act = () => Tenant.Register("Mutual Trust Bank", good);

        act.Should().NotThrow();
    }

    // ----- InstitutionCode immutability -----------------------------------

    [Fact]
    public void InstitutionCode_should_have_no_public_setter()
    {
        typeof(Tenant)
            .GetProperty(nameof(Tenant.InstitutionCode))!
            .GetSetMethod(nonPublic: false)
            .Should()
            .BeNull("InstitutionCode is the natural unique key and is immutable for the aggregate's lifetime.");
    }

    // ----- Activate() -----------------------------------------------------

    [Fact]
    public void Activate_should_move_pending_tenant_to_active_and_raise_event()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");

        tenant.Activate(actor: "platform-staff");

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.IsActive.Should().BeTrue();
        var activated = tenant.DomainEvents.OfType<TenantActivated>().Single();
        activated.TenantId.Should().Be(tenant.Id);
        activated.Actor.Should().Be("platform-staff");
    }

    [Fact]
    public void Activate_should_move_suspended_tenant_back_to_active()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");
        tenant.Suspend("platform-staff", reason: "KYC review");
        tenant.ClearDomainEvents();

        tenant.Activate("platform-staff");

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.DomainEvents.OfType<TenantActivated>().Should().HaveCount(1);
    }

    [Fact]
    public void Activate_on_already_active_tenant_must_not_be_idempotent()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");

        var act = () => tenant.Activate("platform-staff");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Active*");
    }

    [Fact]
    public void Activate_on_terminated_tenant_should_throw()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", reason: "offboarded");

        var act = () => tenant.Activate("platform-staff");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Terminated*");
    }

    // ----- Suspend() ------------------------------------------------------

    [Fact]
    public void Suspend_should_move_pending_or_active_tenant_to_suspended()
    {
        var pending = Tenant.Register("Mutual Trust Bank", "010101");
        pending.Suspend("platform-staff", reason: "KYC review");
        pending.Status.Should().Be(TenantStatus.Suspended);

        var active = Tenant.Register("Nagad", "020202");
        active.Activate("platform-staff");
        active.Suspend("platform-staff", reason: "watchlist hit");
        active.Status.Should().Be(TenantStatus.Suspended);

        var suspendedEvent = active.DomainEvents.OfType<TenantSuspended>().Single();
        suspendedEvent.Actor.Should().Be("platform-staff");
        suspendedEvent.Reason.Should().Be("watchlist hit");
    }

    [Fact]
    public void Suspend_on_suspended_tenant_must_not_be_idempotent()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Suspend("platform-staff", "KYC review");

        var act = () => tenant.Suspend("platform-staff", "duplicate");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Suspended*");
    }

    [Fact]
    public void Suspend_on_terminated_tenant_should_throw()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", "offboarded");

        var act = () => tenant.Suspend("platform-staff", "too late");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Terminated*");
    }

    // ----- Deactivate() ---------------------------------------------------

    [Fact]
    public void Deactivate_should_set_status_terminated_and_is_active_false_atomically()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Activate("platform-staff");

        tenant.Deactivate("platform-staff", reason: "offboarded");

        tenant.Status.Should().Be(TenantStatus.Terminated);
        tenant.IsActive.Should().BeFalse("Deactivate must move both Status and IsActive atomically.");
        var deactivated = tenant.DomainEvents.OfType<TenantDeactivated>().Single();
        deactivated.Reason.Should().Be("offboarded");
    }

    [Fact]
    public void Deactivate_should_be_allowed_from_pending_and_suspended_too()
    {
        var pending = Tenant.Register("Mutual Trust Bank", "010101");
        pending.Deactivate("platform-staff", "rejected");
        pending.Status.Should().Be(TenantStatus.Terminated);
        pending.IsActive.Should().BeFalse();

        var suspended = Tenant.Register("Nagad", "020202");
        suspended.Activate("platform-staff");
        suspended.Suspend("platform-staff", "KYC");
        suspended.Deactivate("platform-staff", "rejected");
        suspended.Status.Should().Be(TenantStatus.Terminated);
        suspended.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Deactivate_on_terminated_tenant_must_not_be_idempotent()
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");
        tenant.Deactivate("platform-staff", "first");

        var act = () => tenant.Deactivate("platform-staff", "second");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*already Terminated*");
    }

    [Fact]
    public void Deactivate_must_not_throw_on_fresh_pending_tenant()
    {
        // Deactivate is allowed from any non-terminal state, including PENDING.
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");

        var act = () => tenant.Deactivate("platform-staff", "rejected before activation");

        act.Should().NotThrow();
    }

    // ----- Argument-validation per lifecycle method -----------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Lifecycle_methods_should_reject_blank_actor(string? actor)
    {
        var tenant = Tenant.Register("Mutual Trust Bank", "010101");

        var activate = () => tenant.Activate(actor!);
        var suspend = () => tenant.Suspend(actor!, reason: null);
        var deactivate = () => tenant.Deactivate(actor!, reason: null);

        activate.Should().Throw<ArgumentException>();
        suspend.Should().Throw<ArgumentException>();
        deactivate.Should().Throw<ArgumentException>();
    }
}
