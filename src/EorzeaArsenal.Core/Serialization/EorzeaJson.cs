using System.Text.Json;
using System.Text.Json.Serialization;

namespace EorzeaArsenal.Serialization;

/// <summary>
/// Central <see cref="JsonSerializerOptions"/> for all Eorzea Arsenal API traffic.
/// </summary>
/// <remarks>
/// <para>
/// The API speaks <c>snake_case</c> JSON, so property names use
/// <see cref="JsonNamingPolicy.SnakeCaseLower"/>. Equipment-slot keys inside the
/// <c>items</c> object are PascalCase (<c>Weapon</c>, <c>Head</c>, …) and must stay
/// literal, so <see cref="JsonSerializerOptions.DictionaryKeyPolicy"/> is deliberately
/// left <see langword="null"/>.
/// </para>
/// <para>
/// Deserialization ignores unknown members (the System.Text.Json default), which
/// satisfies the forward-compatibility rule R14: a future API field never breaks an
/// out-of-date plugin. Optional fields are omitted on write so we never send empty noise.
/// </para>
/// </remarks>
public static class EorzeaJson
{
    /// <summary>The shared, immutable serializer options used for every request and response.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = null,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };
}
