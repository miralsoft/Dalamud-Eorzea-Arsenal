using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;
using EorzeaArsenal.Serialization;

namespace EorzeaArsenal.Api;

/// <summary>
/// The single HTTP implementation of <see cref="IApiClient"/> (R10). Uses one shared, injected
/// <see cref="HttpClient"/> (P5); every call carries an explicit timeout <i>and</i> a
/// <see cref="CancellationToken"/>. Request/response <b>bodies are never logged</b> (R22) — only
/// status codes and the server <c>request_id</c> surface to the caller. TLS validation is left
/// to the platform default and is never disabled (P8).
/// </summary>
public sealed class ApiClient : IApiClient
{
    private const string JsonMediaType = "application/json";

    private readonly HttpClient _http;
    private readonly IApiSettings _settings;
    private readonly TimeSpan _requestTimeout;

    /// <summary>Creates the client.</summary>
    /// <param name="http">The shared, long-lived <see cref="HttpClient"/> (P5).</param>
    /// <param name="settings">Provides the user-configured base URL (P9).</param>
    /// <param name="requestTimeout">Optional per-request timeout (default 30s).</param>
    public ApiClient(HttpClient http, IApiSettings settings, TimeSpan? requestTimeout = null)
    {
        _http = http;
        _settings = settings;
        _requestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
    }

    /// <inheritdoc />
    public async Task<ApiResult<DeviceCodeResponse>> RequestDeviceCodeAsync(CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/device/code"));
        return await SendAsync<DeviceCodeResponse>(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResult<DeviceTokenResponse>> PollDeviceTokenAsync(string deviceCode, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url("/device/token"))
        {
            Content = JsonBody(new DeviceTokenRequest { DeviceCode = deviceCode }),
        };

        // A 400 here is not a transport failure: it carries a terminal device-flow status
        // (expired|invalid|redeemed|denied). Surface it as a value so the poll loop can stop.
        return await SendAsync<DeviceTokenResponse>(request, ct, treatBadRequestAsValue: true).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResult<GearPushResult>> PushGearAsync(string apiKey, GearPayload payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, Url("/gear"))
        {
            Content = JsonBody(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendAsync<GearPushResult>(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResult<VersionResponse>> GetVersionAsync(string? apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url("/version"));
        if (!string.IsNullOrEmpty(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return await SendAsync<VersionResponse>(request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApiResult<BisResponse>> GetBisAsync(string apiKey, string? cidHash, CancellationToken ct)
    {
        var path = "/gear/bis";
        if (!string.IsNullOrEmpty(cidHash))
        {
            path += "?cid_hash=" + Uri.EscapeDataString(cidHash);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return await SendAsync<BisResponse>(request, ct).ConfigureAwait(false);
    }

    private async Task<ApiResult<T>> SendAsync<T>(
        HttpRequestMessage request,
        CancellationToken ct,
        bool treatBadRequestAsValue = false)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_requestTimeout);
        var endpoint = $"{request.Method} {request.RequestUri}";

        try
        {
            using var response = await _http
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode ||
                (treatBadRequestAsValue && response.StatusCode == HttpStatusCode.BadRequest))
            {
                return Deserialize<T>(body);
            }

            return ApiResult<T>.Fail(MapError(response, body, endpoint));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Caller-initiated cancellation: propagate.
            throw;
        }
        catch (OperationCanceledException)
        {
            return ApiResult<T>.Fail(new ApiError
            {
                Kind = ApiErrorKind.Network,
                Message = "The request timed out.",
                Endpoint = endpoint,
            });
        }
        catch (HttpRequestException ex)
        {
            // Note: ex.Message only; never the body (R22).
            return ApiResult<T>.Fail(new ApiError
            {
                Kind = ApiErrorKind.Network,
                Message = ex.Message,
                Endpoint = endpoint,
            });
        }
    }

    private static ApiResult<T> Deserialize<T>(string body)
    {
        try
        {
            var value = JsonSerializer.Deserialize<T>(body, EorzeaJson.Options);
            if (value is null)
            {
                return ApiResult<T>.Fail(new ApiError
                {
                    Kind = ApiErrorKind.Unexpected,
                    Message = "Empty response body.",
                });
            }

            return ApiResult<T>.Ok(value);
        }
        catch (JsonException)
        {
            return ApiResult<T>.Fail(new ApiError
            {
                Kind = ApiErrorKind.Unexpected,
                Message = "Could not parse the server response.",
            });
        }
    }

    private static ApiError MapError(HttpResponseMessage response, string body, string endpoint)
    {
        var kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ApiErrorKind.Unauthorized,
            HttpStatusCode.Forbidden => ApiErrorKind.Forbidden,
            HttpStatusCode.Conflict => ApiErrorKind.Conflict,
            HttpStatusCode.UnprocessableEntity => ApiErrorKind.Validation,
            HttpStatusCode.NotFound => ApiErrorKind.NotFound,
            HttpStatusCode.BadRequest => ApiErrorKind.BadRequest,
            HttpStatusCode.TooManyRequests => ApiErrorKind.RateLimited,
            _ => ApiErrorKind.Unexpected,
        };

        var problem = TryParseProblem(body);
        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        return new ApiError
        {
            Kind = kind,
            StatusCode = (int)response.StatusCode,
            Message = problem?.Title ?? $"HTTP {(int)response.StatusCode}",
            RequestId = problem?.RequestId,
            Endpoint = endpoint,
            RetryAfter = retryAfter is { Ticks: > 0 } ? retryAfter : null,
        };
    }

    private static ProblemDetails? TryParseProblem(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProblemDetails>(body, EorzeaJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static StringContent JsonBody<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, EorzeaJson.Options);
        return new StringContent(json, Encoding.UTF8, JsonMediaType);
    }

    private string Url(string path) => $"{_settings.BaseUrl.TrimEnd('/')}{path}";

    private sealed class DeviceTokenRequest
    {
        public required string DeviceCode { get; init; }
    }
}
