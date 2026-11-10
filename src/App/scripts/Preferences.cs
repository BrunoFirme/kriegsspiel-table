using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace KTable.App;

enum PrefEffect { Kept, Scale, Strip, TopLine, Sound, Display, Graphics, Lettering, Pointers }

sealed record Pref(string Name, string Page, string Meaning, string Allowed, Func<Settings, JsonNode?> Get, Func<Settings, JsonElement, string?> Set, PrefEffect Effect);

static class Preferences
{
    static readonly string[] EdgeSteps = { "off", "2", "4", "8" };
    static readonly string[] ShadowSteps = { "low", "medium", "high" };

    public static readonly IReadOnlyList<Pref> All = new[]
    {
        Number("interface_size", "Interface", "how big the interface draws, on top of the size fitted to the window and screen", Settings.UiScaleMin, Settings.UiScaleMax,
            c => c.UiScale, (c, v) => c.UiScale = v, PrefEffect.Scale),
        Number("sidebar_buttons", "Interface", "sidebar button size in pixels", Settings.StripSizeMin, Settings.StripSizeMax, c => c.StripSize, (c, v) => c.StripSize = MathF.Round(v), PrefEffect.Strip),
        Flag("top_line", "Interface", "the top bar says what just happened (off: only the log does)", c => c.TopLine, (c, on) => c.TopLine = on, PrefEffect.TopLine),
        Number("top_line_length", "Interface", "share of the top bar that line may take", Settings.TopLineWidthMin, 1, c => c.TopLineWidth, (c, v) => c.TopLineWidth = v, PrefEffect.TopLine),
        Flag("text_on_pieces", "Table", "the counters and text placed on pieces (off: names on hover only)", c => c.FaceText, (c, on) => c.FaceText = on, PrefEffect.Lettering),
        Number("name_size", "Table", "how big names print on pieces", Settings.FaceTextScaleMin, Settings.FaceTextScaleMax, c => c.FaceTextScale, (c, v) => c.FaceTextScale = v, PrefEffect.Lettering),
        Choice("smooth_edges", "Graphics", "multisampling samples on the 3D table", EdgeSteps, c => c.Antialias, (c, i) => c.Antialias = i, PrefEffect.Graphics),
        Flag("smooth_fine_lines", "Graphics", "also smooth cut-out pictures and thin lines (small lettering softens a little)", c => c.SmoothFineEdges, (c, on) => c.SmoothFineEdges = on, PrefEffect.Graphics),
        Choice("far_detail", "Graphics", "how sharp a map's fine lines stay in the distance", new[] { "soft", "crisp", "crisper" }, c => c.FarDetail, (c, i) => c.FarDetail = i, PrefEffect.Graphics),
        Choice("render_scale", "Graphics", "the table is drawn at this many times the screen's pixels and shrunk; costs frame rate in proportion",
            Graphics.RenderScales.Select(n => n.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            c => Math.Max(0, Array.FindIndex(Graphics.RenderScales, n => Mathf.IsEqualApprox(n, c.RenderScale))), (c, i) => c.RenderScale = Graphics.RenderScales[i], PrefEffect.Graphics),
        Choice("shadows", "Graphics", "shadow detail where the scenario's light casts shadows; high also keeps them zoomed far out", ShadowSteps, c => c.ShadowDetail, (c, i) => c.ShadowDetail = i, PrefEffect.Graphics),
        Flag("contact_shading", "Graphics", "soft shade where pieces meet the table", c => c.ContactShade, (c, on) => c.ContactShade = on, PrefEffect.Graphics),
        Choice("window", "Interface", "windowed; borderless fills the screen as a window, quick to switch away from; fullscreen takes the screen over", new[] { "windowed", "borderless", "fullscreen" },
            c => Math.Clamp(c.WindowMode, Settings.Windowed, Settings.Fullscreen), (c, i) => c.WindowMode = i, PrefEffect.Display),
        Flag("vsync", "Graphics", "keep frames in step with the screen", c => c.VSync, (c, on) => c.VSync = on, PrefEffect.Display),
        Choice("frame_limit", "Graphics", "frames a second at most (0: none)", Graphics.FrameLimits.Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            c => Math.Max(0, Array.IndexOf(Graphics.FrameLimits, c.FrameLimit)), (c, i) => c.FrameLimit = Graphics.FrameLimits[i], PrefEffect.Display),
        Number("lettering_reach", "Table", FormattableString.Invariant($"how far pieces keep names, counters and buttons, in their own sizes ({Settings.ReachOff}: no limit)"), Settings.ReachMin, Settings.ReachOff,
            c => c.LetteringReach, (c, v) => c.LetteringReach = v, PrefEffect.Lettering),
        Number("volume", "Sound", "table sound volume", 0, 1, c => c.SoundVolume, (c, v) => c.SoundVolume = v, PrefEffect.Sound),
        Flag("silent", "Sound", "no table sounds", c => c.SoundMuted, (c, on) => c.SoundMuted = on, PrefEffect.Sound),
        Flag("sound_in_background", "Sound", "heard while the window is behind others", c => c.SoundInBackground, (c, on) => c.SoundInBackground = on, PrefEffect.Sound),
        new Pref("new_backdrop", "Table", "the backdrop a new blank scenario starts with; a sky also sets its light", "\"\" for the app's own, a colour as #rrggbb, or one of: " + string.Join(", ", KTable.Client.Backdrops.All.Select(b => b.Id)),
            c => c.NewBackdrop, SetBackdrop, PrefEffect.Kept),
        Number("new_table_surface", "Table", "the table surface a new blank scenario starts with: 0 squared paper, 0.5 grey, 1 black, 1.05 see-through", 0, (float)KTable.Core.Model.Lighting.SeeThrough,
            c => (float)c.NewTableShade, (c, v) => c.NewTableShade = Math.Round(v, 2), PrefEffect.Kept),
        Flag("share_pointer", "Table", "the other umpires see this pointer", c => c.ShareCursor, (c, on) => c.ShareCursor = on, PrefEffect.Pointers),
        Flag("show_pointers", "Table", "show the other umpires' pointers", c => c.ShowCursors, (c, on) => c.ShowCursors = on, PrefEffect.Pointers),
        Flag("capture_to_clipboard", "Captures", "captures are copied to the clipboard", c => c.CaptureToClipboard, (c, on) => c.CaptureToClipboard = on, PrefEffect.Kept),
        Flag("capture_to_folder", "Captures", "captures are saved to the capture folder", c => c.CaptureToFolder, (c, on) => c.CaptureToFolder = on, PrefEffect.Kept),
        new Pref("capture_folder", "Captures", "the folder captures are saved to", "a folder that exists", c => c.CaptureFolder, SetFolder, PrefEffect.Kept),
        Number("capture_resolution", "Captures", "captures render at this many times the screen's resolution", 1, Settings.CaptureScaleMost, c => c.CaptureScale, (c, v) => c.CaptureScale = (int)MathF.Round(v), PrefEffect.Kept),
        Flag("check_updates", "Updates", "look for a new version at start", c => c.CheckUpdates, (c, on) => c.CheckUpdates = on, PrefEffect.Kept),
    };

    public static Pref? Find(string name) => All.FirstOrDefault(p => p.Name == name);

    public static void Apply(IEnumerable<PrefEffect> effects, Settings cfg, Window root, Hud? hud)
    {
        foreach (var e in effects.Distinct())
        {
            switch (e)
            {
                case PrefEffect.Scale:
                    Ui.ApplyScale(root, cfg);
                    break;
                case PrefEffect.Sound:
                    Sfx.Current?.Apply();
                    break;
                case PrefEffect.Display:
                    Graphics.ApplyDisplay(cfg);
                    break;
            }
            if (hud == null || !GodotObject.IsInstanceValid(hud)) continue;
            switch (e)
            {
                case PrefEffect.Scale:
                    Callable.From(((IHudShell)hud).Relayout).CallDeferred();
                    break;
                case PrefEffect.Strip:
                    hud.Strip.Rebuild();
                    Callable.From(((IHudShell)hud).Relayout).CallDeferred();
                    break;
                case PrefEffect.TopLine:
                    hud.Top.SyncTopLine();
                    break;
                case PrefEffect.Graphics:
                    hud.T.ApplyGraphics();
                    break;
                case PrefEffect.Lettering:
                    hud.T.ApplyLettering();
                    break;
                case PrefEffect.Pointers:
                    hud.T.ShowCursors = cfg.ShowCursors;
                    hud.Top.UpdatePointerInfo();
                    break;
            }
        }
    }

    static Pref Flag(string name, string page, string meaning, Func<Settings, bool> get, Action<Settings, bool> set, PrefEffect effect) =>
        new(name, page, meaning, "true or false", c => get(c),
            (c, v) => v.ValueKind is JsonValueKind.True or JsonValueKind.False ? Done(() => set(c, v.GetBoolean())) : $"{name} takes true or false", effect);

    static Pref Number(string name, string page, string meaning, float min, float max, Func<Settings, float> get, Action<Settings, float> set, PrefEffect effect)
    {
        var range = FormattableString.Invariant($"{min:0.##} to {max:0.##}");
        return new(name, page, meaning, range, c => Math.Round(get(c), 3),
            (c, v) => v.ValueKind == JsonValueKind.Number && v.GetDouble() is var d && double.IsFinite(d) && d >= min && d <= max
                ? Done(() => set(c, (float)d)) : $"{name} takes a number from {range}", effect);
    }

    static Pref Choice(string name, string page, string meaning, string[] steps, Func<Settings, int> get, Action<Settings, int> set, PrefEffect effect) =>
        new(name, page, meaning, string.Join(", ", steps), c => steps[Math.Clamp(get(c), 0, steps.Length - 1)],
            (c, v) => Array.IndexOf(steps, StepOf(v)) is var i and >= 0 ? Done(() => set(c, i)) : $"{name} takes one of: {string.Join(", ", steps)}", effect);

    static string? StepOf(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString()?.Trim().ToLowerInvariant(),
        JsonValueKind.Number => v.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => null,
    };

    static string? SetBackdrop(Settings c, JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String || v.GetString() is not { } b) return "new_backdrop takes a backdrop's id, a colour as #rrggbb, or \"\"";
        if (b.Length > 0 && !KTable.Client.Backdrops.IsColour(b) && KTable.Client.Backdrops.Find(b) == null) return $"no backdrop {b}: read_settings lists them";
        c.NewBackdrop = b;
        return null;
    }

    static string? SetFolder(Settings c, JsonElement v)
    {
        if (v.ValueKind != JsonValueKind.String || v.GetString() is not { Length: > 0 } path) return "capture_folder takes a folder's full path";
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return $"no folder {path} on this PC";
        c.CaptureFolder = path;
        return null;
    }

    static string? Done(Action set) { set(); return null; }
}
