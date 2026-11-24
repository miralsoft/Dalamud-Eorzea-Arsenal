namespace EorzeaArsenal.Plugin.Configuration;

/// <summary>How much the plugin writes to the Dalamud log.</summary>
public enum LogVerbosity
{
    /// <summary>Only warnings and errors.</summary>
    Quiet = 0,

    /// <summary>Informational messages, warnings and errors (default).</summary>
    Normal = 1,

    /// <summary>Everything (reserved for future debug detail).</summary>
    Verbose = 2,
}

/// <summary>Where the BiS information for a hovered item is shown.</summary>
public enum BisTooltipPlacement
{
    /// <summary>In the plugin's own window docked to the game's tooltip (the default, as before 1.3.0).</summary>
    Overlay = 0,

    /// <summary>Appended to the game's own item tooltip, below its description.</summary>
    Tooltip = 1,

    /// <summary>Both: the short form in the game's tooltip and the full one in the window.</summary>
    Both = 2,
}

/// <summary>
/// Per-character push opt-in. The plugin records each character it sees (keyed by its
/// privacy-preserving <c>cid_hash</c>, never the raw ContentId) so the user can choose which of
/// their characters may be pushed (briefing §7).
/// </summary>
[Serializable]
public sealed class CharacterOptIn
{
    /// <summary>Character full name (for display only).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Home world name (for display only).</summary>
    public string World { get; set; } = string.Empty;

    /// <summary>Whether this character may be pushed.</summary>
    public bool Enabled { get; set; } = true;
}
