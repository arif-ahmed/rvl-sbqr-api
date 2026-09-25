// src/BB.TrustStoreMock/Controllers/AdminController.cs
using Microsoft.AspNetCore.Mvc;

namespace BB.TrustStoreMock.Controllers;

/// <summary>
/// Mock-only control surface — NOT part of the simulated "real" BB contract.
/// The spec-shaped upload lives on the trust-store surface
/// (PUT /trust-store/institutions/{id}/public-key); what remains here are
/// the SIMULATION affordances a real BB service would exercise out-of-band,
/// like withdrawing trust in a key: revoking exercises the "key revoked"
/// rejection path from Annex B Step 3 so consumers can be tested against it.
/// </summary>
[ApiController]
[Route("admin/institutions")]
public sealed class AdminController : ControllerBase
{
    private readonly TrustStoreRepository _store;

    public AdminController(TrustStoreRepository store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>Marks the institution's active key REVOKED — exercises the "key revoked" rejection path from Annex B Step 3.</summary>
    [HttpPost("{institutionId}/keys/revoke")]
    public async Task<IActionResult> Revoke(string institutionId, CancellationToken cancellationToken)
    {
        var revoked = await _store.RevokeActiveKeyAsync(institutionId, cancellationToken).ConfigureAwait(false);
        return revoked ? NoContent() : NotFound();
    }
}
