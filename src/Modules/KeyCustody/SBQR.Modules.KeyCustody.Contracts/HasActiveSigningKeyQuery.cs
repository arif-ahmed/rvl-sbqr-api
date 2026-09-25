using MediatR;

namespace SBQR.Modules.KeyCustody.Contracts;

/// <summary>
/// Pure-read cross-module query: does the tenant have at least one
/// <c>ACTIVE</c> row in <c>crypto_keys</c>? Used by the Tenancy activate-gate
/// (FR-TENANT-001) to refuse <c>Pending → Active</c> transitions when the
/// tenant has no signing key.
///
/// <para>
/// Returns <c>true</c> when the tenant has an ACTIVE key, <c>false</c>
/// otherwise (no rows, all suspended, all retired, all revoked). The
/// gate's caller treats <c>false</c> as a blocking precondition.
/// </para>
///
/// <para>
/// Distinct from <see cref="GetSigningKeyQuery"/>, which returns the
/// highest-version key regardless of status. The gate specifically needs
/// the ACTIVE filter; conflating the two would let suspended keys pass.
/// </para>
/// </summary>
public sealed record HasActiveSigningKeyQuery(Guid TenantId) : IRequest<bool>;
