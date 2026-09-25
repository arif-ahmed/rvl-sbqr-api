using Xunit;

namespace SBQR.Tenancy.IntegrationTests.Infrastructure;

/// <summary>
/// xUnit collection definition for all integration tests that share the
/// single ephemeral PostgreSQL container started by
/// <see cref="PostgreSqlFixture"/>. The collection is sequential
/// (<see cref="DisableParallelization"/> = true) so the Respawn
/// checkpoint is never observed mid-reset by another test.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgreSqlFixture>
{
    /// <summary>Collection name used by <c>[Collection(Name)]</c> attributes.</summary>
    public const string Name = "PostgresCollection";
}
