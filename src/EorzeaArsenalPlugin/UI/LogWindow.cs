using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;
using EorzeaArsenal.Model;
using EorzeaArsenal.Plugin.Services;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// A diagnostics window listing the recent plugin messages (status codes, request ids, errors —
/// never secrets, R22) with copy-to-clipboard and clear actions, so the user can share them for
/// support. Reads from the shared <see cref="LogBuffer"/>.
/// </summary>
public sealed class LogWindow : Window
{
    private static readonly Vector4 Red = new(0.92f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 Yellow = new(0.95f, 0.82f, 0.35f, 1f);
    private static readonly Vector4 Muted = new(0.80f, 0.82f, 0.86f, 1f);

    private static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);

    private readonly LogBuffer _buffer;
    private readonly Localizer _localizer;
    private readonly GearsetMappingService _mapping;
    private readonly GearsetDebugView _gearsets;
    private readonly Action _sampleGearsets;
    private readonly Func<string?> _currentCharacter;

    /// <summary>Creates the log window.</summary>
    /// <param name="buffer">The shared log buffer.</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="mapping">The gearset identity cache, for its live state.</param>
    /// <param name="gearsets">The last sampled view of the mapping.</param>
    /// <param name="sampleGearsets">Takes a fresh sample (off the framework thread).</param>
    /// <param name="currentCharacter">The <c>cid_hash</c> of the character on screen, if any.</param>
    public LogWindow(
        LogBuffer buffer,
        Localizer localizer,
        GearsetMappingService mapping,
        GearsetDebugView gearsets,
        Action sampleGearsets,
        Func<string?> currentCharacter)
        : base("Eorzea Arsenal — Log###EorzeaArsenalLog")
    {
        _buffer = buffer;
        _localizer = localizer;
        _mapping = mapping;
        _gearsets = gearsets;
        _sampleGearsets = sampleGearsets;
        _currentCharacter = currentCharacter;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(460, 280),
            MaximumSize = new Vector2(1200, 1400),
        };
    }

    private string T(string key) => _localizer.Get(key);

    /// <inheritdoc />
    public override void Draw()
    {
        if (ImGui.Button(T(LocKeys.LogCopy)))
        {
            ImGui.SetClipboardText(_buffer.ToText());
        }

        ImGui.SameLine();
        if (ImGui.Button(T(LocKeys.LogClear)))
        {
            _buffer.Clear();
        }

        ImGui.Separator();
        DrawGearsetIdentity();

        var entries = _buffer.Snapshot();
        if (ImGui.BeginChild("##logList", new Vector2(0, 0), true))
        {
            if (entries.Count == 0)
            {
                ImGui.TextDisabled(T(LocKeys.LogEmpty));
            }
            else
            {
                foreach (var entry in entries)
                {
                    var color = entry.Level switch
                    {
                        LogLevel.Error => Red,
                        LogLevel.Warning => Yellow,
                        _ => Muted,
                    };

                    ImGui.PushStyleColor(ImGuiCol.Text, color);
                    ImGui.TextWrapped($"[{entry.Time:HH:mm:ss}] {entry.Message}");
                    ImGui.PopStyleColor();
                }

                // Keep pinned to the newest entry while the user is at the bottom.
                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
                {
                    ImGui.SetScrollHereY(1f);
                }
            }
        }

        ImGui.EndChild();
    }

    /// <summary>
    /// The gearset identity state, collapsed by default. Local only — this is a diagnostics view, and
    /// nothing in it is ever sent anywhere. It answers the questions that "the comparison looks wrong"
    /// cannot: does this server mint identities at all, was the mapping readable, and which uid does
    /// each gearset currently resolve to.
    /// </summary>
    private void DrawGearsetIdentity()
    {
        if (!ImGui.CollapsingHeader(T(LocKeys.LogGearsetIdentity)))
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

        using (ImRaii.Disabled(_gearsets.IsSampling))
        {
            if (ImGui.Button(T(LocKeys.LogGearsetSample)))
            {
                _sampleGearsets();
            }
        }

        if (_gearsets.SampledUtc is { } taken)
        {
            ImGui.SameLine();
            ImGui.TextColored(Muted, $"{taken.ToLocalTime():HH:mm:ss}");
        }

        if (_gearsets.Note is { Length: > 0 } note)
        {
            ImGui.TextColored(Yellow, note);
        }

        var rows = _gearsets.Rows;
        if (rows.Count == 0)
        {
            ImGui.TextDisabled(T(LocKeys.LogGearsetNone));
            ImGui.Separator();
            return;
        }

        ImGui.TextColored(Muted, $"{_gearsets.Resolved}/{rows.Count} identified, {_gearsets.Ambiguous} ambiguous");

        if (ImGui.BeginTable("##gearsetIdentity", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
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

                // Green where the identity is established, yellow where the server guessed or this side
                // declined. The colour is the whole point: a wall of green means a reorder was survived.
                var uncertainRow = !row.IsResolved || MatchedBy.IsUncertain(row.MatchedBy);
                ImGui.TextColored(uncertainRow ? Yellow : Green, row.ShortUid);
                ImGui.TableNextColumn();
                ImGui.TextColored(uncertainRow ? Yellow : Muted, row.Rung);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Name ?? string.Empty);
            }

            ImGui.EndTable();
        }

        if (ImGui.Button(T(LocKeys.LogGearsetCopy)))
        {
            var text = string.Join(
                Environment.NewLine,
                rows.Select(r => $"#{r.GearIndex}\t{r.Job}\t{r.SetUid ?? "-"}\t{r.Rung}\t{r.Name}"));
            ImGui.SetClipboardText(text);
        }

        ImGui.Separator();
    }
}
