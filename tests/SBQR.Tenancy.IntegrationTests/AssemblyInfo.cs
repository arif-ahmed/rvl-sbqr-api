// Integration tests share a single PostgreSQL Testcontainers instance across
// the entire assembly. Disabling assembly- and collection-level parallelism
// keeps Respawn checkpoints safe (no two tests ever touch the same container
// concurrently).
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
