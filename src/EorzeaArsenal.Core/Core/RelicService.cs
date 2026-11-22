using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>How a stage stands for one job.</summary>
public enum RelicMark
{
    /// <summary>The character holds every piece of it, or of a later stage.</summary>
    Held,

    /// <summary>Done by the player's own tick, not by what the server found.</summary>
    Ticked,

    /// <summary>Not reached yet.</summary>
    Missing,
}

/// <summary>One stage of one job as the view draws it.</summary>
/// <param name="Key">The stage key.</param>
/// <param name="Ilvl">The stage's item level.</param>
/// <param name="Mark">How it stands.</param>
/// <param name="Items">The job's pieces of the stage.</param>
public sealed record RelicStageMark(string Key, int Ilvl, RelicMark Mark, IReadOnlyList<RelicItem> Items);

/// <summary>One job of one series as the view draws it.</summary>
/// <param name="Job">The job code.</param>
/// <param name="Stages">The stages that exist for the job, in order.</param>
/// <param name="Next">The next stage, or <see langword="null"/> when the job is done or nothing is known.</param>
/// <param name="Ticked">The stage the player ticked, or <see langword="null"/>.</param>
/// <param name="Done">Whether the job's last stage is reached.</param>
public sealed record RelicJobRow(string Job, IReadOnlyList<RelicStageMark> Stages, RelicStageMark? Next, string? Ticked, bool Done);

/// <summary>
/// The relic helper of server 1.5: the catalog (<c>GET /relics</c>, once per session and language), the
/// character's progress (<c>GET /me/relics</c>, when the view opens and after a sync) and a tick by hand
/// (<c>PUT /me/relics</c>, only when the player clicks). It computes nothing the server decides; it only
/// lines the progress up against the catalog for drawing.
/// </summary>
public sealed class RelicService
{
    private readonly IApiClient _api;
    private readonly ITokenStore _tokens;
    private readonly IApiSettings _settings;
    private readonly ILog _log;

    private readonly Lock _gate = new();
    private RelicCatalog? _catalog;
    private string? _catalogKey;
    private MyRelics? _mine;
    private string? _mineKey;
    private bool _stale = true;
    private int _loading;

    /// <summary>Creates the service.</summary>
    /// <param name="api">The API client.</param>
    /// <param name="tokens">Holds the API key.</param>
    /// <param name="settings">The base URL, so a catalog is kept per address.</param>
    /// <param name="log">Diagnostics sink.</param>
    public RelicService(IApiClient api, ITokenStore tokens, IApiSettings settings, ILog? log = null)
    {
        _api = api;
        _tokens = tokens;
        _settings = settings;
        _log = log ?? NullLog.Instance;
    }

    /// <summary>The character's progress last read, or <see langword="null"/>.</summary>
    public MyRelics? Mine
    {
        get
        {
            lock (_gate)
            {
                return _mine;
            }
        }
    }

    /// <summary>The error of the last read or tick, or <see langword="null"/> after a success.</summary>
    public ApiError? LastError { get; private set; }

    /// <summary>Whether a read is running.</summary>
    public bool Loading => Volatile.Read(ref _loading) != 0;

    /// <summary>The catalog in this language, or <see langword="null"/> before it was read.</summary>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <returns>The catalog.</returns>
    public RelicCatalog? CatalogFor(string language)
    {
        lock (_gate)
        {
            return _catalogKey == CatalogKey(language) ? _catalog : null;
        }
    }

    /// <summary>Whether the view should read again for this character (opened, synced, or another character).</summary>
    /// <param name="language">The view's language.</param>
    /// <param name="characterId">The character the view shows.</param>
    /// <returns><see langword="true"/> when a read is due.</returns>
    public bool NeedsRead(string language, long? characterId)
    {
        lock (_gate)
        {
            return _stale || _catalogKey != CatalogKey(language) || _mineKey != MineKey(characterId);
        }
    }

    /// <summary>Marks the progress as out of date: the view opened, or an inventory sync or gear push went through.</summary>
    public void Invalidate()
    {
        lock (_gate)
        {
            _stale = true;
        }
    }

