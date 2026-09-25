using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SBQR.Modules.InstitutionTrust.Infrastructure.TrustStore;

/// <summary>
/// The single seam that talks HTTP to the trust store — the real BB trust
/// store once BB publishes it, or any trust-store endpoint configured via
/// <c>TrustStore:BaseUrl</c> until then. If BB's real response shape differs
/// from our guess, only the DTO mapping below should need to change.
/// </summary>
public sealed partial class HttpTrustStoreClient : ITrustStoreClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<HttpTrustStoreClient> _logger;

    public HttpTrustStoreClient(HttpClient httpClient, ILogger<HttpTrustStoreClient>? logger = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? NullLogger<HttpTrustStoreClient>.Instance;
    }

    public async Task<IReadOnlyList<TrustStoreInstitutionRecord>> FetchAllAsync(CancellationToken cancellationToken)
    {
        var dtos = await _httpClient
            .GetFromJsonAsync<List<TrustStoreInstitutionDto>>("trust-store/institutions", cancellationToken)
            .ConfigureAwait(false);

        var records = new List<TrustStoreInstitutionRecord>();
        foreach (var dto in dtos ?? [])
        {
            // BB's real contract is unpublished; a response that omits the key
            // object is entirely plausible even though our own mock never does.
            // Skip that one record rather than letting an NRE fail the whole
            // batch and strand every other institution in it.
            if (dto?.ActiveKey is null)
            {
                LogRecordMissingKey(dto?.InstitutionId ?? "(unknown)");
                continue;
            }

            records.Add(new TrustStoreInstitutionRecord(
                dto.InstitutionId,
                dto.InstitutionName,
                dto.Status,
                new TrustStoreKeyRecord(
                    dto.ActiveKey.KeyVersion,
                    dto.ActiveKey.PublicKeyPem,
                    dto.ActiveKey.Sha256,
                    dto.ActiveKey.Status),
                dto.InstituteType));
        }

        return records;
    }

    [LoggerMessage(
        EventId = 6111,
        Level = LogLevel.Warning,
        Message = "Trust store reported institution {InstitutionCode} with no key object; skipping that record.")]
    private partial void LogRecordMissingKey(string institutionCode);

    private sealed record TrustStoreInstitutionDto(
        string InstitutionId,
        string InstitutionName,
        string Status,
        TrustStoreKeyDto? ActiveKey,
        string? InstituteType = null);

    private sealed record TrustStoreKeyDto(
        int KeyVersion,
        string PublicKeyPem,
        string Sha256,
        string Status);
}
