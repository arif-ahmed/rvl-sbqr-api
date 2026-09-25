using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SBQR.SharedKernel.Web;

namespace SBQR.Api.Infrastructure;

/// <summary>
/// Result filter that replaces the framework-default <c>traceId</c> on every
/// <see cref="ProblemDetails"/> error body with the request-scoped
/// <c>correlation_id</c> minted by <see cref="CorrelationIdMiddleware"/>.
/// </summary>
/// <remarks>
/// <para>
/// The framework's default ProblemDetails writer populates <c>traceId</c>
/// from the ambient <see cref="System.Diagnostics.Activity"/>'s W3C trace
/// context id (32-char hex). That id is not stored anywhere
/// queryable — it can't be joined back to <c>audit_logs.correlation_id</c>
/// or to anything an operator can grep. By overwriting <c>traceId</c> with
/// our server-minted GUID, the field becomes the single trace handle a
/// client can quote to ops and ops can join to the audit trail.
/// </para>
/// <para>
/// Successful 2xx responses are unchanged — only ProblemDetails error bodies
/// are touched. There is intentionally no response header echoing the id;
/// the value lives solely in the error body's <c>traceId</c> field and in
/// every audit row written for the request.
/// </para>
/// </remarks>
public sealed class ProblemDetailsCorrelationEnricher : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // Every framework-emitted validation/404/415 path returns an
        // ObjectResult whose Value is a ProblemDetails (or a controller that
        // constructs ValidationProblemDetails / ProblemDetails directly).
        if (context.Result is ObjectResult objectResult
            && objectResult.Value is ProblemDetails problem)
        {
            ReplaceTraceId(problem, context.HttpContext);
        }

        await next().ConfigureAwait(false);
    }

    private static void ReplaceTraceId(ProblemDetails problem, HttpContext context)
    {
        if (!context.Items.TryGetValue(CorrelationContract.ItemsKey, out var raw)
            || raw is not Guid correlationId
            || correlationId == Guid.Empty)
        {
            return;
        }

        // ProblemDetails exposes traceId only through the Extensions bag —
        // the framework's writer populates Extensions["traceId"] with the
        // ambient Activity's W3C trace context id. Overwriting that entry
        // replaces the value the client sees, so the wire field stays a
        // single key with a single value (our server-minted correlation GUID).
        // Idempotent: if a handler downstream has already set it, leave it.
        problem.Extensions["traceId"] = correlationId.ToString();
    }
}

