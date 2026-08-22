namespace EorzeaArsenal.Gear;

/// <summary>
/// Maps the game's <c>ClassJob</c> row id to the API's uppercase 3-letter job code, and says which role
/// group a code belongs to.
/// </summary>
/// <remarks>
/// <para>
/// This mapping is <b>game data</b> and can only live here. The server publishes which codes it accepts
/// (<c>GET /gear/jobs</c>), but not which numeric id in the client's sheets means which code — so a job
/// the plugin cannot name cannot be sent at all, whatever that table says.
/// </para>
/// <para>
/// Identifying a job is <b>not</b> permission to send it. What may go out is decided by the table the
/// server at that address published; see <see cref="Model.JobScope"/> and the sync path. Keeping the two apart
/// is deliberate: this class answers "what is this?", never "may I?".
/// </para>
/// </remarks>
public static class JobMap
{
    /// <summary>Battle classes and jobs, base classes and Blue Mage included.</summary>
    public const string RoleCombat = "combat";

    /// <summary>Disciples of the Hand — the eight crafters.</summary>
    public const string RoleHand = "hand";

    /// <summary>Disciples of the Land — the three gatherers.</summary>
    public const string RoleLand = "land";

    // ClassJob sheet row ids → 3-letter codes. Rows 1 to 42 are exactly the 42 codes the API knows;
    // row 0 is "adventurer" and has no gearsets. The codes match the English Abbreviation column, which
    // is what the API speaks, so a client running in German still sends PLD rather than PAL.
    private static readonly Dictionary<uint, string> ByClassJobId = new()
    {
        // Base classes
        [1] = "GLA",
        [2] = "PGL",
        [3] = "MRD",
        [4] = "LNC",
        [5] = "ARC",
        [6] = "CNJ",
        [7] = "THM",
        // Disciples of the Hand
        [8] = "CRP",
        [9] = "BSM",
        [10] = "ARM",
        [11] = "GSM",
        [12] = "LTW",
        [13] = "WVR",
        [14] = "ALC",
        [15] = "CUL",
        // Disciples of the Land
        [16] = "MIN",
        [17] = "BTN",
        [18] = "FSH",
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
        // The remaining base classes, whose ids sit among the jobs
        [26] = "ACN",
        [29] = "ROG",
        // And the limited job
        [36] = "BLU",
    };

    private static readonly Dictionary<string, string> RoleByCode =
        ByClassJobId.ToDictionary(
            pair => pair.Value,
            pair => pair.Key switch
            {
                >= 8 and <= 15 => RoleHand,
                >= 16 and <= 18 => RoleLand,
                _ => RoleCombat,
            },
            StringComparer.Ordinal);

    /// <summary>Every code the plugin can name, all 42 of them.</summary>
    public static readonly IReadOnlySet<string> ValidCodes =
        new HashSet<string>(ByClassJobId.Values, StringComparer.Ordinal);

    /// <summary>Maps a <c>ClassJob</c> row id to its code, or <see langword="null"/> for an unknown id.</summary>
    /// <param name="classJobId">The game's ClassJob row id.</param>
    /// <returns>The 3-letter code, or <see langword="null"/>.</returns>
    public static string? ToCode(uint classJobId) =>
        ByClassJobId.TryGetValue(classJobId, out var code) ? code : null;

    /// <summary>Whether a code is one the plugin can name.</summary>
    /// <param name="code">The candidate uppercase 3-letter code.</param>
    /// <returns><see langword="true"/> if known.</returns>
    public static bool IsValidCode(string? code) => code is not null && ValidCodes.Contains(code);

    /// <summary>
    /// The role group of a code: <see cref="RoleCombat"/>, <see cref="RoleHand"/> or
    /// <see cref="RoleLand"/>. Unknown codes report <see langword="null"/> rather than a guess.
    /// </summary>
    /// <param name="code">The uppercase 3-letter code.</param>
    /// <returns>The role group, or <see langword="null"/>.</returns>
    /// <remarks>
    /// This is the plugin's own grouping, used to split the list for display. The server sends a richer
    /// <c>role</c> per job in its table (<c>tank</c>, <c>crafter</c>, …) and that one wins wherever both
    /// are available; this exists so a first start without a network is not one flat list.
    /// </remarks>
    public static string? RoleOf(string? code) =>
        code is not null && RoleByCode.TryGetValue(code, out var role) ? role : null;
}
