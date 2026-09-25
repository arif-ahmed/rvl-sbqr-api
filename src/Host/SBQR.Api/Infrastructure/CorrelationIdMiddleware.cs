using System.Diagnostics;
using SBQR.SharedKernel.Web;

namespace SBQR.Api.Infrastructure;

/// <summary>
/// Host-level middleware that establishes a single <c>correlation_id</c> for
/// the duration of one HTTP request and exposes it two ways:
/// <list type="number">
///   <item>On the per-request <c>HttpContext.Items</c> under
///         <see cref="CorrelationContract.ItemsKey"/> so any downstream
///         component (audit logger, application services) can read the same
///         value without re-minting.</item>
///   <item>As the <c>traceId</c> field of every ProblemDetails error body
///         the framework emits, via
///         <see cref="ProblemDetailsCorrelationEnricher"/>. The framework's
///         default <c>traceId</c> (the W3C Activity id) is overwritten so
///         there is exactly one trace handle per response.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// <b>Server-mint only.</b> The id is always generated server-side; any
/// <c>X-Correlation-Id</c> the client supplies is intentionally ignored.
/// Rationale: a client cannot be trusted to mint a unique GUID per request,
/// and reusing a value the client picked lets two unrelated requests appear
/// as one in audit queries. No response header is emitted — the value lives
/// solely in the ProblemDetails <c>traceId</c> field on errors and in the
/// <c>audit_logs.correlation_id</c> column on every audit row for the
/// request, keeping the wire surface minimal.
/// </para>
/// <para>
/// The same id is recorded on every <c>audit_logs</c> row written for this
/// request, regardless of how many <c>IAuditLogger.LogAsync</c> calls fire
/// across modules — which is the difference between a request-scoped trace
/// and the per-call trace that existed before this middleware shipped.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Server-mint only — see remarks. Client-supplied headers are
        // ignored so the value is guaranteed unique and unspoofable.
        var correlationId = Guid.NewGuid();

        // (1) Make it available to anything in the request scope.
        context.Items[CorrelationContract.ItemsKey] = correlationId;

        // (2) Tag the ambient Activity so distributed-tracing sinks /
        //     log enrichers see the same id (Activity.TraceId itself is a
        //     16-byte hex string and doesn't fit a Guid).
        if (Activity.Current is not null)
        {
            Activity.Current.AddTag("correlation_id", correlationId);
            Activity.Current.SetBaggage("correlation_id", correlationId.ToString());
        }

        await _next(context).ConfigureAwait(false);
    }
}
