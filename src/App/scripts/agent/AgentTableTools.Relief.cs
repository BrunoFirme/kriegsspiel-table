using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class AgentTableTools
{
    internal const int MostPoints = 2000;
    const double ReliefWaitSeconds = 600, ShapeWaitSeconds = 20;

    static string KindNames => string.Join(", ", GroundKinds.Brushes.Select(GroundKinds.Name));
    static readonly string[] ParameterNames = ["plots", "buildings", "trees", "houses", "walls", "road"];
    internal const string NeedsScaleWords = "Set the map's scale first (set_map_scale).";

    IEnumerable<AgentTool> ReliefTools()
    {
        yield return new AgentTool("shape_ground",
            "Shapes the relief of the map along a stroke, as the Terrain tool does: raise, lower, smooth or flatten the ground, or paint a ground type on it (" + KindNames + "; none rubs paint out). " +
            "The stroke goes to the top sheet with a relief under its first point, or to the top sheet there, which then gets a flat relief. Paint with density below 1 lays the type in clumps (sparse wood, scattered rock; with none it thins paint), fill paints the whole patch of like ground around the first point, or a bare sheet whole, shape paints a rectangle, ellipse or polygon whole (a square field, a round pond), and route lays the points as waypoints: the ground finds its way between them over the relief, round rock and marsh, and water runs down from the first point as a river, widening, ending in water or a pond. " +
            "Each stroke of paint draws its own natural signs, afresh every time it is painted; wood takes trees (open wood, or closed forest). Roads, settlements, forts, buildings, plot boundaries and farms are not painted: " +
            "build_road, build_place and build_details lay them. The map needs a scale (set_map_scale) before its ground is shaped. One call is one step that undo takes back. Pieces standing on the ground follow it.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] in map units: one point is a single dab, more draw a stroke through them", true),
                new Arg("mode", "string", "raise, lower, smooth, flatten or paint", true), new Arg("radius", "number", "brush radius, map units", true),
                new Arg("strength", "number", "0–1 (0.35 if left out); for raise and lower, how much one pass adds"), new Arg("kind", "string", "ground type for paint"),
                new Arg("density", "number", "for paint, 0–1 of the ground that takes it (1 if left out)"),
                new Arg("trees", "number", "wood: 0–1, open wood to closed forest (0.75 if left out)"),
                new Arg("fill", "boolean", "for paint: fill the patch around the first point instead of brushing, radius then unused"),
                new Arg("shape", "string", "for paint, instead of brushing (radius then unused): rect or ellipse between two opposite corners, lined up with the map; polygon through its corners, three or more"),
                new Arg("route", "boolean", "for paint: the points are waypoints and the ground finds its way between them over the relief at the radius; water runs down from the first point as a river"),
                new Arg("passes", "number", "go over the stroke this many times (1–20, 1 if left out)"), new Arg("sheet", "string", "a sheet's id to shape instead (list_map_sheets)")),
            ShapeGround);
        yield return new AgentTool("ground_at",
            "The ground at points: its height above the table in map units, the ground type painted there with its trees where it is wood, whether the slope there draws as rock, and the sheet whose relief it is.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] in map units", true)), x => Task.FromResult(GroundAt(x)));
    }

    static ToolResult? Levels(JsonElement a, byte kind, out byte detail)
    {
        detail = 0;
        var own = GroundKinds.Parameters(kind);
        int[] percent = own.Select(p => p.Default).ToArray();
        foreach (var name in ParameterNames)
        {
            if (a.Num(name) is not { } value) continue;
            int at = own.ToList().FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (at < 0) return ToolResult.Fail(own.Count == 0 ? $"{GroundKinds.Name(kind)} takes no {name}." : $"{GroundKinds.Name(kind)} takes {string.Join(" and ", own.Select(p => p.Name.ToLowerInvariant()))}, not {name}.");
            percent[at] = (int)Math.Round(Math.Clamp(value, 0, 1) * 100);
        }
        detail = GroundKinds.Levels(percent.Length > 0 ? percent[0] : 0, percent.Length > 1 ? percent[1] : 0);
        return null;
    }

    internal static List<Vec2>? Points(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("points", out var p) || p.ValueKind != JsonValueKind.Array) return null;
        var o = new List<Vec2>();
        foreach (var e in p.EnumerateArray())
        {
            if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 2) return null;
            var xs = e.EnumerateArray().ToList();
            if (xs[0].ValueKind != JsonValueKind.Number || xs[1].ValueKind != JsonValueKind.Number) return null;
            o.Add(new Vec2(xs[0].GetDouble(), xs[1].GetDouble()));
            if (o.Count > MostPoints) return null;
        }
        return o;
    }

    ToolResult GroundAt(JsonElement a)
    {
        if (Table(out var why) is not var (s, _)) return why!;
        if (Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …].");
        var g = s.Ground;
        var rows = new JsonArray(pts.Select(p => (JsonNode?)GroundRow(g, p)).ToArray());
        return new ToolResult(rows.ToJsonString(Opts));
    }

    const double RockSlope = 0.577;

    static JsonObject GroundRow(Ground g, Vec2 p)
    {
        byte kind = g.KindAt(p);
        var row = new JsonObject { ["x"] = p.X, ["z"] = p.Z, ["height"] = Math.Round(g.At(p), 3), ["ground"] = GroundKinds.Name(kind), ["sheet"] = g.ReliefAt(p)?.Id };
        var levels = GroundKinds.LevelsOf(g.DetailAt(p));
        var parameters = GroundKinds.Parameters(kind);
        if (parameters.Count > 0) row[parameters[0].Name.ToLowerInvariant()] = Math.Round(levels.First / 100.0, 2);
        if (parameters.Count > 1) row[parameters[1].Name.ToLowerInvariant()] = Math.Round(levels.Second / 100.0, 2);
        if (g.ReliefAt(p) != null) row["rock"] = kind == GroundKinds.Of("rock") || g.SlopeAt(p) >= RockSlope;
        return row;
    }

    async Task<ToolResult> ShapeGround(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        if (!ReliefTool.Scaled(s)) return ToolResult.Fail(NeedsScaleWords);
        if (Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + MostPoints + ".");
        var mode = (a.Str("mode") ?? "").Trim().ToLowerInvariant();
        if (mode is not ("raise" or "lower" or "smooth" or "flatten" or "paint")) return ToolResult.Fail("mode is raise, lower, smooth, flatten or paint.");
        byte detail = 0;
        if (mode == "paint")
        {
            var kind = (a.Str("kind") ?? "").Trim().ToLowerInvariant();
            if (GroundKinds.Of(kind) is GroundKinds.Road or GroundKinds.Town) return ToolResult.Fail(AgentBuiltTools.NotPainted);
            if (GroundKinds.Of(kind) is null or GroundKinds.Bridge) return ToolResult.Fail("kind is one of " + KindNames + " or none.");
            if (Levels(a, GroundKinds.Of(kind)!.Value, out detail) is { } refused) return refused;
            mode = "paint:" + kind;
        }
        else if (ParameterNames.Any(n => a.Num(n) != null)) return ToolResult.Fail("plots, buildings, trees, houses, walls and road are for paint.");
        bool fill = mode.StartsWith("paint:", StringComparison.Ordinal) && a.Flag("fill") == true;
        var shape = (a.Str("shape") ?? "").Trim().ToLowerInvariant();
        if (shape.Length > 0 && !mode.StartsWith("paint:", StringComparison.Ordinal)) return ToolResult.Fail("shape is for paint.");
        if (shape.Length > 0 && shape is not ("rect" or "ellipse" or "polygon")) return ToolResult.Fail("shape is rect, ellipse or polygon.");
        if (shape is "rect" or "ellipse" && pts.Count != 2) return ToolResult.Fail($"A {shape} takes two points, opposite corners.");
        if (shape == "polygon" && pts.Count < 3) return ToolResult.Fail("A polygon takes three or more points, its corners.");
        bool route = a.Flag("route") == true;
        if (route && !mode.StartsWith("paint:", StringComparison.Ordinal)) return ToolResult.Fail("route is for paint.");
        if (route && (fill || shape.Length > 0)) return ToolResult.Fail("route goes without fill or shape.");
        double radius = a.Num("radius") ?? 0;
        if (!fill && shape.Length == 0 && radius <= 0) return ToolResult.Fail("radius is needed, in map units.");
        double density = Math.Clamp(a.Num("density") ?? 1, 0, 1);
        double strength = Math.Clamp(a.Num("strength") ?? 0.35, 0, 1);
        int passes = (int)Math.Clamp(a.Num("passes") ?? 1, 1, 20);
        SheetDef? target = a.Str("sheet") is { Length: > 0 } id ? s.View.Sheets.FirstOrDefault(x => x.Id == id) : ReliefTool.Target(s, hud.T.Sheets, pts[0]);
        if (target == null) return ToolResult.Fail(a.Str("sheet") is { Length: > 0 } ? "No such sheet (list_map_sheets)." : "No map sheet under the first point (view_map).");
        ReliefGrid? existing = null;
        if (target.Relief is { Length: > 0 })
            for (int i = 0; i < ShapeWaitSeconds * 10 && (existing = s.Relief.Grid(target)) == null; i++) await _a.Delay(0.1);
        if (target.Relief is { Length: > 0 } && existing == null) return ToolResult.Fail("The sheet's relief could not be read.");
        var c = target.Crop ?? SheetGeometry.Whole;
        var grid = existing?.Copy() ?? ReliefGrid.Flat(Ground.FullWidth(target), target.Size.H / Math.Max(1e-6, c.H));
        var uv = pts.Select(p => Ground.Uv(target, p)).Where(x => x != null).Select(x => x!.Value).ToList();
        if (uv.Count == 0 && shape.Length == 0) return ToolResult.Fail($"No point lies on {target.Name}.");
        double cell = Ground.FullWidth(target) / Math.Max(1, grid.Cols - 1);
        var brush = ReliefTool.BrushOf(mode, strength, density, ReliefTool.RollSalt(), detail);
        if (brush.Mode == BrushMode.Flatten) brush = brush with { Level = grid.HeightAt(uv[0].u, uv[0].v) };
        if (shape.Length > 0)
        {
            var outline = shape == "polygon" ? pts : Polyline.ShapePoints(shape, pts[0], pts[1]);
            if (ReliefBrush.Shape(grid, outline.Select(p => Ground.UvOf(target, p)).ToList(), brush).Empty) return ToolResult.Fail($"The {shape} changed nothing on {target.Name}: it lies off the sheet or is too thin.");
        }
        else if (route)
        {
            double metre = s.View.Calibration.UnitsPerMeter!.Value;
            if (GroundRoute.Lay(grid, brush, uv, radius / cell, cell / metre, ReliefTool.HeightUnit(target) / metre).Empty) return ToolResult.Fail($"The route changed nothing on {target.Name}.");
        }
        else if (fill) ReliefBrush.Fill(grid, uv[0].u, uv[0].v, brush);
        else for (int i = 0; i < passes; i++) ReliefBrush.Stroke(grid, uv, radius / cell, brush);
        var before = target.Relief;
        ReliefTool.Commit(s, Json.Clone(target), grid, hud.Map.Reliefs.Share, m => hud.Log(m));
        for (int i = 0; i < ShapeWaitSeconds * 10 && s.View.Sheets.FirstOrDefault(x => x.Id == target.Id)?.Relief == before; i++) await _a.Delay(0.1);
        _a.Log($"shaped the ground of {target.Name}");
        var did = shape.Length > 0 ? $"a {shape} of {mode[6..]}" : route ? $"a route of {mode[6..]}" : mode.Replace("paint:", fill ? "filled with " : "painted ");
        return new ToolResult($"Shaped {target.Name} ({did}, {pts.Count} point{(pts.Count == 1 ? "" : "s")}). ground_at reads the heights back.");
    }

    async Task<ToolResult> AddReliefTo(Session s, Hud hud, SheetDef sh, string path)
    {
        if (!File.Exists(path)) return ToolResult.Fail("No such file: " + path);
        var before = sh.Relief;
        string? problem = null;
        bool done = false;
        hud.Map.Reliefs.ReliefOnto(path, sh.Id, why => { problem = why; done = true; });
        for (int i = 0; i < ReliefWaitSeconds * 10 && !done; i++) await _a.Delay(0.1);
        if (problem != null) return ToolResult.Fail("The relief was not added: " + problem);
        for (int i = 0; i < ShapeWaitSeconds * 10 && s.View.Sheets.FirstOrDefault(x => x.Id == sh.Id)?.Relief == before; i++) await _a.Delay(0.1);
        return new ToolResult("");
    }

    async Task<ToolResult> SetPictureOf(Hud hud, SheetDef sh, string given)
    {
        var (files, noPicture) = await PictureLinks.Local(new[] { given }, hud.S.Store.Root);
        if (noPicture != null) return ToolResult.Fail(noPicture);
        var path = files[0];
        if (!File.Exists(path)) return ToolResult.Fail("No such file: " + path);
        string? problem = null;
        bool done = false;
        hud.Map.Reliefs.SetPicture(sh.Id, path, why => { problem = why; done = true; });
        for (int i = 0; i < ReliefWaitSeconds * 10 && !done; i++) await _a.Delay(0.1);
        return problem != null ? ToolResult.Fail("The picture was not laid on: " + problem) : new ToolResult("");
    }

    static (double width, double depth)? Blank(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("blank", out var b) || b.ValueKind != JsonValueKind.Array || b.GetArrayLength() < 2) return null;
        var v = b.EnumerateArray().ToList();
        if (v[0].ValueKind != JsonValueKind.Number || v[1].ValueKind != JsonValueKind.Number) return null;
        double w = v[0].GetDouble(), d = v[1].GetDouble();
        return w > 0 && d > 0 ? (w, d) : null;
    }

    static List<ReliefJob>? Pairs(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("pairs", out var p) || p.ValueKind != JsonValueKind.Array) return null;
        var jobs = new List<ReliefJob>();
        foreach (var e in p.EnumerateArray())
        {
            var relief = e.Str("relief");
            if (relief == null) continue;
            var picture = e.Str("picture");
            jobs.Add(new ReliefJob(relief, picture is { Length: > 0 } ? picture : null, picture == null && e.Flag("own_texture") != false && ReliefImport.IsModel(relief)));
        }
        return jobs;
    }
}
