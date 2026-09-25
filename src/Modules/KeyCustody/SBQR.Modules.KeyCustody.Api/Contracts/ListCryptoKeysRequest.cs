namespace SBQR.Modules.KeyCustody.Api.Contracts;

/// <summary>
/// Query-string DTO for <c>GET /v1/crypto-keys</c> and
/// <c>GET /v1/crypto-keys/{tenantId}</c>. All fields optional.
/// </summary>
public sealed record ListCryptoKeysRequest(
    string? Status = null,
    int Page = 1,
    int PageSize = 20);
