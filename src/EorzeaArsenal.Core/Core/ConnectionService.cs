using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>The terminal outcome of the device-flow connect loop.</summary>
public enum ConnectOutcome
{
    /// <summary>A key was issued and stored.</summary>
    Success,

    /// <summary>The device code expired before approval.</summary>
    Expired,

    /// <summary>The user denied the request.</summary>
    Denied,

    /// <summary>The code was invalid or already redeemed.</summary>
    Invalid,

    /// <summary>The user/plugin cancelled the flow.</summary>
    Cancelled,

    /// <summary>A transport or unexpected error ended the flow.</summary>
    Error,
}

/// <summary>The result of a connect attempt.</summary>
/// <param name="Outcome">The terminal outcome.</param>
/// <param name="Message">An optional human-readable detail.</param>
public readonly record struct ConnectResult(ConnectOutcome Outcome, string? Message = null)
{
    /// <summary>Whether the connect succeeded.</summary>
    public bool IsSuccess => Outcome == ConnectOutcome.Success;
}

/// <summary>
/// Orchestrates connecting: the OAuth device-flow state machine and the paste-key fallback,
/// plus disconnect. Contains no HTTP or UI — it drives <see cref="IApiClient"/> and persists the
/// issued key via <see cref="ITokenStore"/>. Fully unit-testable with fakes (briefing §13).
/// </summary>
public sealed class ConnectionService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IDelayProvider _delay;
    private readonly ILog _log;

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Where the issued key is stored.</param>
    /// <param name="delay">Delay provider (honors the poll interval; injectable for tests).</param>
    /// <param name="log">Diagnostics sink.</param>
    public ConnectionService(IApiClient api, ITokenStore tokens, IDelayProvider delay, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _delay = delay;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>Starts the device flow.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The device/user codes to display, or an error.</returns>
    public Task<ApiResult<DeviceCodeResponse>> StartDeviceFlowAsync(CancellationToken ct) =>
        _api.RequestDeviceCodeAsync(ct);

    /// <summary>
    /// Polls for approval until a key is issued, the code expires, or cancellation. Honors the
    /// server-provided <see cref="DeviceCodeResponse.Interval"/> between polls (R23) and stops at
    /// <see cref="DeviceCodeResponse.ExpiresIn"/>. On success the key is stored automatically.
    /// </summary>
    /// <param name="code">The response from <see cref="StartDeviceFlowAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The terminal connect result.</returns>
    public async Task<ConnectResult> PollForKeyAsync(DeviceCodeResponse code, CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, code.Interval));
        var pollsRemaining = MaxPolls(code);

        for (var i = 0; i < pollsRemaining; i++)
        {
            try
            {
                await _delay.Delay(interval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new ConnectResult(ConnectOutcome.Cancelled);
            }

            if (ct.IsCancellationRequested)
            {
                return new ConnectResult(ConnectOutcome.Cancelled);
            }

            var poll = await _api.PollDeviceTokenAsync(code.DeviceCode, ct).ConfigureAwait(false);
            if (!poll.IsSuccess)
            {
                // Transient transport error: keep polling until the code expires.
                _log.Warning($"Device-token poll failed transiently: {poll.Error?.Kind}.");
                continue;
            }

            var resolved = Interpret(poll.Value!);
            if (resolved is { } result)
            {
                return result;
            }
        }

        return new ConnectResult(ConnectOutcome.Expired);
    }

    /// <summary>Connects using a key the user pasted (the fallback path, §3.2).</summary>
    /// <param name="key">The pasted key.</param>
    /// <returns><see langword="true"/> if a non-empty key was stored.</returns>
    public bool ConnectWithPastedKey(string? key)
    {
        var trimmed = key?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return false;
        }

        _tokens.SetApiKey(trimmed);
        _log.Info("Stored a pasted API key.");
        return true;
    }

    /// <summary>Clears the stored key — the "Disconnect" action (R42).</summary>
    public void Disconnect()
    {
        _tokens.Clear();
        _log.Info("Disconnected; stored key cleared.");
    }

    private ConnectResult? Interpret(DeviceTokenResponse token)
    {
        if (!string.IsNullOrEmpty(token.ApiKey))
        {
            _tokens.SetApiKey(token.ApiKey);
            _log.Info("Device flow succeeded; key stored.");
            return new ConnectResult(ConnectOutcome.Success);
        }

        return token.Status switch
        {
            "pending" or null or "" => null, // keep polling
            "expired" => new ConnectResult(ConnectOutcome.Expired),
            "denied" => new ConnectResult(ConnectOutcome.Denied),
            "redeemed" or "invalid" => new ConnectResult(ConnectOutcome.Invalid, token.Status),
            _ => new ConnectResult(ConnectOutcome.Error, token.Status),
        };
    }

    private static int MaxPolls(DeviceCodeResponse code)
    {
        var interval = Math.Max(1, code.Interval);
        var expires = Math.Max(interval, code.ExpiresIn);
        return Math.Max(1, expires / interval);
    }
}
