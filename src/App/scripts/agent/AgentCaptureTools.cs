using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.App;

sealed class AgentCaptureTools
{
    readonly AgentTableTools _tools;
    readonly AgentEndpoint _a;

    public AgentCaptureTools(AgentTableTools tools, AgentEndpoint a)
    {
        _tools = tools;
        _a = a;
    }

    const int ShownPixels = 620, MostTraced = 60;
    const double LeastPart = 0.01;
    const string Above = "2d", Eye = "first_person";
    const string Busy = "A GIF is being recorded: wait for it to end.", Headless = "headless";
    static readonly string[] Formats = Enum.GetValues<SightingFormat>().Select(SightingDraft.NameOf).ToArray();

    static string GifWords => $"record a GIF this many seconds long instead of a still picture, up to {GifRecording.MostSeconds:0}";

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("capture",
            "Takes the Capture tool's picture of the person's view, panels left out, all or a part, a still or a GIF, or the map from straight above (map_box or profile). " +
            "It goes to the clipboard and capture folder as the Capture settings say; show_view aims the view.",
            Args.Schema(new Arg("region", Args.Numbers, "[left, top, right, bottom], fractions 0–1 of the view (all if left out)"), new Arg("gif_seconds", "number", GifWords),
                new Arg("map_box", Args.Numbers, "[x, z, width, depth], map units, x, z its middle"),
                new Arg("pixels", "number", $"map_box's longer side, {MapBox.LeastPixels}–{MapBox.MostPixels} ({MapBox.UsualPixels} if left out)"),
                new Arg("profile", "string", "a map box profile's name")),
            Capture);
        yield return new AgentTool("pov_capture",
            "Takes the picture a piece would send home, as the POV capture tool does (umpires): from above out to its reach, round, square or cut to an outline you draw, any of them as a viewshed that leaves out the ground its eye cannot see; " +
            "or first person, out of the piece's own eyes. Several pieces at once give one picture from above of all they see, each looking from where it stands. Traced lines block the view behind them. It works on the person's screen as the tool does: the piece is selected and the view goes to it. " +
            "The picture goes to the clipboard and the capture folder as the Capture settings say, and comes back to you to look at.",
            Args.Schema(new Arg("piece", "string", "the piece whose point of view it is"), new Arg("pieces", "array", "instead of piece: the pieces whose sight joins into one picture, from above"), new Arg("view", "string", $"{Above} from above or {Eye} ({Above} if left out)"),
                new Arg("format", "string", $"from above: {string.Join(", ", Formats)} (round if left out)"),
                new Arg("viewshed", "boolean", "from above: leave out the ground the eye cannot see"),
                new Arg("radius", "number", "from above: how far the piece sees, in map units (the reach the scenario last used if left out)"),
                new Arg("sharp", "number", "from above: 0–1, where the picture starts to blur toward its edge; 1 keeps it sharp everywhere (as the scenario last used if left out)"),
                new Arg("blur", "number", $"from above: {SightingDraft.LeastBlur}–{SightingDraft.MostBlur}, how far things smudge at the very edge (as the scenario last used if left out)"),
                new Arg("eye_height", "number", $"viewshed: metres the eye stands over the ground, {SightingDraft.LowestWatch}–{SightingDraft.HighestWatch}; first person: map units over the ground (as the tool last had it if left out)"),
                new Arg("heading", "number", "first person: compass degrees it looks toward, 0 north, 90 east (the piece's front if left out)"),
                new Arg("lens", "number", $"first person: degrees the picture takes in across, {SightingDraft.NarrowestLens}–{SightingDraft.WidestLens} ({SightingDraft.NormalLens} if left out)"),
                new Arg("hide_text", "boolean", "no names, counters, notes or lettering in the picture"),
                new Arg("traced", Args.Objects, "from above: [{\"points\": [[x, z], …]}, …] lines that block the view, or with drawn the outline the picture is cut to"),
                new Arg("gif_seconds", "number", GifWords),
                new Arg("deliver", "boolean", "false only shows you the picture and hands nothing over (true if left out)")),
            PovCapture);
    }

    static bool Rendering => DisplayServer.GetName() != Headless;

    static string? Seconds(JsonElement a, out double? seconds)
    {
        seconds = a.Num("gif_seconds");
        return seconds is <= 0 or > GifRecording.MostSeconds ? $"gif_seconds is above 0 and at most {GifRecording.MostSeconds:0}." : null;
    }

    async Task<ToolResult> Capture(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (_, hud)) return why!;
        if (Seconds(a, out var seconds) is { } refused) return ToolResult.Fail(refused);
        var captures = hud.Captures;
        if (captures.RegionRecording != null || captures.Recording != null) return ToolResult.Fail(Busy);
        if (a.Str("profile") is { } named)
        {
            var scenario = hud.S.View.ScenarioId ?? "";
            if (KTable.Client.Prefs.CaptureProfiles.Named(hud.T.Settings, scenario, named, boxes: true)?.Box is not [var x, var z, var wide, var deep, var pixels])
                return ToolResult.Fail($"No map box profile is called {named}: " + string.Join(", ", KTable.Client.Prefs.CaptureProfiles.Of(hud.T.Settings, scenario, boxes: true).Select(p => p.Name)));
            return await MapCapture(captures, a, [x, z, wide, deep], pixels, seconds != null);
        }
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("map_box", out var box))
        {
            if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4 || box.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                return ToolResult.Fail("map_box is [x, z, width, depth] in map units.");
            if (a.Num("pixels") is { } asked && asked != MapBox.FitPixels(asked)) return ToolResult.Fail($"pixels is a whole number, {MapBox.LeastPixels}–{MapBox.MostPixels}.");
            return await MapCapture(captures, a, box.EnumerateArray().Select(v => v.GetDouble()).ToArray(), a.Num("pixels") ?? MapBox.UsualPixels, seconds != null);
        }
        var view = hud.T.GetViewport().GetVisibleRect().Size;
        var part = new Rect2(Vector2.Zero, view);
        if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("region", out var region))
        {
            if (region.ValueKind != JsonValueKind.Array || region.GetArrayLength() != 4 || region.EnumerateArray().Any(v => v.ValueKind != JsonValueKind.Number))
                return ToolResult.Fail("region is [left, top, right, bottom], fractions 0–1 of the view.");
            var f = region.EnumerateArray().Select(v => Math.Clamp(v.GetDouble(), 0, 1)).ToArray();
            if (f[2] - f[0] < LeastPart || f[3] - f[1] < LeastPart) return ToolResult.Fail("region's right is past its left and its bottom below its top.");
            part = new Rect2((float)(f[0] * view.X), (float)(f[1] * view.Y), (float)((f[2] - f[0]) * view.X), (float)((f[3] - f[1]) * view.Y));
        }
        if (!Rendering) return ToolResult.Fail("This app has no screen to take a picture of.");
        await GroundTiles.WhenSettled(_a);
        captures.Handed = null;
        var run = captures.Capture(part, seconds != null);
        if (seconds is { } wait)
        {
            await _a.Delay(wait);
            captures.StopRegion();
        }
        await run;
        _a.Log(seconds != null ? "recorded a GIF of the view" : "captured the view");
        return captures.Handed is { } handed ? new ToolResult(handed) : ToolResult.Fail("Nothing was captured: the part asked for is too small.");
    }

    async Task<ToolResult> MapCapture(Captures captures, JsonElement a, double[] n, double pixels, bool gif)
    {
        if (gif || a.TryGetProperty("region", out _)) return ToolResult.Fail("map_box takes a still of the map: leave out region and gif_seconds.");
        var area = new MapArea(new Vec2(n[0], n[1]), n[2], n[3]);
        if (!area.Sound || area != area.Fitted())
            return ToolResult.Fail(FormattableString.Invariant($"map_box's width and depth are {MapBox.LeastSide} to {MapBox.MostSide} map units, its middle within {MapBox.Farthest} of the map's."));
        if (!Rendering) return ToolResult.Fail("This app has no screen to take a picture of.");
        captures.Handed = null;
        await captures.Boxed(area, MapBox.FitPixels(pixels));
        _a.Log("captured a box of the map");
        return captures.Handed is { } handed ? new ToolResult(handed, !handed.StartsWith(Captures.PictureWord, StringComparison.Ordinal)) : ToolResult.Fail("Nothing was captured: another picture is being taken.");
    }

    static string? Traced(JsonElement a, out List<List<Vec2>> lines)
    {
        lines = new();
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("traced", out var traced)) return null;
        const string shape = "traced is [{\"points\": [[x, z], …]}, …], two points or more to a line.";
        if (traced.ValueKind != JsonValueKind.Array || traced.GetArrayLength() > MostTraced) return shape[..^1] + $", {MostTraced} lines at most.";
        foreach (var line in traced.EnumerateArray())
        {
            if (AgentTableTools.Points(line) is not { Count: >= 2 } points) return shape;
            lines.Add(points);
        }
        return null;
    }

    async Task<ToolResult> PovCapture(JsonElement a)
    {
        if (_tools.Table(out var why) is not var (s, hud)) return why!;
        if (!s.IsUmpire) return ToolResult.Fail("POV capture is the umpires'.");
        var ids = Watchers(a);
        if (ids.Count == 0) return ToolResult.Fail("pieces names at least one piece.");
        if (ids.FirstOrDefault(one => s.View.Find(Layers.Umpire, one) == null) is { } missing) return ToolResult.Fail($"No piece {missing} on this table (list_pieces).");
        var piece = s.View.Find(Layers.Umpire, ids[0])!;
        var view = (a.Str("view") ?? Above).Trim().ToLowerInvariant();
        if (view is not (Above or Eye)) return ToolResult.Fail($"view is {Above} or {Eye}.");
        var format = (a.Str("format") ?? Formats[0]).Trim().ToLowerInvariant();
        if (Array.IndexOf(Formats, format) < 0) return ToolResult.Fail("format is " + string.Join(", ", Formats) + ".");
        if (a.Num("radius") is <= 0) return ToolResult.Fail("radius is above 0, in map units.");
        if (Seconds(a, out var seconds) is { } refused) return ToolResult.Fail(refused);
        if (Traced(a, out var lines) is { } badLines) return ToolResult.Fail(badLines);
        bool eye = view == Eye;
        if (eye && ids.Count > 1) return ToolResult.Fail("first person looks out of one piece.");
        if (lines.Count > 0 && eye) return ToolResult.Fail("traced lines go with the pictures from above.");
        if (eye && a.Flag("viewshed") == true) return ToolResult.Fail("a viewshed goes with the pictures from above.");
        var captures = hud.Captures;
        if (captures.Recording != null || captures.RegionRecording != null) return ToolResult.Fail(Busy);
        var t = hud.T;
        if (t.Draft != null) t.CancelSighting();
        t.Selection.Set(ids);
        t.SetTool(Tool.Sighting);
        if (t.Draft is not { } d || !d.Watchers.ToHashSet().SetEquals(ids)) return ToolResult.Fail("POV capture did not begin from those pieces.");
        Set(t.Sighting, d, a, eye, SightingDraft.FormatNamed(format));
        foreach (var line in lines)
            if (!t.Sighting.Trace(line))
            {
                t.CancelSighting();
                return ToolResult.Fail("A traced line lies outside the picture, or the outline is too small to cut to.");
            }
        d.Gif = seconds != null;
        t.RefreshSightingPanel();
        await _a.Drawn(AgentEndpoint.SettleFrames);
        if (Rendering) await GroundTiles.WhenSettled(_a);
        string name = ids.Count > 1 ? $"{ids.Count} pieces" : piece.Label ?? s.Def(piece.Def)?.Name ?? piece.Def;
        string taken = $"POV capture from {name}: {(d.Included.Count == 1 ? "1 piece" : d.Included.Count + " pieces")} in the picture.";
        var shown = seconds == null && Rendering && await t.RenderSighting(d, ShownPixels) is { } picture ? new[] { picture.SavePngToBuffer() } : null;
        if (a.Flag("deliver") == false || !Rendering)
        {
            t.CancelSighting();
            return new ToolResult(taken + " Nothing was handed over.", false, shown);
        }
        captures.Handed = null;
        var run = captures.Sighting();
        if (seconds is { } wait)
        {
            await _a.Delay(wait);
            await captures.Sighting();
        }
        await run;
        if (t.Draft != null) t.CancelSighting();
        _a.Log("took a POV capture from " + name);
        return captures.Handed is { } handed ? new ToolResult(taken + " " + handed, false, shown) : ToolResult.Fail(taken + " No picture came of it.");
    }

    static List<string> Watchers(JsonElement a) =>
        a.ValueKind == JsonValueKind.Object && a.TryGetProperty("pieces", out var many) && many.ValueKind == JsonValueKind.Array
            ? many.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Distinct().ToList()
            : new List<string> { a.Str("piece") ?? "" };

    static void Set(Sighting sight, SightingDraft d, JsonElement a, bool eye, SightingFormat format)
    {
        sight.SetFirstPerson(eye);
        sight.SetHideText(a.Flag("hide_text") == true);
        if (eye)
        {
            sight.SetLens(a.Num("lens") ?? SightingDraft.NormalLens);
            if (a.Num("eye_height") is { } height) sight.SetEyeHeight(height);
            if (a.Num("heading") is { } heading) sight.Look(heading);
            else sight.LookAhead();
            return;
        }
        sight.SetFormat(format);
        sight.SetSurveyed(a.Flag("viewshed") == true);
        if (a.Num("radius") is { } radius) sight.SetRadius(radius);
        if (a.Num("sharp") is { } sharp) sight.SetSharp(sharp);
        if (a.Num("blur") is { } blur) sight.SetBlur(blur);
        if (d.Surveyed && a.Num("eye_height") is { } watch) sight.SetWatch(watch);
    }
}
