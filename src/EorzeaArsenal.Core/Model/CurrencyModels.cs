namespace EorzeaArsenal.Model;

/// <summary>
/// Body of <c>POST /inventory/currencies</c> (server 1.5): a full snapshot of the currencies
/// the server lists in <c>currencies</c> of <c>GET /gear/tracked-items</c>, zeros included.
/// </summary>
/// <remarks>
/// The server replaces the character's whole currency stock with it, so an id left out is cleared. That
/// is why <see cref="Stock"/> always carries every listed id, a 0 for one the character does not hold.
/// </remarks>
public sealed class CurrencyStockRequest
{
    /// <summary>The plugin build that produced this write (see <see cref="ProtocolConstants.PluginVersion"/>).</summary>
    public string PluginVersion { get; init; } = ProtocolConstants.PluginVersion;

    /// <summary>The character, found by <c>cid_hash</c>. It must exist for the caller (a gear sync creates it).</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>One row per listed id, in ascending id order.</summary>
    public required IReadOnlyList<CurrencyStockRow> Stock { get; init; }
}

/// <summary>One currency count: the game's item id and the count the game shows.</summary>
public sealed class CurrencyStockRow
{
    /// <summary>The currency's item id, as listed by the server.</summary>
    public required int ItemId { get; init; }

    /// <summary>The count the game shows, 0 when none is held.</summary>
    public required int Count { get; init; }
}

/// <summary>Response of <c>POST /inventory/currencies</c>.</summary>
public sealed class CurrencyStockResult
{
    /// <summary>Always <c>"ok"</c> on success.</summary>
    public string? Status { get; init; }

    /// <summary>The server's id of the character the stock was stored for.</summary>
    public string? CharacterId { get; init; }

    /// <summary>How many ids were stored with a count above 0.</summary>
    public int Rows { get; init; }
}

/// <summary>The limits of the currency sync, as the contract states them.</summary>
public static class CurrencyProtocol
{
    /// <summary>The most rows the server takes in one snapshot.</summary>
    public const int MaxRows = 500;

    /// <summary>Calls per hour per account, a bucket of its own.</summary>
    public const int MaxCallsPerHour = 30;
}
