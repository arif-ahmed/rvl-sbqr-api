using Xunit;

namespace SBQR.Tests.Common;

/// <summary>
/// Shared xUnit collection definition for tests that need a single
/// application-wide fixture (clock, telemetry, etc). The fixture
/// itself is wired up per test class via <c>IClassFixture&lt;T&gt;</c>.
/// </summary>
public sealed class AssemblyFixture : IDisposable
{
    public AssemblyFixture()
    {
        // Reserved for future cross-cutting setup. Empty in this scaffolding pass.
    }

    public void Dispose()
    {
        // Reserved for future cross-cutting teardown.
    }
}

/// <summary>
/// xUnit collection definition that aggregates every class fixture that wants
/// to share the same <see cref="AssemblyFixture"/> instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AssemblyFixtureCollectionDef : ICollectionFixture<AssemblyFixture>
{
    public const string Name = "assembly";
}
