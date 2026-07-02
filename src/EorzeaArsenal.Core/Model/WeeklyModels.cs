using System.Text.Json;

namespace EorzeaArsenal.Model;

/// <summary>
/// The weekly-checklist values read from the game for one character. Only fields the plugin can
/// determine <i>with confidence</i> are set; everything else stays <see langword="null"/> and is
/// never sent, so the server's merge keeps the user's manual web-app entries untouched (the whole
/// feature is best-effort and must never clobber a value it isn't sure about).
/// </summary>
public sealed class WeeklyValues
{
    /// <summary>Weekly-limited tomestones acquired this week (0..450), or <see langword="null"/> if unknown.</summary>
    public int? TomesHave { get; init; }

    /// <summary>Custom Deliveries done this week (all weekly allowances used), or <see langword="null"/> if unknown.</summary>
    public bool? Custom { get; init; }

    /// <summary>Savage floor 1 weekly loot obtained this week, or <see langword="null"/> if unknown.</summary>
    public bool? F1 { get; init; }

    /// <summary>Savage floor 2 weekly loot obtained this week, or <see langword="null"/> if unknown.</summary>
    public bool? F2 { get; init; }

    /// <summary>Savage floor 3 weekly loot obtained this week, or <see langword="null"/> if unknown.</summary>
    public bool? F3 { get; init; }

    /// <summary>Savage floor 4 weekly loot obtained this week, or <see langword="null"/> if unknown.</summary>
    public bool? F4 { get; init; }

    /// <summary>Whether no field could be determined (nothing to send).</summary>
    public bool IsEmpty =>
        TomesHave is null && Custom is null && F1 is null && F2 is null && F3 is null && F4 is null;

    /// <summary>
    /// Enumerates the fields that are known, as <c>(wireKey, boxedValue)</c> pairs using the literal
    /// camelCase item keys the API expects. Only present (non-<see langword="null"/>) fields appear.
    /// </summary>
    /// <returns>The known fields, keyed by their wire name.</returns>
    public IEnumerable<KeyValuePair<string, object>> Present()
    {
        if (TomesHave is int tomes)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldTomesHave, tomes);
        }

        if (Custom is bool custom)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldCustom, custom);
        }

        if (F1 is bool f1)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldF1, f1);
        }

        if (F2 is bool f2)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldF2, f2);
        }

        if (F3 is bool f3)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldF3, f3);
        }

        if (F4 is bool f4)
        {
            yield return new KeyValuePair<string, object>(WeeklyProtocol.FieldF4, f4);
        }
    }
}

/// <summary>A weekly read: the character it belongs to plus the values determined from the game.</summary>
public sealed class WeeklyData
{
    /// <summary>The character the values belong to (same identity/cid_hash as <c>PUT /gear</c>).</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>The weekly values that could be read (only confident fields are set).</summary>
    public required WeeklyValues Values { get; init; }
}

/// <summary>
/// The wire body of <c>PUT /api/v1/characters/{characterId}/weekly</c>. Carries only the known
/// fields as a literal-keyed <c>items</c> map; the server merges them (a sent field is set, an
/// unsent field is left as-is). No week is computed/sent — the server derives the current reset week.
/// </summary>
public sealed class WeeklyPayload
{
    /// <summary>The known fields to merge, keyed by their literal camelCase names (e.g. <c>tomesHave</c>).</summary>
    public required IReadOnlyDictionary<string, object> Items { get; init; }
}

/// <summary>
/// Response of <c>GET /api/v1/characters/{characterId}/weekly</c>: the current server-stored values,
/// the server-derived week (informational), and whether Savage lockout fields should be tracked.
/// </summary>
public sealed class WeeklyResponse
{
    /// <summary>
    /// The current server-stored weekly values. Kept as a raw <see cref="JsonElement"/> (not a typed
    /// dictionary) so a backend that serializes an empty object as <c>[]</c> (a common PHP quirk)
    /// still parses; read it via <see cref="WeeklyData"/>-side helpers only when it is an object.
    /// </summary>
    public JsonElement? Data { get; init; }

    /// <summary>The reset week the values belong to (server-derived; informational only).</summary>
    public string? Week { get; init; }

    /// <summary>Whether Savage-floor fields (<c>f1</c>..<c>f4</c>) are tracked for this account.</summary>
    public bool SavageLockout { get; init; }
}

/// <summary>Success body of <c>PUT …/weekly</c>: <c>{ "status":"ok", "week":"…", "data":{ …merged… } }</c>.</summary>
public sealed class WeeklyPushResult
{
    /// <summary>Always <c>"ok"</c> on success.</summary>
    public string? Status { get; init; }

    /// <summary>The reset week the merge applied to (server-derived).</summary>
    public string? Week { get; init; }

    /// <summary>The merged values after applying the request (raw; tolerant of an <c>[]</c> empty object).</summary>
    public JsonElement? Data { get; init; }
}

/// <summary>Protocol constants for the weekly-checklist contract.</summary>
public static class WeeklyProtocol
{
    /// <summary>Scope the key must carry to write the checklist (<c>PUT …/weekly</c>).</summary>
    public const string WriteScope = "characters:write";

    /// <summary>Scope the key must carry to read the checklist (<c>GET …/weekly</c>).</summary>
    public const string ReadScope = "gear:read";

    /// <summary>Item key: weekly-limited tomestones acquired this week.</summary>
    public const string FieldTomesHave = "tomesHave";

    /// <summary>Item key: Custom Deliveries done this week.</summary>
    public const string FieldCustom = "custom";

    /// <summary>Item key: Savage floor 1 weekly loot obtained.</summary>
    public const string FieldF1 = "f1";

    /// <summary>Item key: Savage floor 2 weekly loot obtained.</summary>
    public const string FieldF2 = "f2";

    /// <summary>Item key: Savage floor 3 weekly loot obtained.</summary>
    public const string FieldF3 = "f3";

    /// <summary>Item key: Savage floor 4 weekly loot obtained.</summary>
    public const string FieldF4 = "f4";

    /// <summary>Upper bound for <see cref="FieldTomesHave"/> (the weekly tomestone cap).</summary>
    public const int MaxTomes = 450;

    /// <summary>Whether a wire key is a Savage-floor field (<c>f1</c>..<c>f4</c>), gated by <c>savage_lockout</c>.</summary>
    /// <param name="key">The wire field key.</param>
    /// <returns><see langword="true"/> for <c>f1</c>..<c>f4</c>.</returns>
    public static bool IsSavageField(string key) =>
        key is FieldF1 or FieldF2 or FieldF3 or FieldF4;
}
