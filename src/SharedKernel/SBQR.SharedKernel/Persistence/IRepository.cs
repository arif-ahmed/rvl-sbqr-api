namespace SBQR.SharedKernel.Persistence;

/// <summary>
/// Generic repository abstraction. Each module's
/// <c>Infrastructure/Persistence/</c> ships an EF Core implementation;
/// Dapper-based implementations live under
/// <c>Infrastructure/Persistence/DapperQueries/</c> for the reporting
/// escape hatch (see <c>docs/PERSISTENCE_DECISIONS.md</c>).
///
/// The shared kernel only knows about this interface. Module-specific
/// repository contracts (e.g. <c>ISigningKeyRepository</c>) extend this
/// with aggregate-specific query methods.
/// </summary>
/// <typeparam name="T">Aggregate root type.</typeparam>
public interface IRepository<T>
    where T : class
{
    /// <summary>
    /// Look up an aggregate by its strongly-typed identifier.
    /// </summary>
    /// <param name="id">Aggregate identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The aggregate, or <c>null</c> if not found.</returns>
    Task<T?> GetByIdAsync(object id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a new aggregate. Persistence happens when the surrounding
    /// <see cref="IUnitOfWork"/> is committed.
    /// </summary>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an aggregate for removal. Persistence happens when the
    /// surrounding <see cref="IUnitOfWork"/> is committed.
    /// </summary>
    Task RemoveAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark an aggregate as modified. Persistence happens when the
    /// surrounding <see cref="IUnitOfWork"/> is committed.
    /// </summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
}
