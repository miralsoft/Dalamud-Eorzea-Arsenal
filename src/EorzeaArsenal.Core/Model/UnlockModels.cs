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
}

/// <summary>The limits of the unlock report.</summary>
public static class UnlockProtocol
{
    /// <summary>The most ids <c>checked</c> may carry in one call.</summary>
    public const int MaxChecked = 2000;

    /// <summary>Calls per hour per account.</summary>
    public const int MaxCallsPerHour = 30;
}
