namespace EorzeaArsenal.Model;

/// <summary>The classified outcome of an API call, independent of HTTP details.</summary>
public enum ApiErrorKind
{
    /// <summary>No error.</summary>
    None = 0,

    /// <summary>401 — missing/invalid/revoked/expired key. Prompt to reconnect.</summary>
    Unauthorized,

    /// <summary>403 — the key lacks <c>gear:write</c>. Reconnect with a correct key.</summary>
    Forbidden,

    /// <summary>409 — this character is already linked to another account. Do not retry.</summary>
    Conflict,

    /// <summary>422 — validation error (unknown job, bad gear_index, …). Fix the payload.</summary>
    Validation,

    /// <summary>404 — no such resource (e.g. no BiS target for this character yet).</summary>
    NotFound,

    /// <summary>400 — payload too large (&gt; 64 KB) or malformed JSON.</summary>
    BadRequest,

    /// <summary>429 — rate limit (max 30 uploads/hour). Back off; honor the window.</summary>
    RateLimited,

    /// <summary>A transport/IO failure (DNS, TLS, timeout, connection refused).</summary>
    Network,

    /// <summary>Any other or unexpected server response.</summary>
    Unexpected,
}

/// <summary>Details of a failed API call. Carries no secrets and no raw bodies (R22).</summary>
public sealed class ApiError
{
    /// <summary>The classified error kind.</summary>
    public required ApiErrorKind Kind { get; init; }

    /// <summary>The HTTP status code, when there was a response.</summary>
    public int? StatusCode { get; init; }

    /// <summary>A short, user-safe message (already free of secrets).</summary>
    public required string Message { get; init; }

    /// <summary>Server correlation id from the problem body, if any — safe to log/show.</summary>
    public string? RequestId { get; init; }

    /// <summary>The request method + URL that failed (no secrets) — safe to log for diagnostics.</summary>
    public string? Endpoint { get; init; }

    /// <summary>For <see cref="ApiErrorKind.RateLimited"/>: how long to back off, if the server said so.</summary>
    public TimeSpan? RetryAfter { get; init; }
}

/// <summary>
/// A discriminated result of an API call: either a typed value or an <see cref="ApiError"/>.
/// Keeps transport and error-mapping concerns out of the calling services.
/// </summary>
/// <typeparam name="T">The success payload type.</typeparam>
public sealed class ApiResult<T>
{
    private ApiResult(bool isSuccess, T? value, ApiError? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error;
    }

    /// <summary>Whether the call succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>The success value, set when <see cref="IsSuccess"/> is <see langword="true"/>.</summary>
    public T? Value { get; }

    /// <summary>The error, set when <see cref="IsSuccess"/> is <see langword="false"/>.</summary>
    public ApiError? Error { get; }

    /// <summary>Creates a success result.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful <see cref="ApiResult{T}"/>.</returns>
    public static ApiResult<T> Ok(T value) => new(true, value, null);

    /// <summary>Creates a failure result.</summary>
    /// <param name="error">The error describing the failure.</param>
    /// <returns>A failed <see cref="ApiResult{T}"/>.</returns>
    public static ApiResult<T> Fail(ApiError error) => new(false, default, error);
}
