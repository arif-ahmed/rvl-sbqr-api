namespace SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;

/// <summary>
/// Port onto the BB trust store (or its mock). This is a shape WE derived
/// from the BB spec's own language (Annex B: "retrieve the public key file
/// ... using Institution_ID"; Annex C: SPKI PEM keys) — no real BB contract
/// has been published. The only implementation today is
/// <c>HttpTrustStoreClient</c>, pointed at the mock via
/// <c>TrustStore:BaseUrl</c>. If BB later publishes a different real
/// contract, only that implementation's request/response mapping should
/// need to change.
/// </summary>
public interface ITrustStoreClient
{
    /// <summary>Fetch every institution currently known to the trust store.</summary>
    Task<IReadOnlyList<TrustStoreInstitutionRecord>> FetchAllAsync(CancellationToken cancellationToken);
}

/// <summary>One institution as reported by the trust store.</summary>
public sealed record TrustStoreInstitutionRecord(
    string InstitutionId,
    string InstitutionName,
    string Status,
    TrustStoreKeyRecord ActiveKey,
    string? InstituteType = null);

/// <summary>An institution's currently active public key, as reported by the trust store.</summary>
public sealed record TrustStoreKeyRecord(
    int KeyVersion,
    string PublicKeyPem,
    string Sha256,
    string Status);
