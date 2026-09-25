namespace SBQR.SharedKernel.Persistence;

/// <summary>
/// Unit-of-work abstraction. Each module's <c>Infrastructure/Persistence/</c>
/// owns a concrete implementation backed by its EF Core <c>DbContext</c>
/// (or, for Dapper flows, by an explicit <c>IDbTransaction</c>).
/// </summary>
public interface IUnitOfWork
{
    /// <summary>
    /// Commit pending changes. Returns the number of state entries
    /// written (mirrors <c>DbContext.SaveChangesAsync</c> for the EF Core
    /// implementation; the Dapper implementation returns rows affected
    /// by the underlying <c>IDbConnection</c> call).
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
