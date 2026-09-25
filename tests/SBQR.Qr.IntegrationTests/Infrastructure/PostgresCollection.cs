using Xunit;

namespace SBQR.Qr.IntegrationTests.Infrastructure;

/// <summary>Sequential collection sharing the single PostgreSQL container.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgresCollection : ICollectionFixture<PostgreSqlFixture>
{
    public const string Name = "PostgresCollection";
}
