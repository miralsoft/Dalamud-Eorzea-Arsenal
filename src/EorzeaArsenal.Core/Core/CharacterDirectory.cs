using System.Collections.Concurrent;

namespace EorzeaArsenal.Core;

/// <summary>
/// Maps a character's stable <c>cid_hash</c> to the server's numeric <c>character_id</c> (e.g.
/// <c>"42"</c>). The server's per-character REST paths (<c>…/characters/{id}/weekly</c>) are keyed by
/// that numeric id, not by the hash; the id is only learned from a successful <c>PUT /gear</c> or
/// <c>POST /inventory</c> response. The gear/inventory sync services record it here; the weekly sync
/// reads it. Thread-safe; raises <see cref="Changed"/> so the host can persist the map across sessions.
/// </summary>
public sealed class CharacterDirectory
{
    private readonly ConcurrentDictionary<string, string> _map;

    /// <summary>Creates the directory, optionally seeded from a persisted map.</summary>
    /// <param name="seed">Previously persisted <c>cid_hash → character_id</c> entries, or <see langword="null"/>.</param>
    public CharacterDirectory(IEnumerable<KeyValuePair<string, string>>? seed = null) =>
        _map = seed is null
            ? new ConcurrentDictionary<string, string>(StringComparer.Ordinal)
            : new ConcurrentDictionary<string, string>(seed, StringComparer.Ordinal);

    /// <summary>Raised (on the recording thread) when a mapping is added or changed, for persistence.</summary>
    public event Action? Changed;

    /// <summary>Looks up the server character id for a hash.</summary>
    /// <param name="cidHash">The character's <c>cid_hash</c>.</param>
    /// <param name="characterId">The numeric server id when known.</param>
    /// <returns><see langword="true"/> if a mapping exists.</returns>
    public bool TryGet(string cidHash, out string characterId)
    {
        if (!string.IsNullOrEmpty(cidHash) && _map.TryGetValue(cidHash, out var id))
        {
            characterId = id;
            return true;
        }

        characterId = string.Empty;
        return false;
    }

    /// <summary>
    /// Records (or updates) the server id for a hash. No-op for empty inputs. Raises
    /// <see cref="Changed"/> only when the stored value actually changed; a throwing handler is
    /// swallowed so persistence can never turn a successful push into a failure.
    /// </summary>
    /// <param name="cidHash">The character's <c>cid_hash</c>.</param>
    /// <param name="characterId">The numeric server id from a push response.</param>
    public void Record(string cidHash, string? characterId)
    {
        if (string.IsNullOrEmpty(cidHash) || string.IsNullOrEmpty(characterId))
        {
            return;
        }

        var changed = !_map.TryGetValue(cidHash, out var existing) || existing != characterId;
        _map[cidHash] = characterId;
        if (!changed)
        {
            return;
        }

        try
        {
            Changed?.Invoke();
        }
        catch (Exception)
        {
            // Persistence is best-effort; a failing handler must not propagate into the push path.
        }
    }

    /// <summary>Returns a snapshot of all mappings (for persistence).</summary>
    /// <returns>A copy of the current <c>cid_hash → character_id</c> map.</returns>
    public IReadOnlyDictionary<string, string> Snapshot() =>
        new Dictionary<string, string>(_map, StringComparer.Ordinal);
}
