namespace SBQR.SharedKernel.Web;

/// <summary>
/// Wire-level constants for the request-scoped correlation id. The host
/// middleware (<c>SBQR.Api.Infrastructure.CorrelationIdMiddleware</c>) mints
/// the value server-side, surfaces it on the per-request context (under
/// <see cref="ItemsKey"/>), and the host's
/// <c>ProblemDetailsCorrelationEnricher</c> writes it into the
/// <c>traceId</c> field of every error body so a single field is the
/// canonical trace handle. Audit logger reads it from
/// <c>HttpContext.Items</c> via <see cref="ItemsKey"/> and never mints its
/// own.
/// </summary>
/// <remarks>
/// Lives in the shared kernel so the host-side middleware and any module
/// that wants to log/audit against the same id can reference one source of
/// truth — same pattern as <c>JwtClaimNames</c> for OAuth claim spellings.
/// </remarks>
public static class CorrelationContract
{
    /// <summary>
    /// Key under which the per-request correlation id (Guid) is stashed on
    /// <c>HttpContext.Items</c>. Audit logger reads this to avoid minting a
    /// fresh GUID per write, so every audit row for the same HTTP request
    /// shares one id.
    /// </summary>
    public const string ItemsKey = "CorrelationId";
}


