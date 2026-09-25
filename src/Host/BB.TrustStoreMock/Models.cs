// src/BB.TrustStoreMock/Models.cs
namespace BB.TrustStoreMock;

/// <summary>Response shape for the trust-store read endpoints.</summary>
public sealed record TrustStoreInstitutionDto(
    string InstitutionId,
    string InstituteType,
    string InstitutionName,
    string Status,
    TrustStoreKeyDto ActiveKey);

/// <summary>
/// One key version. ValidFrom/ValidTo are passthrough metadata echoed from
/// the upload — the consumer (InstitutionTrust) owns the temporal gate.
/// Unknown members are ignored by the sync client's JSON binding, so adding
/// them is backward compatible.
/// </summary>
public sealed record TrustStoreKeyDto(
    int KeyVersion,
    string PublicKeyPem,
    string Sha256,
    string Status,
    DateTimeOffset ValidFrom,
    DateTimeOffset? ValidTo);

/// <summary>
/// Request body for the spec-shaped public-key upload
/// (PUT /trust-store/institutions/{institutionId}/public-key — spec Annex B/C:
/// "Bank/MFS/PSP will share their public key with Bangladesh Bank", one key
/// file per Institution_ID). The institution id itself travels in the route.
/// ValidFrom/ValidTo are optional validity-window hints (default validFrom =
/// now); the mock stores and reports them verbatim without deriving status
/// from them.
/// </summary>
public sealed record PublishPublicKeyRequest(
    string PublicKeyPem,
    string InstitutionName,
    string? InstituteType = null,
    DateTimeOffset? ValidFrom = null,
    DateTimeOffset? ValidTo = null);
