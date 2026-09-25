using System.Data.Common;

namespace SBQR.SharedKernel.Persistence;

/// <summary>
/// Factory for opening a <see cref="DbConnection"/> to the application's
/// PostgreSQL database. Used by Dapper query classes inside
/// <c>Infrastructure/Persistence/DapperQueries/</c>.
///
/// Concrete implementation per module is registered in
/// <see cref="Application.IModule.RegisterServices"/>.
/// </summary>
public interface IDbConnectionFactory
{
    /// <summary>
    /// Open a new connection. The caller owns disposal.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DbConnection> OpenAsync(CancellationToken cancellationToken = default);
}
