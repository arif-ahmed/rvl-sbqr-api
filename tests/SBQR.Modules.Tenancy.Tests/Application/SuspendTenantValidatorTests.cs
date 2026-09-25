using FluentAssertions;
using FluentValidation.TestHelper;
using SBQR.Modules.Tenancy.Application.Commands.SuspendTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Pure-validator tests for <see cref="SuspendTenantValidator"/>. Stateful
/// rules (tenant exists, current state is non-terminal) are NOT declared
/// here — the handler enforces them via the repository pre-check.
/// </summary>
public sealed class SuspendTenantValidatorTests
{
    private readonly SuspendTenantValidator _sut = new();

    [Fact]
    public void Should_pass_when_tenant_id_is_set_and_reason_is_null()
    {
        var result = _sut.TestValidate(new SuspendTenantCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            Reason: null));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_pass_when_reason_is_at_max_length()
    {
        var result = _sut.TestValidate(new SuspendTenantCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            Reason: new string('a', 500)));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_fail_when_tenant_id_is_empty()
    {
        var result = _sut.TestValidate(new SuspendTenantCommand(
            TenantId: default,
            Reason: null));

        result.ShouldHaveValidationErrorFor(c => c.TenantId);
    }

    [Fact]
    public void Should_fail_when_reason_exceeds_500_chars()
    {
        var result = _sut.TestValidate(new SuspendTenantCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            Reason: new string('a', 501)));

        result.ShouldHaveValidationErrorFor(c => c.Reason);
    }
}
