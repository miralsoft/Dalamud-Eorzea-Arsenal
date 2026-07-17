using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using EorzeaArsenal.Abstractions;
using EorzeaArsenal.Core;
using EorzeaArsenal.Localization;

namespace EorzeaArsenal.Plugin.UI;

/// <summary>
/// Shows one content-hub image on demand, scaled to the (resizable) window — content-hub images can be
/// large, so they are opened here on click rather than rendered inline. Downloads the bytes via
/// <see cref="TeamsService"/> (bearer) and builds an ImGui texture; the image fits the window width and
/// the window scrolls if it is taller. One image at a time.
/// </summary>
public sealed class ImageWindow : Window, IDisposable
{
    private readonly TeamsService _teams;
    private readonly ITextureProvider _textures;
    private readonly Localizer _localizer;
    private readonly ILog _log;

    private IDalamudTextureWrap? _wrap;
    private volatile bool _loading;
    private volatile bool _failed;
    private long _teamId;
    private long _resourceId;
    private string _title = string.Empty;

    /// <summary>Creates the image window.</summary>
    /// <param name="teams">Teams service (streams the file bytes).</param>
    /// <param name="textures">Texture provider (builds the image texture).</param>
    /// <param name="localizer">UI string resolver.</param>
    /// <param name="log">Diagnostics sink.</param>
    public ImageWindow(TeamsService teams, ITextureProvider textures, Localizer localizer, ILog log)
        : base("Eorzea Arsenal — Image###EorzeaArsenalImage")
    {
        _teams = teams;
        _textures = textures;
        _localizer = localizer;
        _log = log;

        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(320, 240),
            MaximumSize = new Vector2(2400, 2000),
        };
    }

    /// <summary>Opens the window showing the given resource image (re-downloads if it changed).</summary>
    /// <param name="teamId">The team id.</param>
    /// <param name="resourceId">The resource id.</param>
    /// <param name="title">A title to show above the image.</param>
    public void Open(long teamId, long resourceId, string? title)
    {
        _title = title ?? string.Empty;
        if (_teamId != teamId || _resourceId != resourceId || (_wrap is null && !_loading))
        {
            _teamId = teamId;
            _resourceId = resourceId;
            _wrap?.Dispose();
            _wrap = null;
            _failed = false;
            Load();
        }

        IsOpen = true;
    }

    /// <inheritdoc />
    public override void Draw()
    {
        if (!string.IsNullOrEmpty(_title))
        {
            ImGui.TextUnformatted(_title);
            ImGui.Separator();
        }

        if (_loading)
        {
            ImGui.TextDisabled(_localizer.Get(LocKeys.TeamsLoading));
            return;
        }

        if (_failed)
        {
            ImGui.TextDisabled(_localizer.Get(LocKeys.TeamsErrorGeneric));
            return;
        }

        if (_wrap is not { Width: > 0 } wrap)
        {
            return;
        }

        // Fit the image to the current window width (scales up/down as the user resizes); the window
        // scrolls vertically if the scaled image is taller than the viewport.
        var width = Math.Max(32f, ImGui.GetContentRegionAvail().X);
        var scale = width / wrap.Width;
        ImGui.Image(wrap.Handle, new Vector2(width, wrap.Height * scale));
    }

    private void Load()
    {
        _loading = true;
        var teamId = _teamId;
        var resourceId = _resourceId;
        _ = Task.Run(async () =>
        {
            IDalamudTextureWrap? result = null;
            try
            {
                var res = await _teams.GetResourceFileAsync(teamId, resourceId, CancellationToken.None).ConfigureAwait(false);
                if (res.IsSuccess && res.Value is { Bytes.Length: > 0 } file)
                {
                    result = await _textures.CreateFromImageAsync(file.Bytes).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _log.Error($"Image window load failed: {ex.GetType().Name}.");
            }
            finally
            {
                _wrap = result;
                _failed = result is null;
                _loading = false;
            }
        });
    }

    /// <summary>Disposes the current texture on unload (P3).</summary>
    public void Dispose()
    {
        _wrap?.Dispose();
        _wrap = null;
    }
}
