using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class AgentTableTools
{
    const int MostPoints = 2000;
    const double ReliefWaitSeconds = 600, ShapeWaitSeconds = 20;

    static string KindNames => string.Join(", ", GroundKinds.All.Select(k => k.Name));

    IEnumerable<AgentTool> ReliefTools()
    {
        yield return new AgentTool("shape_ground",
            "Shapes the relief of the map along a stroke, as the Relief tool does: raise, lower, smooth or flatten the ground, or paint a ground type on it (" + KindNames + "; none rubs paint out). " +
            "The stroke goes to the top sheet with a relief under its first point, or to the top sheet there, which then gets a flat relief. One call is one step that undo takes back. Pieces standing on the ground follow it.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] in map units: one point is a single dab, more draw a stroke through them", true),
                new Arg("mode", "string", "raise, lower, smooth, flatten or paint", true), new Arg("radius", "number", "brush radius, map units", true),
                new Arg("strength", "number", "0–1 (0.35 if left out); for raise and lower, how much one pass adds"), new Arg("kind", "string", "ground type for paint"),
                new Arg("passes", "number", "go over the stroke this many times (1–20, 1 if left out)"), new Arg("sheet", "string", "a sheet's id to shape instead (list_map_sheets)")),
            ShapeGround);
        yield return new AgentTool("ground_at",
            "The ground at points: its height above the table in map units, the ground type painted there, and the sheet whose relief it is.",
            Args.Schema(new Arg("points", "array", "[[x, z], …] in map units", true)), x => Task.FromResult(GroundAt(x)));
    }

    static List<Vec2>? Points(JsonElement a)
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
        var rows = new JsonArray(pts.Select(p => (JsonNode?)new JsonObject
        {
            ["x"] = p.X, ["z"] = p.Z, ["height"] = Math.Round(g.At(p), 3), ["ground"] = GroundKinds.Name(g.KindAt(p)), ["sheet"] = g.ReliefAt(p)?.Id,
        }).ToArray());
        return new ToolResult(rows.ToJsonString(Opts));
    }

    async Task<ToolResult> ShapeGround(JsonElement a)
    {
        if (Table(out var why) is not var (s, hud)) return why!;
        if (!CanChange(s, out why)) return why!;
        if (Points(a) is not { Count: > 0 } pts) return ToolResult.Fail("points is needed: [[x, z], …], at most " + MostPoints + ".");
        var mode = (a.Str("mode") ?? "").Trim().ToLowerInvariant();
        if (mode is not ("raise" or "lower" or "smooth" or "flatten" or "paint")) return ToolResult.Fail("mode is raise, lower, smooth, flatten or paint.");
        if (mode == "paint")
        {
            var kind = (a.Str("kind") ?? "").Trim().ToLowerInvariant();
            if (GroundKinds.Of(kind) == null) return ToolResult.Fail("kind is one of " + KindNames + ".");
            mode = "paint:" + kind;
        }
        if (a.Num("radius") is not { } radius || radius <= 0) return ToolResult.Fail("radius is needed, in map units.");
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
        if (uv.Count == 0) return ToolResult.Fail($"No point lies on {target.Name}.");
        double cell = Ground.FullWidth(target) / Math.Max(1, grid.Cols - 1);
        var brush = ReliefTool.BrushOf(mode, strength);
        if (brush.Mode == BrushMode.Flatten) brush = brush with { Level = grid.HeightAt(uv[0].u, uv[0].v) };
        for (int i = 0; i < passes; i++) ReliefBrush.Stroke(grid, uv, radius / cell, brush);
        var before = target.Relief;
        ReliefTool.Commit(s, Json.Clone(target), grid, hud.Map.Reliefs.Share, m => hud.Log(m));
        for (int i = 0; i < ShapeWaitSeconds * 10 && s.View.Sheets.FirstOrDefault(x => x.Id == target.Id)?.Relief == before; i++) await _a.Delay(0.1);
        _a.Log($"shaped the ground of {target.Name}");
        return new ToolResult($"Shaped {target.Name} ({mode.Replace("paint:", "painted ")}, {uv.Count} point{(uv.Count == 1 ? "" : "s")}). ground_at reads the heights back.");
    }

    async Task<ToolResult> AddReliefTo(Session s, Hud hud, SheetDef sh, string path)
    {
        if (!File.Exists(path)) return ToolResult.Fail("No such file: " + path);
        var before = sh.Relief;
        string? problem = null; bool done = false;
        hud.Map.Reliefs.ReliefOnto(path, sh.Id, why => { problem = why; done = true; });
        for (int i = 0; i < ReliefWaitSeconds * 10 && !done; i++) await _a.Delay(0.1);
        if (problem != null) return ToolResult.Fail("The relief was not added: " + problem);
        for (int i = 0; i < ShapeWaitSeconds * 10 && s.View.Sheets.FirstOrDefault(x => x.Id == sh.Id)?.Relief == before; i++) await _a.Delay(0.1);
        return new ToolResult("");
    }

    async Task<ToolResult> SetPictureOf(Hud hud, SheetDef sh, string path)
    {
        if (!File.Exists(path)) return ToolResult.Fail("No such file: " + path);
        string? problem = null; bool done = false;
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
