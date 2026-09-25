using FluentAssertions;
using FluentValidation.TestHelper;
using SBQR.Modules.Tenancy.Application.Commands.CreateTenant;
using Xunit;

namespace SBQR.Modules.Tenancy.Tests.Application;

/// <summary>
/// Unit tests for <see cref="CreateTenantValidator"/>. The validator runs inside
/// the shared <c>ValidationBehavior&lt;,&gt;</c> pipeline, so these tests pin
/// down the exact rule set the controller relies on. Stateful rules
/// (uniqueness of <c>institution_code</c>) are NOT declared here — the DB
/// UNIQUE constraint on <c>tenants.institution_code</c> is the authoritative
/// guard, because <c>IValidator</c> rules must be pure.
/// </summary>
public sealed class CreateTenantValidatorTests
{
    private readonly CreateTenantValidator _sut = new();

    [Fact]
    public void Valid_command_should_pass()
    {
        var cmd = new CreateTenantCommand(
            InstitutionName: "Mutual Trust Bank",
            InstitutionCode: "010101");

        var result = _sut.TestValidate(cmd);

        result.IsValid.Should().BeTrue();
    }

    // -- InstitutionName ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InstitutionName_should_reject_null_or_whitespace(string name)
    {
        var cmd = new CreateTenantCommand(name, "010101");

        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(c => c.InstitutionName);
    }

    [Fact]
    public void InstitutionName_should_reject_names_longer_than_two_hundred_chars()
    {
        var longName = new string('A', 201);

        var cmd = new CreateTenantCommand(longName, "010101");

        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(c => c.InstitutionName);
    }

    // -- InstitutionCode ---------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void InstitutionCode_should_reject_null_or_whitespace(string code)
    {
        // institution_code is required at registration (mirrors
        // institution_registries.institution_code). NotEmpty must catch
        // a missing/null value BEFORE the regex match.
        var cmd = new CreateTenantCommand("Mutual Trust Bank", code);

        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(c => c.InstitutionCode);
    }

    [Theory]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("abcdef")]
    [InlineData("12345a")]
    public void InstitutionCode_should_reject_wrong_format(string bad)
    {
        var cmd = new CreateTenantCommand("Mutual Trust Bank", bad);

        _sut.TestValidate(cmd).ShouldHaveValidationErrorFor(c => c.InstitutionCode);
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("010101")]
    [InlineData("999999")]
    public void InstitutionCode_should_accept_six_digits(string good)
    {
        var cmd = new CreateTenantCommand("Mutual Trust Bank", good);

        _sut.TestValidate(cmd).ShouldNotHaveValidationErrorFor(c => c.InstitutionCode);
    }
}