using Dalamud.Game;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Gear;
using LuminaClassJob = Lumina.Excel.Sheets.ClassJob;

namespace EorzeaArsenal.Plugin.Gear;

/// <summary>One row of the game's <c>ClassJob</c> sheet, in the two languages this plugin speaks.</summary>
/// <param name="Id">The sheet row id, which is what a gearset carries.</param>
/// <param name="Code">The English abbreviation, which is what the API speaks.</param>
/// <param name="LocalCode">The same abbreviation in German, which is what the player sees in game.</param>
/// <param name="English">The job's English name, for a bug report that names the job rather than a number.</param>
/// <param name="German">The same name in German, for the same reason.</param>
public sealed record GameJob(uint Id, string Code, string LocalCode, string English, string German);

/// <summary>
/// Reads the game's own list of jobs, so a job the plugin was never told about can still be named.
/// </summary>
/// <remarks>
/// <para>
/// The mapping in <see cref="JobMap"/> was transcribed from this very sheet. Transcribing it again every
/// time the game adds a job means a set silently missing from the list until somebody notices and ships a
/// release, and the player sees no reason for it: <see cref="GameGearSource"/> skips a gearset whose job
/// it cannot name, leaving a gap in the numbering and nothing else.
/// </para>
/// <para>
/// Reading the sheet does not widen what may be sent. That stays with the table the server published, so
/// a job learned here is named, shown and filtered out of every push until the server lists it too.
/// </para>
/// </remarks>
public static class GameJobSheet
{
    /// <summary>Reads every job row the game has.</summary>
    /// <param name="data">The game data manager.</param>
    /// <param name="log">Diagnostics sink.</param>
    /// <returns>The rows, ascending by id; empty when the sheet cannot be read.</returns>
    /// <remarks>
    /// Two sheets, because the abbreviation is localised as well and the API speaks the English one. The
    /// German name is carried alongside for the report and for nothing else: the server's job table has no
    /// name field, so nothing here is ever sent.
    /// </remarks>
    public static IReadOnlyList<GameJob> Read(IDataManager data, ILog log)
    {
        try
        {
            var english = data.GetExcelSheet<LuminaClassJob>(ClientLanguage.English);
            if (english is null)
            {
                return [];
            }

            var german = data.GetExcelSheet<LuminaClassJob>(ClientLanguage.German);
            var jobs = new List<GameJob>(english.Count);

            foreach (var row in english)
            {
                if (row.RowId == 0)
                {
                    continue;
                }

                var code = row.Abbreviation.ExtractText();
                if (string.IsNullOrWhiteSpace(code))
                {
                    continue;
                }

                var name = row.Name.ExtractText();

                // The German row twice over: the abbreviation because it is what the player reads on the
                // gearset in game and is usually a different word (PLD is PAL there), and the name so a
                // report can say which job it means rather than only which row.
                var localCode = code;
                var localName = name;
                if (german is not null && german.TryGetRow(row.RowId, out var other))
                {
                    localCode = other.Abbreviation.ExtractText();
                    localName = other.Name.ExtractText();
                }

                jobs.Add(new GameJob(row.RowId, code, localCode, name, localName));
            }

            return jobs;
        }
        catch (Exception ex)
        {
            // Never fatal. Without the sheet the plugin knows exactly what it knew before: the floor.
            log.Warning($"Reading the job sheet failed ({ex.GetType().Name}); keeping the compiled job map.");
            return [];
        }
    }
}
