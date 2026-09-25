namespace SBQR.Tests.Common;

/// <summary>
/// Shared test data builder. Use this when you need a quick
/// value-object seed in tests:
/// <code>
/// var recipient = TestDataBuilder.SomeRecipient();
/// </code>
/// </summary>
public static class TestDataBuilder
{
    /// <summary>Returns a deterministic Guid for assertion stability.</summary>
    public static Guid SomeId() => Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Returns a different deterministic Guid for variety in fixtures.</summary>
    public static Guid AnotherId() => Guid.Parse("00000000-0000-0000-0000-000000000002");
}