    /// <summary>
    /// Reads what is missing: the catalog once per address and language, then the progress. One read at a
    /// time; a second call while one runs returns at once.
    /// </summary>
    /// <param name="language"><c>de</c> or <c>en</c>.</param>
    /// <param name="characterId">The caller's own character, or <see langword="null"/> for the active one.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the read is done.</returns>
    public async Task RefreshAsync(string language, long? characterId, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key) || Interlocked.Exchange(ref _loading, 1) != 0)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                _stale = false;
            }

            if (CatalogFor(language) is null)
            {
                var catalog = await _api.GetRelicCatalogAsync(key, language, ct).ConfigureAwait(false);
                if (!catalog.IsSuccess || catalog.Value?.Data is null)
                {
                    Fail(catalog.Error, "catalog");
                    return;
                }

                lock (_gate)
                {
                    _catalog = catalog.Value.Data;
                    _catalogKey = CatalogKey(language);
                }
            }

            var mine = await _api.GetMyRelicsAsync(key, characterId, ct).ConfigureAwait(false);
            if (!mine.IsSuccess)
            {
                Fail(mine.Error, "progress");
                return;
            }

            lock (_gate)
            {
                _mine = mine.Value?.Data;
                _mineKey = MineKey(characterId);
            }

            LastError = null;
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception ex)
        {
            _log.Error($"Relic read failed: {ex.GetType().Name}.");
            LastError = new ApiError { Kind = ApiErrorKind.Unexpected, Message = ex.GetType().Name };
        }
        finally
        {
            Volatile.Write(ref _loading, 0);
        }
    }

    /// <summary>Ticks a stage by hand, or clears the tick. Only ever on the player's click.</summary>
    /// <param name="characterId">The caller's own character, or <see langword="null"/> for the active one.</param>
    /// <param name="series">The series key.</param>
    /// <param name="job">The job code.</param>
    /// <param name="stage">The stage key, or <see langword="null"/> to clear.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see langword="null"/> on success, else the error.</returns>
    public async Task<ApiError?> TickAsync(long? characterId, string series, string job, string? stage, CancellationToken ct)
    {
        var key = _tokens.ApiKey;
        if (string.IsNullOrEmpty(key))
        {
            return new ApiError { Kind = ApiErrorKind.Unauthorized, Message = "Not connected." };
        }

        var request = new RelicTickRequest { CharacterId = characterId, Series = series, Job = job, Stage = stage ?? string.Empty };
        var result = await _api.PutRelicTickAsync(key, request, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            Fail(result.Error, "tick");
            return result.Error;
        }

        lock (_gate)
        {
            _mine = result.Value?.Data;
            _mineKey = MineKey(characterId);
        }

        LastError = null;
        return null;
    }

    /// <summary>Lines one series up for drawing: per job its stages with their marks and the next stage.</summary>
    /// <param name="series">The series from the catalog.</param>
    /// <param name="mine">The character's progress in it, or <see langword="null"/> when none is known.</param>
    /// <returns>A row per job that has at least one stage, in the catalog's job order.</returns>
    public static IReadOnlyList<RelicJobRow> Rows(RelicSeries series, MyRelicSeries? mine)
    {
        var rows = new List<RelicJobRow>();
        foreach (var job in series.Jobs)
        {
            var stages = series.Stages
                .Where(stage => stage.Items.TryGetValue(job, out var pieces) && pieces.Count > 0)
                .ToList();
            if (stages.Count == 0)
            {
                continue;
            }

            MyRelicJob? progress = null;
            mine?.Jobs.TryGetValue(job, out progress);
            var held = IndexOf(stages, progress?.Held);
            var reached = Math.Max(held, IndexOf(stages, progress?.Reached));

            var marks = stages.Select((stage, i) => new RelicStageMark(
                stage.Key,
                stage.Ilvl,
                i <= held ? RelicMark.Held : i <= reached ? RelicMark.Ticked : RelicMark.Missing,
                stage.Items[job])).ToList();

            var next = progress?.Next is { } nextKey ? marks.FirstOrDefault(m => m.Key == nextKey) : null;
            rows.Add(new RelicJobRow(job, marks, next, progress?.Ticked, progress?.Done ?? false));
        }

        return rows;
    }

    private static int IndexOf(List<RelicStage> stages, string? key) =>
        key is null ? -1 : stages.FindIndex(stage => stage.Key == key);

    private void Fail(ApiError? error, string what)
    {
        LastError = error ?? new ApiError { Kind = ApiErrorKind.Unexpected, Message = "No answer." };
        var tail = error?.RequestId is null ? string.Empty : $", request_id {error.RequestId}";
        _log.Info($"Relic {what} failed: {LastError.Kind}{tail}.");
    }

    private string Address() => _settings.BaseUrl.TrimEnd('/');

    private string CatalogKey(string language) => $"{Address()}|{language}";

    private string MineKey(long? characterId) => $"{Address()}|{characterId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}";
}
