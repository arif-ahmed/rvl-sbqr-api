using FluentAssertions;
using FluentValidation.TestHelper;
using SBQR.Modules.Tenancy.Application.Commands.ReactivateTenant;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Pure-validator tests for <see cref="ReactivateTenantValidator"/>. Stateful
/// rules (tenant exists, current state is Suspended) are NOT declared here —
/// the handler enforces them via the repository pre-check.
/// </summary>
public sealed class ReactivateTenantValidatorTests
{
    private readonly ReactivateTenantValidator _sut = new();

    [Fact]
    public void Should_pass_when_tenant_id_is_set()
    {
        var result = _sut.TestValidate(new ReactivateTenantCommand(
            TenantId: new TenantId(Guid.NewGuid())));

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Should_fail_when_tenant_id_is_empty()
    {
        var result = _sut.TestValidate(new ReactivateTenantCommand(
            TenantId: default));

        result.ShouldHaveValidationErrorFor(c => c.TenantId);
    }
}
