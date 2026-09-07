using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// Which live gearset holds which identity, both ways round, plus the positions nobody could attribute.
/// </summary>
/// <param name="UidByIndex">Live position to identity, for a window that starts from a gearset.</param>
/// <param name="IndexByUid">Identity to live position, for a window that starts from a BiS target.</param>
/// <param name="Undecided">
/// Live positions left without an identity because the answer could not be trusted. Positions rather than
/// a count: the status window says how many, and the gear window has to know which, since such a set falls
/// into the "no target pinned" list for want of a match and must not then be told to pin one it has.
/// </param>
public readonly record struct LivePositionTables(
    IReadOnlyDictionary<int, string> UidByIndex,
    IReadOnlyDictionary<string, int> IndexByUid,
    IReadOnlySet<int> Undecided);

/// <summary>
/// Turns the live gearset list plus the identity mapping into the two lookup tables the windows read.
/// </summary>
/// <remarks>
/// Pulled out of the service that used to build it inline, because the interesting part is not the
/// looping but the rule below about an identity claimed twice, and that rule was worth proving rather
/// than reading.
/// </remarks>
public static class LivePositionMap
{
    /// <summary>
    /// Builds both tables, withdrawing every claim that cannot be sound.
    /// </summary>
    /// <param name="sets">The gearsets as the game reports them.</param>
    /// <param name="resolve">Resolves one live gearset to the identity the server gave it.</param>
    /// <returns>The tables and the positions left undecided.</returns>
    /// <remarks>
    /// <para>
    /// The mapping declines where two remembered rows fit one live gearset, and that is the only doubt it
    /// can see. The other direction is invisible to it: two live gearsets that each fit the same remembered
    /// row are two separate questions, and each gets a single confident answer. Copy a gearset in game and
    /// the copy is indistinguishable from the original until the next push tells the server it exists.
    /// </para>
    /// <para>
    /// One identity cannot sit in two places, so where it appears to, neither claim is worth anything and
    /// both are withdrawn. Writing the second over the first, which is what a plain assignment does, kept
    /// the table full and pointed it at whichever set came last, with nothing anywhere suggesting a doubt.
    /// A wrong set number and somebody else's BiS target read exactly like a correct one.
    /// </para>
    /// </remarks>
    public static LivePositionTables Build(IEnumerable<GearsetDto> sets, Func<GearsetDto, GearsetIdentityMatch> resolve)
    {
        ArgumentNullException.ThrowIfNull(sets);
        ArgumentNullException.ThrowIfNull(resolve);

        var uidByIndex = new Dictionary<int, string>();
        var indexByUid = new Dictionary<string, int>(StringComparer.Ordinal);
        var undecided = new HashSet<int>();
        var claimedTwice = new HashSet<string>(StringComparer.Ordinal);

        foreach (var set in sets)
        {
            var match = resolve(set);
            if (!match.IsResolved)
            {
                // A refusal is recorded rather than passed over. Declining is the right answer where two
                // gearsets cannot be told apart, and it is also why the BiS comparison for that set goes
                // empty. Silence there reads as a fault; this is what lets the interface say what happened.
                if (match.WasAmbiguous)
                {
                    undecided.Add(set.GearIndex);
                }

                continue;
            }

            var uid = match.SetUid!;

            if (claimedTwice.Contains(uid))
            {
                // A third or later set with an identity two others already withdrew. It must not put it
                // back, and it is no better off than they were.
                undecided.Add(set.GearIndex);
                continue;
            }

            if (indexByUid.TryGetValue(uid, out var taken))
            {
                undecided.Add(set.GearIndex);
                undecided.Add(taken);
                indexByUid.Remove(uid);
                claimedTwice.Add(uid);
                continue;
            }

            uidByIndex[set.GearIndex] = uid;
            indexByUid[uid] = set.GearIndex;
        }

        foreach (var index in undecided)
        {
            uidByIndex.Remove(index);
        }

        return new LivePositionTables(uidByIndex, indexByUid, undecided);
    }
}
