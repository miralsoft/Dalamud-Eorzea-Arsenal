using EorzeaArsenal.Model;

namespace EorzeaArsenal.Gear;

/// <summary>
/// Which job codes may leave this machine, and the <c>scope</c> that push therefore declares.
/// </summary>
/// <remarks>
/// <para>
/// Naming a job (<see cref="JobMap"/>) and being allowed to send it are two different questions, and
/// this type answers the second. What may go out is decided by the table the <b>server at that address</b>
/// published, never by the copy shipped with the plugin: assuming what a server accepts is exactly what
/// "on any disagreement the server wins" forbids, and an unaccepted code fails the whole push with a 422.
/// </para>
/// <para>
/// The scope follows from <i>which table governed</i>, not from what happened to be dropped. A push that
/// ran under a real table declares <see cref="JobScope.All"/> even if it reported nothing for half of it:
/// a job the server does not have in its table is a job it has no rows for, so there is nothing there it
/// could wrongly park. Downgrading the label because a set was skipped would make an honest push look
/// like a narrowed one, and a narrowed one is what stops rows from being parked.
/// </para>
/// </remarks>
public sealed class JobPolicy
{
    private JobPolicy(IReadOnlySet<string> allowed, string scope)
    {
        AllowedCodes = allowed;
        Scope = scope;
    }

    /// <summary>The codes that may be sent.</summary>
    public IReadOnlySet<string> AllowedCodes { get; }

    /// <summary>What a push under this policy declares it covered.</summary>
    public string Scope { get; }

    /// <summary>
    /// The conservative policy: the frozen compatibility floor, declared as
    /// <see cref="JobScope.Combat"/>. This is what applies when the server's table is unknown — a 404
    /// from an older server, or no table ever seen from this address.
    /// </summary>
    /// <remarks>
    /// It damages nothing and syncs most of it, where a full push against a server that rejects a code
    /// would be a 422 and therefore sync nothing. It heals on the first successful fetch.
    /// </remarks>
    public static JobPolicy Floor { get; } = new(JobScope.CombatFloor, JobScope.Combat);

    /// <summary>
    /// The policy a real table implies: exactly the codes it lists, declared as
    /// <see cref="JobScope.All"/>.
    /// </summary>
    /// <param name="table">The table this server published.</param>
    /// <returns>
    /// A policy over the table's codes, or <see cref="Floor"/> if the table names none — an empty table
    /// is not a licence to send nothing under a full label, it is an answer that cannot be trusted.
    /// </returns>
    public static JobPolicy FromTable(JobTableResponse table)
    {
        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var job in table.Jobs)
        {
            if (!string.IsNullOrWhiteSpace(job.Code))
            {
                codes.Add(job.Code);
            }
        }

        return codes.Count == 0 ? Floor : new JobPolicy(codes, JobScope.All);
    }

    /// <summary>Whether a gearset's job may be sent under this policy.</summary>
    /// <param name="code">The gearset's job code.</param>
    /// <returns><see langword="true"/> if it may go out.</returns>
    public bool Allows(string? code) => code is not null && AllowedCodes.Contains(code);

    /// <summary>
    /// Drops the gearsets this policy does not allow, keeping the player's order among those that stay.
    /// </summary>
    /// <param name="data">The snapshot read from the game.</param>
    /// <returns>The same snapshot where everything is allowed, otherwise a filtered copy.</returns>
    public GearData Apply(GearData data)
    {
        var kept = new List<GearsetDto>(data.Gearsets.Count);
        foreach (var set in data.Gearsets)
        {
            if (Allows(set.Job))
            {
                kept.Add(set);
            }
        }

        return kept.Count == data.Gearsets.Count
            ? data
            : new GearData { Character = data.Character, Gearsets = kept };
    }
}
