using FluentValidation.TestHelper;
using SBQR.Modules.Verification.Application.Commands;
using Xunit;

namespace SBQR.Modules.Verification.Tests;

/// <summary>
/// A6 (validation is 100% server-side) + C6 (replay window): the command
/// validator is the first gate for the verification endpoint's request
/// contract — request id shape and the ±5 minute timestamp window.
/// </summary>
public sealed class ValidateQrValidatorTests
{
    private readonly ValidateQrValidator _validator = new();

    private static ValidateQrCommand ValidCommand(DateTimeOffset? timestamp = null) =>
        new(
            QrPayload: "0002010131008090901",
            RequestId: "req-3f9d2c81ab",
            RequestTimestamp: timestamp ?? DateTimeOffset.UtcNow);

    [Fact]
    public void Accepts_a_fresh_request()
    {
        var result = _validator.TestValidate(ValidCommand());

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Rejects_a_stale_timestamp_outside_the_replay_window()
    {
        var result = _validator.TestValidate(ValidCommand(DateTimeOffset.UtcNow.AddMinutes(-10)));

        result.ShouldHaveValidationErrorFor(c => c.RequestTimestamp);
    }

    [Fact]
    public void Rejects_a_future_timestamp_outside_the_replay_window()
    {
        var result = _validator.TestValidate(ValidCommand(DateTimeOffset.UtcNow.AddMinutes(6)));

        result.ShouldHaveValidationErrorFor(c => c.RequestTimestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has spaces!")]
    public void Rejects_a_malformed_request_id(string requestId)
    {
        var command = new ValidateQrCommand(
            QrPayload: "0002010131008090901",
            RequestId: requestId,
            RequestTimestamp: DateTimeOffset.UtcNow);

        var result = _validator.TestValidate(command);

        result.ShouldHaveValidationErrorFor(c => c.RequestId);
    }
}
