using EorzeaArsenal.Model;

namespace EorzeaArsenal.Core;

/// <summary>
/// What a row with no gearset in game actually is, in the one sentence somebody needs before they can
/// decide anything about it.
/// </summary>
public enum OrphanVerdict
{
    /// <summary>Built on the website and never in game. The server refuses to delete one of these.</summary>
    HandMade,

    /// <summary>
    /// Handed to the website on purpose. Its contents are frozen at the moment of the handover, and the
    /// way back exists and is one button.
    /// </summary>
    Released,

    /// <summary>Nothing the player still has resembles it, so there is nothing to fall back on.</summary>
    Unique,

    /// <summary>Every occupied slot agrees down to the materia: this row is another row's copy.</summary>
    Copy,

    /// <summary>Every slot carries the same piece, but not the same melds.</summary>
    SameGearOtherMateria,

    /// <summary>It resembles a set that is still there without being the same gear.</summary>
    Similar,
}

/// <summary>What the window should say about one row, and the verb it may recommend.</summary>
public sealed class OrphanAdvice
{
    /// <summary>What the row is.</summary>
    public required OrphanVerdict Verdict { get; init; }

    /// <summary>The set it was held against, when there was one.</summary>
    public string? OtherName { get; init; }

    /// <summary>Its identity, so the interface can say which set that is when two share a name.</summary>
    public string? OtherUid { get; init; }

    /// <summary>How many slots carry a different piece, for the sentence that names the difference.</summary>
    public int DifferentSlots { get; init; }

    /// <summary>
    /// The verb to name outright, or <see langword="null"/> where the honest answer is that only the
    /// player can decide.
    /// </summary>
    public string? Recommended { get; init; }
}

/// <summary>
/// Turns a row and its comparison into advice. <b>This is the plugin's own judgement, not the server's:</b>
/// the server proposes an attribution for a held row and deliberately says nothing about an orphan. What it
/// does supply is <c>similar[]</c>, and its stated purpose is to answer delete-or-keep without anybody
/// having to remember a name from months ago. This is that answer, made explicit.
/// </summary>
/// <remarks>
/// <para>
/// A verb is recommended only where the recommendation cannot be wrong. That is a short list: a row that is
/// a copy of one still present with nothing hanging on it, and a hand-made row that has never been put
/// aside. Everywhere else the sentence names both options and the question that decides between them,
/// which is more useful than a confident guess and is the only honest thing available.
/// </para>
/// <para>
/// In particular no verb is recommended while a pinned target or a team share hangs on the row. Those are
/// not part of the comparison and a recommendation that ignores them would be telling somebody to throw
/// away the one thing the window exists to protect.
/// </para>
/// </remarks>
public static class OrphanAdvisor
{
    /// <summary>Reads a row and the comparison that was drawn for it.</summary>
    /// <param name="row">The row being decided about.</param>
    /// <param name="pairs">
    /// The comparison against the strongest resemblance, or empty when the row resembles nothing.
    /// </param>
    /// <returns>The advice.</returns>
    public static OrphanAdvice For(OrphanRow row, IReadOnlyList<SlotPair> pairs)
    {
        // Released before hand-made, because both are `manual` and only the mark tells them apart. Getting
        // the order wrong is not a wording slip: a released row would read as somebody else's set, and the
        // one button on it would promise the wrong thing.
        if (row.WasReleased)
        {
            // Nothing is recommended. Releasing was a decision somebody made on purpose, and the way back
            // is offered rather than urged.
            return new OrphanAdvice { Verdict = OrphanVerdict.Released };
        }

        // A hand-made row next, because it changes what the buttons even mean: the server rejects delete
        // on one of these, always. Advice naming a verb that cannot be carried out is worse than none.
        if (!row.IsFromPlugin)
        {
            return new OrphanAdvice
            {
                Verdict = OrphanVerdict.HandMade,
                Recommended = row.IsPutAside ? null : ReviewAction.Ignore,
            };
        }

        if (row.Similar.Count == 0 || pairs.Count == 0)
        {
            return new OrphanAdvice { Verdict = OrphanVerdict.Unique };
        }

        var other = row.Similar[0].Name;
        var otherUid = row.Similar[0].SetUid;
        var (_, materia, different) = SetComparison.Counts(pairs);

        if (different > 0)
        {
            return new OrphanAdvice
            {
                Verdict = OrphanVerdict.Similar,
                OtherName = other,
                OtherUid = otherUid,
                DifferentSlots = different,
            };
        }

        if (materia > 0)
        {
            return new OrphanAdvice
            {
                Verdict = OrphanVerdict.SameGearOtherMateria,
                OtherName = other,
                OtherUid = otherUid,
            };
        }

        return new OrphanAdvice
        {
            Verdict = OrphanVerdict.Copy,
            OtherName = other,
            OtherUid = otherUid,
            Recommended = Attached(row) ? null : ReviewAction.Delete,
        };
    }

    /// <summary>
    /// Whether something hangs on the row that the comparison knows nothing about, and that deleting would
    /// take with it.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns><see langword="true"/> when a pinned target or a team share is on it.</returns>
    private static bool Attached(OrphanRow row) => row.HasPin || row.HasTeamShare;
}
