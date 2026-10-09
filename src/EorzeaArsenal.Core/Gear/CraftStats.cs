using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>One target materia slot: whether the worn piece fills it, and with which materia.</summary>
/// <param name="Filled">Whether the slot counts as done.</param>
/// <param name="Worn">
/// The worn materia it was matched with; for an unfilled slot, what is left in the worn piece (the same
/// slot first), so the window can say what sits there now. 0 when nothing is left.
/// </param>
public readonly record struct MeldSlot(bool Filled, int Worn);

/// <summary>
/// Crafter and gatherer stats: what a piece adds up to with HQ and the meld cap, and whether a worn piece
/// fills the target's materia slots. A port of <c>pieceStats</c>, <c>meldSatisfies</c> and
/// <c>meldMatch</c> in the website's <c>public/assets/js/data/craft-stats.js</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ported as it stands, and on purpose not "improved": the web and the server's generator compute with
/// that file, and a second implementation that differs by a rounding would show a player "missing" where
/// the web says "done". The numbers come from the server too (<see cref="CraftTables"/>), so nothing here
/// derives a cap from the game data. The shared cases in <c>docs/plugin/samples/meld-match-cases.json</c>
/// of the server repository run against this as they run against the web.
/// </para>
/// <para>
/// For crafter and gatherer targets only. A combat target keeps the plain comparison: the same materia,
/// order irrelevant.
/// </para>
/// </remarks>
public static class CraftStats
{
    /// <summary>
    /// One piece's stats: its base value (with the HQ bonus when the piece is HQ and the item can be), plus
    /// its materia, each stat capped. A piece already above its cap keeps its base.
    /// </summary>
    /// <param name="item">The item's stat row, or <see langword="null"/>.</param>
    /// <param name="hq">Whether the piece is HQ.</param>
    /// <param name="materia">The materia item ids in the piece.</param>
    /// <param name="table">The materia rows by item id.</param>
    /// <returns>BaseParam id to value; empty for an unknown item.</returns>
    public static Dictionary<int, int> PieceStats(
        CraftItemRow? item,
        bool hq,
        IReadOnlyList<int> materia,
        IReadOnlyDictionary<string, CraftMateriaRow> table)
    {
        var result = new Dictionary<int, int>();
        if (item is null)
        {
            return result;
        }

        var isHq = hq && item.CanHq;
        var add = new Dictionary<int, int>();
        foreach (var id in materia)
        {
            if (Row(table, id) is { } m)
            {
                add[m.Param] = add.GetValueOrDefault(m.Param) + m.Value;
            }
        }

        var parameters = new HashSet<int>(add.Keys);
        foreach (var key in item.Nq.Keys)
        {
            if (int.TryParse(key, out var p))
            {
                parameters.Add(p);
            }
        }

        foreach (var p in parameters)
        {
            var key = p.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var baseValue = item.Nq.GetValueOrDefault(key) + (isHq ? item.Hqv.GetValueOrDefault(key) : 0);
            var melded = baseValue + add.GetValueOrDefault(p);
            result[p] = item.Cap.TryGetValue(key, out var cap)
                ? Math.Min(melded, Math.Max(baseValue, cap))
                : melded;
        }

        return result;
    }

    /// <summary>
    /// Does the worn materia do the job of the one the target names? The same materia, or one of the same
    /// stat that gives at least as much. Compared by value, not by grade: the grades alternate.
    /// </summary>
    /// <param name="worn">The materia item id in the slot (0 = empty).</param>
    /// <param name="want">The materia item id the target names.</param>
    /// <param name="table">The materia rows by item id.</param>
    /// <returns>Whether it does.</returns>
    public static bool MeldSatisfies(int worn, int want, IReadOnlyDictionary<string, CraftMateriaRow> table)
    {
        if (worn == 0)
        {
            return false;
        }

        if (worn == want)
        {
            return true;
        }

        return Row(table, worn) is { } w && Row(table, want) is { } t && w.Param == t.Param && w.Value >= t.Value;
    }

    /// <summary>
    /// Matches a worn piece's materia to the target's, slot by slot. The order does not decide whether a
    /// piece is done. In this order, each step only with what the steps before left over (the same slot
    /// first, then any): the same materia; a materia of the same stat that gives at least as much; a
    /// materia of the same stat while the piece still reaches, in that stat, the value the target's
    /// materia give it.
    /// </summary>
    /// <param name="item">The target piece's stat row, or <see langword="null"/>.</param>
    /// <param name="targetHq">The target's HQ state. Both sides are computed at it, so the answer is about materia, not HQ.</param>
    /// <param name="target">The target's materia, in slot order.</param>
    /// <param name="worn">The worn piece's materia.</param>
    /// <param name="table">The materia rows by item id.</param>
    /// <returns>One entry per target slot.</returns>
    public static IReadOnlyList<MeldSlot> MeldMatch(
        CraftItemRow? item,
        bool targetHq,
        IReadOnlyList<int> target,
        IReadOnlyList<int> worn,
        IReadOnlyDictionary<string, CraftMateriaRow> table)
    {
        var t = item is not null ? PieceStats(item, targetHq, target, table) : [];
        var w = item is not null ? PieceStats(item, targetHq, worn, table) : [];
        var used = new bool[worn.Count];
        var result = new MeldSlot[target.Count];

        int Take(int i, Func<int, bool> fits)
        {
            // The same slot first, then any, as `[i, ...have.keys()]` walks it.
            for (var n = -1; n < worn.Count; n++)
            {
                var k = n < 0 ? i : n;
                if (k < worn.Count && !used[k] && worn[k] > 0 && fits(worn[k]))
                {
                    used[k] = true;
                    return worn[k];
                }
            }

            return 0;
        }

        void Pass(Func<int, int, bool> fits)
        {
            for (var i = 0; i < target.Count; i++)
            {
                if (result[i].Filled)
                {
                    continue;
                }

                var m = target[i];
                var got = Take(i, x => fits(x, m));
                if (got != 0)
                {
                    result[i] = new MeldSlot(true, got);
                }
            }
        }

        Pass((x, m) => x == m);
        Pass((x, m) => MeldSatisfies(x, m, table));
        Pass((x, m) => Row(table, m) is { } tm &&
            Row(table, x)?.Param == tm.Param &&
            w.GetValueOrDefault(tm.Param) >= t.GetValueOrDefault(tm.Param));

        for (var i = 0; i < target.Count; i++)
        {
            if (!result[i].Filled)
            {
                result[i] = new MeldSlot(false, Take(i, _ => true));
            }
        }

        return result;
    }

    private static CraftMateriaRow? Row(IReadOnlyDictionary<string, CraftMateriaRow> table, int id) =>
        table.TryGetValue(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var row) ? row : null;
}
