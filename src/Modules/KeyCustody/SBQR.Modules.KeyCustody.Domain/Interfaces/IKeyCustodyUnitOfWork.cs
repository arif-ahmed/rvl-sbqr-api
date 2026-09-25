using SBQR.SharedKernel.Persistence;

namespace SBQR.Modules.KeyCustody.Domain.Interfaces;

/// <summary>
/// Module-local unit-of-work seam for the KeyCustody bounded context.
/// Extends the shared <see cref="IUnitOfWork"/> contract but is registered
/// (and injected) under its OWN name: with multiple modules each owning a
/// DbContext, a second bare <c>IUnitOfWork</c> registration would make the
/// host's DI container resolve the wrong module's context (last
/// registration wins — Tenancy keeps the bare contract, same convention as
/// <c>SBQR.Modules.IdentityAccess.Application.Abstractions.IIdentityUnitOfWork</c>).
/// KeyCustody code must inject <c>IKeyCustodyUnitOfWork</c> so its saves
/// always flush through <c>KeyCustodyDbContext</c>.
/// </summary>
public interface IKeyCustodyUnitOfWork : IUnitOfWork
{
}
