using FluentAssertions;
using FluentValidation.TestHelper;
using SBQR.Modules.Tenancy.Application.Commands.ProvisionTenantConfiguration;
using SBQR.Modules.Tenancy.Domain.Aggregates;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ProvisionTenantConfigurationValidator"/>. The
/// validator runs inside the shared <c>ValidationBehavior&lt;,&gt;</c>
/// pipeline, so these tests pin down the exact rule set the controller
/// relies on. Stateful rules (tenant exists, is not Suspended / Terminated,
/// has no active configuration) are NOT declared here — the handler
/// enforces them via the repository pre-check and the provisioner's own
/// single-active-configuration guard, because <c>IValidator</c> rules must
/// be pure.
/// </summary>
public sealed class ProvisionTenantConfigurationValidatorTests
{
    private readonly ProvisionTenantConfigurationValidator _sut = new();

    // -- TenantId ----------------------------------------------------------

    [Fact]
    public void Empty_TenantId_should_fail()
    {
        var cmd = new ProvisionTenantConfigurationCommand(TenantId: default);

        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(c => c.TenantId);
    }

    [Fact]
    public void Non_empty_TenantId_should_pass()
    {
        var cmd = new ProvisionTenantConfigurationCommand(TenantId: new TenantId(Guid.NewGuid()));

        _sut.TestValidate(cmd).ShouldNotHaveValidationErrorFor(c => c.TenantId);
    }

    // -- At least one QR capability ---------------------------------------

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Valid_command_with_at_least_one_capability_should_pass(
        bool isQrGenerationAllowed, bool isQrValidationAllowed)
    {
        var cmd = new ProvisionTenantConfigurationCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            IsQrGenerationAllowed: isQrGenerationAllowed,
            IsQrValidationAllowed: isQrValidationAllowed);

        _sut.TestValidate(cmd).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Both_capabilities_false_should_fail_with_at_least_one_message()
    {
        // A tenant_configurations row minted with both flags = false
        // authorises zero QR flows, so the credential can never be used at
        // the call site. Reject up front instead of producing a row the
        // platform can do nothing with.
        var cmd = new ProvisionTenantConfigurationCommand(
            TenantId: new TenantId(Guid.NewGuid()),
            IsQrGenerationAllowed: false,
            IsQrValidationAllowed: false);

        var result = _sut.TestValidate(cmd);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e =>
            e.ErrorMessage.Contains("At least one of 'isQrGenerationAllowed' or 'isQrValidationAllowed'"));
    }

    [Fact]
    public void Omitting_capabilities_defaults_to_both_true_and_passes()
    {
        // Backwards-compat: pre-capability callers send a 1-arg command. The
        // default values (true, true) must satisfy the at-least-one
        // validator without any explicit choice from the caller.
        var cmd = new ProvisionTenantConfigurationCommand(TenantId: new TenantId(Guid.NewGuid()));

        _sut.TestValidate(cmd).IsValid.Should().BeTrue();
    }
}
