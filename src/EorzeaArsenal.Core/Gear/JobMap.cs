namespace EorzeaArsenal.Gear;

/// <summary>
/// Maps the game's <c>ClassJob</c> row id to the API's uppercase 3-letter job code, restricted
/// to the 21-job whitelist. Base classes (GLA, MRD, …), Blue Mage and any unknown id map to
/// <see langword="null"/> so the caller can skip them — an unknown job would otherwise be a 422.
/// </summary>
public static class JobMap
{
    // ClassJob sheet row ids → whitelisted 3-letter codes (the 21 combat jobs only).
    private static readonly Dictionary<uint, string> ByClassJobId = new()
    {
        // Tanks
        [19] = "PLD",
        [21] = "WAR",
        [32] = "DRK",
        [37] = "GNB",
        // Healers
        [24] = "WHM",
        [28] = "SCH",
        [33] = "AST",
        [40] = "SGE",
        // Melee DPS
        [20] = "MNK",
        [22] = "DRG",
        [30] = "NIN",
        [34] = "SAM",
        [39] = "RPR",
        [41] = "VPR",
        // Physical ranged DPS
        [23] = "BRD",
        [31] = "MCH",
        [38] = "DNC",
        // Magical ranged DPS
        [25] = "BLM",
        [27] = "SMN",
        [35] = "RDM",
        [42] = "PCT",
    };

    /// <summary>The set of valid uppercase job codes (the whitelist).</summary>
    public static readonly IReadOnlySet<string> ValidCodes =
        new HashSet<string>(ByClassJobId.Values, StringComparer.Ordinal);

    /// <summary>Maps a <c>ClassJob</c> row id to its whitelisted code, or <see langword="null"/>.</summary>
    /// <param name="classJobId">The game's ClassJob row id.</param>
    /// <returns>The 3-letter code, or <see langword="null"/> if not a whitelisted job.</returns>
    public static string? ToCode(uint classJobId) =>
        ByClassJobId.TryGetValue(classJobId, out var code) ? code : null;

    /// <summary>Whether a code is part of the job whitelist.</summary>
    /// <param name="code">The candidate uppercase 3-letter code.</param>
    /// <returns><see langword="true"/> if whitelisted.</returns>
    public static bool IsValidCode(string? code) => code is not null && ValidCodes.Contains(code);
}
