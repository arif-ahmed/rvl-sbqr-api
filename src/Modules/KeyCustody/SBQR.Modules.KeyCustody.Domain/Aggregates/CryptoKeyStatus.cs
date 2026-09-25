namespace SBQR.Modules.KeyCustody.Domain.Aggregates;

/// <summary>
/// Lifecycle status of a <see cref="CryptoKey"/>. Mirrors the DB CHECK constraint
/// on <c>crypto_keys.status</c> defined in
/// <c>docs/design/database-design.md</c> §1.4.
/// </summary>
public enum CryptoKeyStatus
{
    /// <summary>Key generation in progress.</summary>
    Generating = 0,

    /// <summary>Generated and stored but not yet trusted for signing.</summary>
    Pending = 1,

    /// <summary>The tenant's currently-trusted signing key.</summary>
    Active = 2,

    /// <summary>Temporarily not trusted (e.g. compromise investigation).</summary>
    Suspended = 3,

    /// <summary>Marked for retirement; new generations stop, in-flight verifications still pass.</summary>
    Retiring = 4,

    /// <summary>No longer trusted; removed from the trust set.</summary>
    Retired = 5,

    /// <summary>Compromised — verifications MUST fail closed against this key.</summary>
    Revoked = 6,
}
