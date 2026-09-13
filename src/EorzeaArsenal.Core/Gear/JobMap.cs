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
    //
    // This is the floor and not the whole answer. A job the game adds after a release of this plugin is
    // not in here, and a job that cannot be named is skipped when the gearsets are read, so it vanishes
    // from the list without a word. LearnFromGame closes that: the same Abbreviation column this table
    // was transcribed from is right there at runtime, and reading it beats transcribing it again.
    private static readonly Dictionary<uint, string> CompiledFloor = new()
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

    // Swapped whole rather than mutated: the gearset read runs on the framework thread while the sheet is
    // learned from once at startup, and the three views have to agree with each other at every instant. A
    // code in ValidCodes whose id is not yet in ById would let a set through the sanitizer and then fail
    // to be named.
    private static volatile Tables _tables = Tables.From(CompiledFloor);

    /// <summary>The mapping as it was compiled in, before the game had anything to add.</summary>
    /// <remarks>
    /// The floor never changes and never shrinks. It is what a machine with no game data still knows, and
    /// it is what the contract test measures: the 42 codes the server's table lists.
    /// </remarks>
    public static IReadOnlyDictionary<uint, string> Floor => CompiledFloor;

    /// <summary>Every code the plugin can name: the 42 of the floor, plus anything learned from the game.</summary>
    public static IReadOnlySet<string> ValidCodes => _tables.Codes;

    /// <summary>Maps a <c>ClassJob</c> row id to its code, or <see langword="null"/> for an unknown id.</summary>
    /// <param name="classJobId">The game's ClassJob row id.</param>
    /// <returns>The 3-letter code, or <see langword="null"/>.</returns>
    public static string? ToCode(uint classJobId) =>
        _tables.ById.TryGetValue(classJobId, out var code) ? code : null;

    /// <summary>Whether a code is one the plugin can name.</summary>
    /// <param name="code">The candidate uppercase 3-letter code.</param>
    /// <returns><see langword="true"/> if known.</returns>
    public static bool IsValidCode(string? code) => code is not null && _tables.Codes.Contains(code);

    /// <summary>
    /// Teaches the map the <c>ClassJob</c> rows the game has and the floor does not.
    /// </summary>
    /// <param name="fromGame">Row id and English abbreviation, as the game's own sheet spells them.</param>
    /// <returns>The codes this call added, in ascending row order, for the log and the report.</returns>
    /// <remarks>
    /// <para>
    /// Naming a job is still not permission to send it. What may leave the machine is decided by the table
    /// the server published, so a job learned here is filtered out of every push until that table lists
    /// it. The mechanism therefore fails in the safe direction, which is the only reason reading the sheet
    /// is allowed to widen anything at all.
    /// </para>
    /// <para>
    /// It only ever fills gaps. A row the floor already names keeps the name the floor gave it, so no
    /// quirk in the sheet and no future renaming in the game can turn PLD into something else under a
    /// running plugin. Called once at startup; safe to call again, and the same input yields the same
    /// tables.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> LearnFromGame(IEnumerable<KeyValuePair<uint, string>> fromGame)
    {
        var merged = Merge(CompiledFloor, fromGame);
        var added = merged.Where(pair => !CompiledFloor.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .ToArray();

        _tables = Tables.From(merged);
        return added;
    }

    /// <summary>
    /// The floor with the game's unknown rows folded in, as a value: the whole decision, testable without
    /// touching what a running plugin is using.
    /// </summary>
    /// <param name="floor">The compiled mapping.</param>
    /// <param name="fromGame">Row id and abbreviation, unvalidated, as read from the sheet.</param>
    /// <returns>A new mapping. The floor's entries are in it unchanged.</returns>
    /// <remarks>
    /// Four rejections, each for something a sheet really contains. Row 0 is "adventurer" and has no
    /// gearsets. An empty abbreviation is a placeholder row. Anything that is not three letters is not a
    /// code the API speaks, and sending one would fail the whole push rather than that one set. And a code
    /// some other row already carries is dropped, because the role lookup is keyed by code and two rows
    /// claiming one code is a collision, not a widening.
    /// </remarks>
    public static IReadOnlyDictionary<uint, string> Merge(
        IReadOnlyDictionary<uint, string> floor,
        IEnumerable<KeyValuePair<uint, string>> fromGame)
    {
        var merged = new Dictionary<uint, string>(floor);
        var taken = new HashSet<string>(floor.Values, StringComparer.Ordinal);

        foreach (var (id, raw) in fromGame.OrderBy(pair => pair.Key))
        {
            if (id == 0 || merged.ContainsKey(id) || !IsWellFormedCode(raw) || !taken.Add(raw))
            {
                continue;
            }

            merged[id] = raw;
        }

        return merged;
    }

    /// <summary>Whether an abbreviation is shaped like a code the API speaks.</summary>
    /// <param name="code">The candidate.</param>
    /// <returns><see langword="true"/> for exactly three uppercase ASCII letters.</returns>
    private static bool IsWellFormedCode(string? code) =>
        code is { Length: 3 } && code.All(c => c is >= 'A' and <= 'Z');

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
        code is not null && _tables.RoleByCode.TryGetValue(code, out var role) ? role : null;

    /// <summary>The three views of one mapping, always built together so they cannot disagree.</summary>
    /// <param name="ById">Row id to code.</param>
    /// <param name="Codes">Every code the mapping names.</param>
    /// <param name="RoleByCode">The plugin's own role grouping, by code.</param>
    private sealed record Tables(
        IReadOnlyDictionary<uint, string> ById,
        IReadOnlySet<string> Codes,
        IReadOnlyDictionary<string, string> RoleByCode)
    {
        /// <summary>Builds all three from one mapping.</summary>
        /// <param name="byId">Row id to code, already merged and free of duplicate codes.</param>
        /// <returns>The views.</returns>
        /// <remarks>
        /// The role comes from the row id, because that is where the game puts the distinction: rows 8 to
        /// 15 are the eight crafters and 16 to 18 the three gatherers. Everything else is combat, which is
        /// also the right answer for a job the game adds later: the two ranges are full and closed.
        /// </remarks>
        public static Tables From(IReadOnlyDictionary<uint, string> byId) => new(
            byId,
            new HashSet<string>(byId.Values, StringComparer.Ordinal),
            byId.ToDictionary(
                pair => pair.Value,
                pair => pair.Key switch
                {
                    >= 8 and <= 15 => RoleHand,
                    >= 16 and <= 18 => RoleLand,
                    _ => RoleCombat,
                },
                StringComparer.Ordinal));
    }

    // The nine classes a job grows out of. They have gearsets and they are sent, but no catalogue lists
    // targets for them: a BiS set belongs to the job, and the class is what you are before you have one.
    private static readonly IReadOnlySet<string> BaseClasses = new HashSet<string>(StringComparer.Ordinal)
    {
        "GLA", "MRD", "CNJ", "THM", "ARC", "LNC", "PGL", "ROG", "ACN",
    };

    /// <summary>Whether a code is one of the nine base classes rather than a job.</summary>
    /// <param name="code">The uppercase 3-letter code.</param>
    /// <returns><see langword="true"/> for a base class.</returns>
    public static bool IsBaseClass(string? code) => code is not null && BaseClasses.Contains(code);

    /// <summary>
    /// Whether a BiS catalogue can be expected to have lists for this job at all.
    /// </summary>
    /// <param name="code">The uppercase 3-letter code.</param>
    /// <returns><see langword="false"/> for hand, land and the base classes.</returns>
    /// <remarks>
    /// This is what separates "nothing is pinned yet", which the player can act on in the web app, from
    /// "there is nothing to pin", which nobody can. Telling them apart matters because the second is not a
    /// fault and the first reads like one. Hand and land lose this exemption when that feature lands, and
    /// then this is the single place that changes.
    /// </remarks>
    public static bool HasBisCatalogue(string? code) =>
        RoleOf(code) == RoleCombat && !IsBaseClass(code);
}
