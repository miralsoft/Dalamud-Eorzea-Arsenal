#if EORZEA_ARSENAL_DEVTOOLS
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using EorzeaArsenal.Core;
using EorzeaArsenal.Gear;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// The developer view of the gearset identity mapping, drawn inside the diagnostics window. Compiled in
/// only when <c>EORZEA_ARSENAL_DEVTOOLS</c> is defined, which comes from a git-ignored local props file —
/// so in a released build this type is not in the assembly at all rather than merely switched off.
/// </summary>
/// <remarks>
/// It answers what "the comparison looks wrong" cannot: does this server mint identities, was the
/// mapping readable, and which uid does each gearset resolve to right now. Sampling is behind a button
/// because resolving hashes every gearset, which has no business running per frame on the framework
/// thread (P1).
/// </remarks>
public sealed class GearsetIdentityPanel
{
    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Muted = new(0.80f, 0.82f, 0.86f, 1f);

    private readonly Localizer _localizer;
    private readonly GearsetMappingService _mapping;
    private readonly GearsetDebugView _view;
    private readonly Action _sample;
    private readonly Func<string?> _currentCharacter;
    private readonly Func<JobPolicy> _policy;
    private readonly Func<ReviewSummary?> _review;

    /// <summary>Creates the panel.</summary>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="mapping">The identity cache, for its live state.</param>
    /// <param name="view">The last sample.</param>
    /// <param name="sample">Takes a fresh sample, off the framework thread.</param>
    /// <param name="currentCharacter">The <c>cid_hash</c> of the character on screen, if any.</param>
    /// <param name="policy">What the next push may send, and the scope it will declare.</param>
    /// <param name="review">What the last push said is waiting, if it said anything.</param>
    public GearsetIdentityPanel(
        Localizer localizer,
        GearsetMappingService mapping,
        GearsetDebugView view,
        Action sample,
        Func<string?> currentCharacter,
        Func<JobPolicy> policy,
        Func<ReviewSummary?> review)
    {
        _localizer = localizer;
        _mapping = mapping;
        _view = view;
        _sample = sample;
        _currentCharacter = currentCharacter;
        _policy = policy;
        _review = review;
    }

    /// <summary>Draws the panel as a collapsed section.</summary>
    public void Draw()
    {
        if (!ImGui.CollapsingHeader(_localizer.Get(LocKeys.LogGearsetIdentity)))
        {
            return;
        }

        // Cheap, per-frame state: no hashing, no requests.
        var mints = _mapping.ServerMintsUids;
        ImGui.TextColored(mints ? Green : Yellow, $"set_uid: {(mints ? "server mints them" : "not seen yet")}");
        ImGui.TextColored(Muted, $"mapping: {_mapping.MappingStatus}");
        ImGui.TextColored(Muted, $"cached rows: {_mapping.CachedCount(_currentCharacter())}");

        var uncertain = _mapping.UncertainMatches.Count;
        if (uncertain > 0)
        {
            ImGui.TextColored(Yellow, $"the server was unsure about {uncertain} set(s)");
        }

        // What the next push may send, and what it will declare it covered. Worth seeing side by side:
        // the whole point of the scope field is that these two can disagree with a version number, and
        // the failure it prevents is invisible from the client otherwise.
        var policy = _policy();
        var full = string.Equals(policy.Scope, JobScope.All, StringComparison.Ordinal);
        ImGui.TextColored(
            full ? Green : Yellow,
            $"job table: {policy.AllowedCodes.Count} code(s), sending scope \"{policy.Scope}\"");

        if (!full)
        {
            ImGui.TextColored(Muted, "  (no table from this address; reporting the frozen combat floor)");
        }

        // The third condition: what the server says is waiting. Counted apart because "2 sets are waiting
        // for your decision" and "10 rows no longer exist in game" are two different sentences.
        if (_review() is { } review)
        {
            var orphans = review.Orphans;
            var line =
                $"review: {review.Held} held, {orphans?.Open ?? 0} open orphan(s), " +
                $"{orphans?.Ignored ?? 0} put aside";

            ImGui.TextColored(review.NeedsAttention ? Yellow : Muted, line);

            if (review.StateToken is { Length: > 0 } token)
            {
                ImGui.TextColored(Muted, $"  token {token[..Math.Min(8, token.Length)]}");
            }
        }
        else
        {
            ImGui.TextColored(Muted, "review: not reported by this server");
        }

        using (ImRaii.Disabled(_view.IsSampling))
        {
            if (ImGui.Button(_localizer.Get(LocKeys.LogGearsetSample)))
            {
                _sample();
            }
        }

        if (_view.SampledUtc is { } taken)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"{taken.ToLocalTime():HH:mm:ss}");
        }

        if (_view.Note is { Length: > 0 } note)
        {
            ImGui.TextColored(Yellow, note);
        }

        var rows = _view.Rows;
        if (rows.Count == 0)
        {
            ImGui.TextDisabled(_localizer.Get(LocKeys.LogGearsetNone));
            ImGui.Separator();
            return;
        }

        ImGui.TextColored(Muted, $"{_view.Resolved}/{rows.Count} identified, {_view.Ambiguous} ambiguous");
        DrawTable(rows);

        if (ImGui.Button(_localizer.Get(LocKeys.LogGearsetCopy)))
        {
            ImGui.SetClipboardText(string.Join(
                Environment.NewLine,
                rows.Select(r => $"#{r.GearIndex}\t{r.Job}\t{r.SetUid ?? "-"}\t{r.Rung}\t{r.Name}")));
        }

        ImGui.Separator();
    }

    private static void DrawTable(IReadOnlyList<GearsetIdentityRow> rows)
    {
        if (!ImGui.BeginTable("##gearsetIdentity", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGui.TableSetupColumn("set_uid", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("matched_by", ImGuiTableColumnFlags.WidthFixed, 120f);
        ImGui.TableSetupColumn("name");
        ImGui.TableHeadersRow();

        foreach (var row in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.GearIndex.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Job);
            ImGui.TableNextColumn();

            // The colour is the message: a column of green means a reorder was survived. Yellow marks
            // where the server guessed or where this side declined to answer.
            var uncertain = !row.IsResolved || MatchedBy.IsUncertain(row.MatchedBy);
            ImGui.TextColored(uncertain ? Yellow : Green, row.ShortUid);
            ImGui.TableNextColumn();
            ImGui.TextColored(uncertain ? Yellow : Muted, row.Rung);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(row.Name ?? string.Empty);
        }

        ImGui.EndTable();
    }
}
#endif
