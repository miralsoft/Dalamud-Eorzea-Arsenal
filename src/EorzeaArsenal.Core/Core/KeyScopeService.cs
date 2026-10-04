using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Api;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>What is known about the stored key's scopes.</summary>
public enum KeyScopeState
{
    /// <summary>Not asked yet for this key, or no key is stored.</summary>
    NotAsked,

    /// <summary>The server answered with the key's scopes.</summary>
    Known,

    /// <summary>A 404: a server before 1.4, which cannot say. Check nothing and let a 403 decide.</summary>
    NotOffered,

    /// <summary>The question failed for another reason. Check nothing; asking again later may work.</summary>
    Failed,
}

/// <summary>
/// Holds what the stored key may do, read once per key from <c>GET /device/key</c>.
/// </summary>
/// <remarks>
/// <para>
/// It exists for one button. From server 1.4 on, deleting a row needs <c>gear:delete</c>, and a key paired
/// before that is never topped up with it. Offering the button anyway turns every press into a 403; hiding
/// it would leave the player wondering where it went. So the window shows it locked, with the way to get
/// the right, and that needs to know before the press.
/// </para>
/// <para>
/// Only a known list that lacks the scope locks anything. A 404 (a server before 1.4, where deleting works
/// without it) and a failed question both leave the button live and let a 403 decide, because guessing
/// "no" would hide a working feature. A 403 that names a scope is remembered as well, so the lock appears
/// after the first refusal even on a server that answered nothing here.
/// </para>
/// </remarks>
public sealed class KeyScopeService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly ILog? _log;
    private readonly object _gate = new();
    private readonly HashSet<string> _refused = new(StringComparer.OrdinalIgnoreCase);

    // The key the held answer belongs to. Compared in memory only, never logged or written anywhere.
    private string? _askedFor;
    private HashSet<string>? _scopes;
    private KeyScopeState _state = KeyScopeState.NotAsked;
    private Task<KeyScopeState>? _inFlight;

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Supplies the stored key.</param>
    /// <param name="log">Optional log.</param>
    public KeyScopeService(IApiClient api, ITokenStore tokens, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _log = log;
    }

    /// <summary>What is known about the stored key right now. Asks nothing.</summary>
    public KeyScopeState State
    {
        get
        {
            lock (_gate)
            {
                ForgetIfKeyChanged();
                return _state;
            }
        }
    }

    /// <summary>
    /// Whether the stored key is known to lack <paramref name="scope"/>: the server listed its scopes without
    /// it, or refused a call naming it. <see langword="false"/> whenever nothing certain is known.
    /// </summary>
    /// <param name="scope">The scope to check, such as <see cref="ScopeUtil.GearDelete"/>.</param>
    /// <returns><see langword="true"/> only when the key certainly lacks it.</returns>
    public bool IsKnownToLack(string scope)
    {
        lock (_gate)
        {
            ForgetIfKeyChanged();
            return _refused.Contains(scope) ||
                (_state == KeyScopeState.Known && _scopes is not null && !_scopes.Contains(scope));
        }
    }

    /// <summary>
    /// Whether the stored key certainly has <paramref name="scope"/>; <see langword="null"/> when the server
    /// did not say.
    /// </summary>
    /// <param name="scope">The scope to check.</param>
    /// <returns><see langword="true"/> or <see langword="false"/> from a known list, otherwise <see langword="null"/>.</returns>
    public bool? Has(string scope)
    {
        lock (_gate)
        {
            ForgetIfKeyChanged();
            if (_refused.Contains(scope))
            {
                return false;
            }

            return _state == KeyScopeState.Known && _scopes is not null ? _scopes.Contains(scope) : null;
        }
    }

    /// <summary>Remembers that the server refused a call because the stored key lacks <paramref name="scope"/>.</summary>
    /// <param name="scope">The scope a 403 named in <c>missing_scope</c>.</param>
    public void NoteRefused(string scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
        {
            return;
        }

        lock (_gate)
        {
            ForgetIfKeyChanged();
            _refused.Add(scope);
        }
    }

    /// <summary>
    /// Like <see cref="EnsureAsync"/>, but asks again while the key is known to lack
    /// <paramref name="scope"/>.
    /// </summary>
    /// <param name="scope">The scope whose lock should be re-checked, such as <see cref="ScopeUtil.GearDelete"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The state afterwards.</returns>
    /// <remarks>
    /// Allowing a scope on the website changes the key without replacing it, so nothing here would notice:
    /// the button would stay locked until the plugin restarts. Asking again on each opening while it is
    /// locked costs one read per opening, and only for a player who is looking at a locked button.
    /// </remarks>
    public Task<KeyScopeState> RecheckAsync(string scope, CancellationToken ct) =>
        IsKnownToLack(scope) ? RefreshAsync(ct) : EnsureAsync(ct);

    /// <summary>Asks the server once for the stored key, unless an answer for it is already held.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The state afterwards.</returns>
    public Task<KeyScopeState> EnsureAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            ForgetIfKeyChanged();
            if (_state is KeyScopeState.Known or KeyScopeState.NotOffered)
            {
                return Task.FromResult(_state);
            }
        }

        return RefreshAsync(ct);
    }

    /// <summary>Asks the server for the stored key's scopes, joining a question already on its way.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The state afterwards.</returns>
    public Task<KeyScopeState> RefreshAsync(CancellationToken ct)
    {
        lock (_gate)
        {
            ForgetIfKeyChanged();
            if (_inFlight is { IsCompleted: false } running)
            {
                return running;
            }

            var key = _tokens.ApiKey;
            if (string.IsNullOrEmpty(key))
            {
                return Task.FromResult(KeyScopeState.NotAsked);
            }

            _inFlight = AskAsync(key, ct);
            return _inFlight;
        }
    }

    private async Task<KeyScopeState> AskAsync(string key, CancellationToken ct)
    {
        ApiResult<KeyScopesResponse> result;
        try
        {
            result = await _api.GetKeyScopesAsync(key, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return KeyScopeState.NotAsked;
        }

        lock (_gate)
        {
            // The key changed while the question was out: the answer is about a key that is gone.
            if (!string.Equals(key, _tokens.ApiKey, StringComparison.Ordinal))
            {
                return KeyScopeState.NotAsked;
            }

            _askedFor = key;
            if (result.IsSuccess)
            {
                _scopes = new HashSet<string>(result.Value!.Scopes.Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
                _state = KeyScopeState.Known;

                // A right granted since the refusal, on the website's API keys page, keeps the same key.
                // The list is the newer word, so a refusal it contradicts no longer locks anything.
                _refused.ExceptWith(_scopes);
            }
            else if (result.Error!.Kind == ApiErrorKind.NotFound)
            {
                _scopes = null;
                _state = KeyScopeState.NotOffered;
            }
            else
            {
                _scopes = null;
                _state = KeyScopeState.Failed;
                _log?.Warning($"Key scopes: could not read them ({result.Error.Kind}, HTTP {result.Error.StatusCode}). request_id={result.Error.RequestId}.");
            }

            return _state;
        }
    }

    /// <summary>Drops everything held once the stored key is not the one it was learned for. Call under the gate.</summary>
    private void ForgetIfKeyChanged()
    {
        var key = _tokens.ApiKey;
        if (_askedFor is null && _refused.Count == 0 && _state == KeyScopeState.NotAsked)
        {
            _askedFor = key;
            return;
        }

        if (string.Equals(key, _askedFor, StringComparison.Ordinal))
        {
            return;
        }

        _askedFor = key;
        _scopes = null;
        _state = KeyScopeState.NotAsked;
        _refused.Clear();
    }
}
