using FluentValidation;
using MediatR;

namespace SBQR.SharedKernel.Application;

/// <summary>
/// MediatR pipeline behavior that runs FluentValidation validators
/// before every command/query handler. The behavior is registered
/// once in the host via <c>services.AddTransient(typeof(IPipelineBehavior&lt;,&gt;), typeof(ValidationBehavior&lt;,&gt;))</c>;
/// validators themselves are discovered per-module by
/// <c>services.AddValidatorsFromAssembly(...)</c> in each
/// <see cref="IModule.RegisterServices"/> call.
///
/// On failure, the behavior short-circuits with
/// <see cref="Result{T}.Failure(ErrorCode.ValidationFailed, ...)"/>
/// without invoking the inner handler. The inner handler therefore
/// can assume its input is valid.
/// </summary>
/// <typeparam name="TRequest">MediatR request type.</typeparam>
/// <typeparam name="TResponse">MediatR response type. Must be <see cref="Result{T}"/> for any non-trivial pipeline.</typeparam>
public sealed class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;

    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators)
    {
        _validators = validators;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(next);

        if (!_validators.Any())
        {
            return await next().ConfigureAwait(false);
        }

        var context = new ValidationContext<TRequest>(request);
        var results = await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken)))
            .ConfigureAwait(false);

        var failures = results
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0)
        {
            return await next().ConfigureAwait(false);
        }

        var message = string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"));

        // If the response type is Result<T>, build a failure directly. Otherwise, throw — the
        // caller has wired up an incompatible pipeline.
        var responseType = typeof(TResponse);
        if (responseType.IsGenericType && responseType.GetGenericTypeDefinition() == typeof(Result<>))
        {
            var failureMethod = responseType
                .GetMethod(nameof(Result<object>.Failure), new[] { typeof(ErrorCode), typeof(string) })!;

            return (TResponse)failureMethod.Invoke(null, new object?[] { ErrorCode.ValidationFailed, message })!;
        }

        throw new ValidationException(failures);
    }
}
