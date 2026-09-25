using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.IdentityAccess.Application.Abstractions;

/// <summary>
/// Module-local unit-of-work seam for the Identity &amp; Access bounded
/// context. Extends the shared <see cref="IUnitOfWork"/> contract but is
/// registered (and injected) under its OWN name: with two modules each owning
/// a DbContext, a second bare <c>IUnitOfWork</c> registration would make the
/// host's DI container resolve the wrong module's context for one of them
/// (last registration wins). Tenancy keeps the bare contract; IdentityAccess
/// code must inject <c>IIdentityUnitOfWork</c> so its saves always flush
/// through <c>IdentityDbContext</c>.
/// </summary>
public interface IIdentityUnitOfWork : IUnitOfWork
{
}
