using System.Text.Json;
using System.Text.Json.Serialization;

namespace EorzeaArsenal.Model;

/// <summary>
/// What a crafter or gatherer target carries besides its pieces (<c>craft</c> on a <c>GET /gear/bis</c>
/// row, contract revision 21). Absent on a combat target.
/// </summary>
public sealed class CraftBlock
{
    /// <summary>3 High, 2 Mid, 1 Budget.</summary>
    public int Level { get; init; }

    /// <summary>The level in English, as the server names it. The window names the level itself.</summary>
    public string? LevelName { get; init; }

    /// <summary>Where the set comes from. <c>game</c> for a set with newer pieces from the game.</summary>
    public CraftSource? Source { get; init; }

    /// <summary>The set's own page at its source, or <see langword="null"/>.</summary>
    public string? SetUrl { get; init; }

    /// <summary>The patch the set was made for, or <see langword="null"/>.</summary>
    public string? Patch { get; init; }

    /// <summary><c>crafter</c> or <c>gatherer</c>.</summary>
    public string? Family { get; init; }

    /// <summary>The set's computed stats by BaseParam id, base CP or GP included, without food.</summary>
    [JsonConverter(typeof(LenientMapConverter<int>))]
    public Dictionary<string, int> Totals { get; init; } = [];

    /// <summary>For a set with newer pieces from the game: the set it was built from.</summary>
    public CraftBasedOn? BasedOn { get; init; }

    /// <summary>The slot keys whose piece comes from the game instead of <see cref="BasedOn"/>.</summary>
    public List<string> Swapped { get; init; } = [];

    /// <summary>
    /// The source to name as the attribution: the one in <see cref="BasedOn"/> when the set itself is
    /// "game", otherwise <see cref="Source"/>.
    /// </summary>
    [JsonIgnore]
    public CraftSource? Attribution =>
        string.Equals(Source?.Id, CraftSource.GameId, StringComparison.Ordinal) && BasedOn?.Source is { } based
            ? based
            : Source;
}

/// <summary>A set's source: its id, the name to show, and the link to it.</summary>
public sealed class CraftSource
{
    /// <summary>The id a set built from the game's own data carries.</summary>
    public const string GameId = "game";

    /// <summary>Stable id, such as <c>teamcraft</c> or <c>game</c>.</summary>
    public string? Id { get; init; }

    /// <summary>The name to show, from the server's data. Never hard-coded on this side.</summary>
    public string? Name { get; init; }

    /// <summary>The source's page, or <see langword="null"/> (the game data has none).</summary>
    public string? Url { get; init; }
}

/// <summary>The set a set with newer pieces from the game was built from.</summary>
public sealed class CraftBasedOn
{
    /// <summary>Its source, in the same shape as <see cref="CraftBlock.Source"/>.</summary>
    public CraftSource? Source { get; init; }

    /// <summary>Its key at that source, such as <c>high</c>.</summary>
    public string? Key { get; init; }

    /// <summary>Its patch, or <see langword="null"/>.</summary>
    public string? Patch { get; init; }
}

/// <summary>
/// The numbers <see cref="Gear.CraftStats"/> computes with (<c>craft_tables</c>, once per
/// <c>GET /gear/bis</c> answer that has a crafter or gatherer row). The same rows the website computes
/// with, so "done" cannot differ from the web by a rounding.
/// </summary>
public sealed class CraftTables
{
    /// <summary>The target pieces' stat rows, by item id.</summary>
    [JsonConverter(typeof(LenientMapConverter<CraftItemRow>))]
    public Dictionary<string, CraftItemRow> Items { get; init; } = [];

    /// <summary>The whole crafting and gathering materia table, by materia item id.</summary>
    [JsonConverter(typeof(LenientMapConverter<CraftMateriaRow>))]
    public Dictionary<string, CraftMateriaRow> Materia { get; init; } = [];
}

/// <summary>One piece's stat row: base values, HQ bonus and the cap per stat, by BaseParam id.</summary>
public sealed class CraftItemRow
{
    /// <summary>The contract slot key, or <see langword="null"/>.</summary>
    public string? Slot { get; init; }

    /// <summary>The item level, or <see langword="null"/>.</summary>
    public int? Ilvl { get; init; }

    /// <summary>Whether the item exists in HQ at all.</summary>
    [JsonPropertyName("canHq")]
    public bool CanHq { get; init; }

    /// <summary>The guaranteed materia slots.</summary>
    public int Slots { get; init; }

    /// <summary>Whether the piece can be overmelded.</summary>
    public bool Adv { get; init; }

    /// <summary>NQ values by BaseParam id.</summary>
    [JsonConverter(typeof(LenientMapConverter<int>))]
    public Dictionary<string, int> Nq { get; init; } = [];

    /// <summary>The HQ bonus by BaseParam id.</summary>
    [JsonConverter(typeof(LenientMapConverter<int>))]
    public Dictionary<string, int> Hqv { get; init; } = [];

    /// <summary>The most a stat may reach on this piece, by BaseParam id; materia beyond it is lost.</summary>
    [JsonConverter(typeof(LenientMapConverter<int>))]
    public Dictionary<string, int> Cap { get; init; } = [];
}

/// <summary>One materia: the stat it gives, its grade, and how much.</summary>
public sealed class CraftMateriaRow
{
    /// <summary>The BaseParam id it adds to.</summary>
    public int Param { get; init; }

    /// <summary>The grade, 1 to 12.</summary>
    public int Grade { get; init; }

    /// <summary>How much it adds.</summary>
    public int Value { get; init; }
}

/// <summary>
/// Reads a JSON object as a dictionary, and an array as an empty one.
/// </summary>
/// <remarks>
/// The server is PHP, and PHP writes an empty map as <c>[]</c> rather than <c>{}</c>. A strict
/// dictionary throws on that, and one empty table would then fail the whole BiS read. A non-empty array
/// carries no keys to read, so it is skipped the same way.
/// </remarks>
/// <typeparam name="TValue">The value type.</typeparam>
public sealed class LenientMapConverter<TValue> : JsonConverter<Dictionary<string, TValue>>
{
    /// <inheritdoc />
    public override Dictionary<string, TValue> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                var map = new Dictionary<string, TValue>(StringComparer.Ordinal);
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var key = reader.GetString() ?? string.Empty;
                    reader.Read();
                    var value = JsonSerializer.Deserialize<TValue>(ref reader, options);
                    if (value is not null)
                    {
                        map[key] = value;
                    }
                }

                return map;
            case JsonTokenType.StartArray:
                reader.Skip();
                return [];
            default:
                reader.Skip();
                return [];
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Dictionary<string, TValue> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, item) in value)
        {
            writer.WritePropertyName(key);
            JsonSerializer.Serialize(writer, item, options);
        }

        writer.WriteEndObject();
    }
}
