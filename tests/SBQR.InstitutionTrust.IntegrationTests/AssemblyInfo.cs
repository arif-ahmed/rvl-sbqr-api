using Xunit;

// Integration tests share a single PostgreSQL Testcontainers instance across
// the entire assembly. Disabling assembly- and collection-level parallelism
// keeps Respawn checkpoints safe (no two tests ever touch the same container
// concurrently).
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SBQR.InstitutionTrust.IntegrationTests;

/// <summary>
/// xUnit collection backed by <see cref="Infrastructure.PostgreSqlFixture"/>.
/// Membership groups every integration test in this assembly so they share
/// the single ephemeral PostgreSQL container the fixture owns.
/// </summary>
[CollectionDefinition(nameof(InstitutionTrustCollection))]
public sealed class InstitutionTrustCollection : ICollectionFixture<Infrastructure.PostgreSqlFixture>
{
}
