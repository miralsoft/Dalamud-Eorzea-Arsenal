using EorzeaArsenal.Model;

namespace EorzeaArsenal.Plugin.Services;

/// <summary>
/// The last sampled view of the gearset identity mapping, for the diagnostics window. Local only —
/// nothing here is ever sent anywhere; it exists so the states this feature moves through can be
/// followed while testing instead of inferred from whether the comparison looks right.
/// </summary>
/// <remarks>
/// Sampled on request rather than every frame on purpose. Resolving an identity hashes the whole
/// gearset, so doing it per frame for thirty sets would put real work on the framework thread (P1).
/// The window draws whatever was last sampled and offers a button to sample again.
/// </remarks>
public sealed class GearsetDebugView
{
    private volatile GearsetIdentityRow[] _rows = [];

    /// <summary>The rows from the last sample, oldest position first.</summary>
    public IReadOnlyList<GearsetIdentityRow> Rows => _rows;

    /// <summary>When the sample was taken, or <see langword="null"/> if none has been.</summary>
    public DateTimeOffset? SampledUtc { get; private set; }

    /// <summary>Why there are no rows, when that is the interesting part (not logged in, no key, …).</summary>
    public string? Note { get; private set; }

    /// <summary>Whether a sample is currently being taken.</summary>
    public bool IsSampling { get; set; }

    /// <summary>How many of the sampled gearsets resolved to an identity.</summary>
    public int Resolved => _rows.Count(r => r.IsResolved);

    /// <summary>How many were declined because more than one cached row matched.</summary>
    public int Ambiguous => _rows.Count(r => r.WasAmbiguous);

    /// <summary>Records a completed sample.</summary>
    /// <param name="rows">The rows, or empty.</param>
    /// <param name="takenUtc">When it was taken.</param>
    /// <param name="note">Optional reason there is nothing to show.</param>
    public void Set(IReadOnlyList<GearsetIdentityRow> rows, DateTimeOffset takenUtc, string? note = null)
    {
        _rows = rows.ToArray();
        SampledUtc = takenUtc;
        Note = note;
        IsSampling = false;
    }
}
