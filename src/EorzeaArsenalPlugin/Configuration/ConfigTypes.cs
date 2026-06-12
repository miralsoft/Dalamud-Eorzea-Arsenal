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
