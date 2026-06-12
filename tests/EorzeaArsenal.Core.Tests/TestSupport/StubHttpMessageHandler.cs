using System.Net;
using System.Text;
using EorzeaArsenal.Abstractions;

namespace EorzeaArsenal.Tests.TestSupport;

/// <summary>
/// A scripted <see cref="HttpMessageHandler"/> that returns queued responses and records the
/// requests it saw, so <see cref="EorzeaArsenal.Api.ApiClient"/> can be unit-tested without a
/// network (briefing §13).
/// </summary>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses = new();

    /// <summary>Every request the handler received, in order.</summary>
    public List<RecordedRequest> Requests { get; } = [];

    /// <summary>Queues a JSON response.</summary>
    /// <param name="status">HTTP status code.</param>
    /// <param name="json">Response body.</param>
    /// <param name="contentType">Content type (defaults to application/json).</param>
    /// <param name="retryAfterSeconds">Optional Retry-After header in seconds.</param>
    /// <returns>This handler, for chaining.</returns>
    public StubHttpMessageHandler Enqueue(
        HttpStatusCode status,
        string json,
        string contentType = "application/json",
        int? retryAfterSeconds = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, contentType),
        };

        if (retryAfterSeconds is { } seconds)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
        }

        _responses.Enqueue(response);
        return this;
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        Requests.Add(new RecordedRequest(
            request.Method,
            request.RequestUri,
            request.Headers.Authorization?.ToString(),
            body));

        return _responses.Count > 0
            ? _responses.Dequeue()
            : new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    /// <summary>Fixed <see cref="IApiSettings"/> used in handler-based tests.</summary>
    public sealed class Settings : IApiSettings
    {
        /// <inheritdoc />
        public string BaseUrl { get; init; } = "http://127.0.0.1:8080/api/v1";
    }
}

/// <summary>A captured outgoing request.</summary>
/// <param name="Method">HTTP method.</param>
/// <param name="Uri">Request URI.</param>
/// <param name="Authorization">Authorization header value, if any.</param>
/// <param name="Body">Request body, if any.</param>
public readonly record struct RecordedRequest(HttpMethod Method, Uri? Uri, string? Authorization, string? Body);
