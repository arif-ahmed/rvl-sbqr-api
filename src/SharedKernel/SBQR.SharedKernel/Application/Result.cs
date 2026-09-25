namespace SBQR.SharedKernel.Application;

/// <summary>
/// Discriminated-union-style return type for application handlers and
/// services. Handlers MUST NOT throw exceptions for expected business
/// outcomes; they return <see cref="Result{T}"/> instead.
/// </summary>
/// <typeparam name="T">The success payload type.</typeparam>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, ErrorCode? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        _value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"Cannot read Value from a failed Result. ErrorCode={ErrorCode}, ErrorMessage={ErrorMessage}.");

    public ErrorCode? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static Result<T> Ok(T value) =>
        new(isSuccess: true, value: value, errorCode: null, errorMessage: null);

    public static Result<T> Failure(ErrorCode errorCode, string errorMessage) =>
        new(isSuccess: false, value: default, errorCode: errorCode, errorMessage: errorMessage);

    public static implicit operator Result<T>(T value) => Ok(value);
}

/// <summary>
/// Stable error codes returned from <see cref="Result{T}"/>. Concrete
/// codes are defined per module; this enum lists only the cross-cutting
/// codes used by the shared kernel and host.
/// </summary>
public enum ErrorCode
{
    /// <summary>No specific error category — caller should look at ErrorMessage.</summary>
    Unspecified = 0,

    /// <summary>One or more FluentValidation rules failed. Details in ErrorMessage.</summary>
    ValidationFailed = 1,

    /// <summary>The requested entity was not found.</summary>
    NotFound = 2,

    /// <summary>The caller is not authenticated.</summary>
    Unauthenticated = 3,

    /// <summary>The caller is authenticated but lacks the required authorization.</summary>
    Forbidden = 4,

    /// <summary>An invariant required by the domain has been violated.</summary>
    InvariantViolation = 5,

    /// <summary>The request conflicts with existing state (e.g. a duplicate idempotency key).</summary>
    Conflict = 6,

    /// <summary>The request is well-formed but the current resource state prevents it (e.g. no ACTIVE signing key).</summary>
    InvalidState = 7,

    /// <summary>A cryptographic signing operation failed (key/HSM problem) — distinct from an unhandled software bug.</summary>
    SigningFailed = 8,
}
