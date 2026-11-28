namespace EorzeaArsenal.Model;

/// <summary>
/// Body of <c>PUT /me/unlocks</c> (server 1.5, goals): which of the goal items that unlock a mount, a minion,
/// an orchestrion roll or a Triple Triad card the plugin looked at, and which of them the character learned.
/// </summary>
/// <remarks>
/// For every id in <see cref="Checked"/> the server stores "learned" or clears it; an id not in it is left
/// as it was, so the plugin sends only what it could look at.
/// </remarks>
public sealed class UnlocksRequest
{
    /// <summary>The character, found by <c>cid_hash</c>; it must exist for the caller (a gear sync creates it).</summary>
    public required CharacterDto Character { get; init; }

    /// <summary>The unlock items looked at, ascending, at most <see cref="UnlockProtocol.MaxChecked"/>.</summary>
    public required IReadOnlyList<int> Checked { get; init; }

    /// <summary>Those of them whose unlock the character has learned.</summary>
    public required IReadOnlyList<int> Learned { get; init; }

    /// <summary>The collection's kind (<see cref="UnlockKinds"/>); left out in the goals mode.</summary>
    public string? Kind { get; init; }

    /// <summary>With <see cref="Kind"/>: <c>checked</c> is every item of the kind, so anything else stored is cleared.</summary>
    public bool? Full { get; init; }
}

/// <summary>Response of <c>PUT /me/unlocks</c>.</summary>
public sealed class UnlocksResult
{
    /// <summary>Always <c>"ok"</c> on success.</summary>
    public string? Status { get; init; }

    /// <summary>The server's id of the character.</summary>
    public string? CharacterId { get; init; }

    /// <summary>How many of the checked ids the server kept as unlock items.</summary>
    public int Checked { get; init; }

    /// <summary>The state per kind after the call (with <c>unlock-collection</c>).</summary>
    public Dictionary<string, UnlockKindState>? Kinds { get; init; }
}

/// <summary>What the server holds of one kind for a character.</summary>
public sealed class UnlockKindState
{
    /// <summary>How many items of the kind are stored as learned.</summary>
    public int Learned { get; init; }

    /// <summary>How many items of the kind the server's catalog knows.</summary>
    public int Known { get; init; }

    /// <summary>When the plugin last reported some of the kind.</summary>
    public string? CheckedAt { get; init; }

    /// <summary>When the plugin last reported all of the kind.</summary>
    public string? FullAt { get; init; }
}

/// <summary>Response of <c>GET /me/unlocks</c>.</summary>
public sealed class MyUnlocksResponse
{
    /// <summary>The state.</summary>
    public MyUnlocks? Data { get; init; }
}

/// <summary>The character and its state per kind.</summary>
public sealed class MyUnlocks
{
    /// <summary>The server's id of the character.</summary>
    public string? CharacterId { get; init; }

    /// <summary>The state per kind.</summary>
    public Dictionary<string, UnlockKindState> Kinds { get; init; } = [];
}

/// <summary>The four kinds of the collection, as the server names them.</summary>
public static class UnlockKinds
{
    /// <summary>Mounts.</summary>
    public const string Mount = "mount";

    /// <summary>Minions.</summary>
    public const string Minion = "minion";

    /// <summary>Orchestrion rolls.</summary>
    public const string Orchestrion = "orchestrion";

    /// <summary>Triple Triad cards.</summary>
    public const string Card = "card";

    /// <summary>Emotes (since <c>unlock-kinds</c>).</summary>
    public const string Emote = "emote";

    /// <summary>Hairstyles (since <c>unlock-kinds</c>).</summary>
    public const string Hairstyle = "hairstyle";

    /// <summary>Chocobo bardings (since <c>unlock-kinds</c>).</summary>
    public const string Barding = "barding";

    /// <summary>Fashion accessories (since <c>unlock-kinds</c>).</summary>
    public const string Accessory = "accessory";

    /// <summary>Facewear (since <c>unlock-kinds</c>).</summary>
    public const string Facewear = "facewear";

    /// <summary>Framer's kits, "Portraitmaterial" (since <c>unlock-kinds</c>).</summary>
    public const string Portrait = "portrait";

    /// <summary>The first four, the kinds every server with <c>unlock-collection</c> takes, in the order they are synced.</summary>
    public static IReadOnlyList<string> All { get; } = [Mount, Minion, Orchestrion, Card];

    /// <summary>All ten, for a server that lists <c>unlock-kinds</c>, in the order they are synced.</summary>
    public static IReadOnlyList<string> Extended { get; } = [Mount, Minion, Orchestrion, Card, Emote, Hairstyle, Barding, Accessory, Facewear, Portrait];
}

/// <summary>The limits of the unlock report.</summary>
public static class UnlockProtocol
{
    /// <summary>The most ids <c>checked</c> may carry in one call.</summary>
    public const int MaxChecked = 2000;

    /// <summary>Calls per hour per account, the goals mode and the collection together (60 since 2026-11-28).</summary>
    public const int MaxCallsPerHour = 60;
}
