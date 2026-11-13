using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Client.Agent;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

sealed class AgentGroundForms
{
    readonly AgentEndpoint _a;

    public AgentGroundForms(AgentEndpoint a) { _a = a; }

    internal static readonly string[] Modes =
    [
        "raise", "lower", "smooth", "flatten", ReliefTool.LevelMode, "paint", ReliefTool.HillMode, ReliefTool.RidgeMode, ReliefTool.ValleyMode, ReliefTool.SlopeMode, ReliefTool.ContoursMode,
        ReliefTool.RoughenMode, ReliefTool.ErodeMode, ReliefTool.LiftMode,
    ];
    internal static string ModeNames => string.Join(", ", Modes);
    internal const double UsualGrain = 150;
    const int MostContours = 200;

    static double PerMetre(Session s, SheetDef sheet) => (s.View.Calibration.UnitsPerMeter ?? 0) / (Ground.FullWidth(sheet) * Math.Max(ReliefTool.HeightUnit(sheet), 1e-6));

    internal static double Metres(JsonElement a, Session s, SheetDef sheet) => Math.Clamp(a.Num("height") ?? 0, -Settings.HighestReliefLimit, Settings.HighestReliefLimit) * PerMetre(s, sheet);

    async Task<(SheetDef Sheet, ReliefGrid Grid)?> Draft(Session s, SheetDef target)
    {
        ReliefGrid? existing = null;
        if (target.Relief is { Length: > 0 })
            for (int i = 0; i < AgentTableTools.ShapeWaitSeconds * 10 && (existing = s.Relief.Grid(target)) == null; i++) await _a.Delay(0.1);
        if (target.Relief is { Length: > 0 } && existing == null) return null;
        var c = target.Crop ?? SheetGeometry.Whole;
        return (target, existing?.Copy() ?? ReliefGrid.Flat(Ground.FullWidth(target), target.Size.H / Math.Max(1e-6, c.H)));
    }

    async Task<ToolResult> Shaped(Session s, Hud hud, SheetDef target, ReliefGrid grid, CellBox changed, string did)
    {
        if (changed.Empty) return ToolResult.Fail($"The {did} changed nothing on {target.Name}: it lies off the sheet or is too small for its cells.");
        var before = target.Relief;
        ReliefTool.Commit(s, Json.Clone(target), grid, hud.Map.Reliefs.Share, m => hud.Log(m));
        for (int i = 0; i < AgentTableTools.ShapeWaitSeconds * 10 && s.View.Sheets.FirstOrDefault(x => x.Id == target.Id)?.Relief == before; i++) await _a.Delay(0.1);
        _a.Log($"shaped the ground of {target.Name}");
        return new ToolResult($"Shaped {target.Name} ({did}). ground_at reads the heights back.");
    }

    static SheetDef? Sheet(Session s, Hud hud, JsonElement a, Vec2? at) =>
        a.Str("sheet") is { Length: > 0 } id ? s.View.Sheets.FirstOrDefault(x => x.Id == id) : at is { } p ? ReliefTool.Target(s, hud.T.Sheets, p) : null;

    const string NoSheet = "No sheet under that point or of that id (list_map_sheets).";

    internal async Task<ToolResult> ShapeForm(Session s, Hud hud, JsonElement a, string mode, List<Vec2> pts)
    {
        bool spans = mode is ReliefTool.HillMode or ReliefTool.SlopeMode;
        if (pts.Count < 2) return ToolResult.Fail(spans ? $"A {mode} takes two points: its {(mode == ReliefTool.HillMode ? "summit" : "top")}, then its foot." : $"A {mode} takes two or more points, its line.");
        if (mode != ReliefTool.SlopeMode && a.Num("height") is not > 0) return ToolResult.Fail($"A {mode} takes height, in metres above 0.");
        if (mode != ReliefTool.HillMode && a.Num("radius") is not > 0) return ToolResult.Fail($"A {mode} takes radius, half its width in map units.");
        var profile = (a.Str("profile") ?? ReliefForms.Round).Trim().ToLowerInvariant();
        if (!ReliefForms.Profiles.Contains(profile)) return ToolResult.Fail("profile is " + string.Join(", ", ReliefForms.Profiles) + ".");
        if (Sheet(s, hud, a, pts[0]) is not { } target) return ToolResult.Fail(NoSheet);
        if (await Draft(s, target) is not var (sheet, grid)) return ToolResult.Fail("The sheet's relief could not be read.");
        var uv = pts.Select(p => Ground.UvOf(sheet, p)).ToList();
        double across = (a.Num("radius") ?? 0) / (Ground.FullWidth(sheet) / Math.Max(1, grid.Cols - 1)), height = Metres(a, s, sheet);
        var changed = mode switch
        {
            ReliefTool.HillMode => ReliefForms.Hill(grid, uv[0], uv[^1], height, profile),
            ReliefTool.SlopeMode => ReliefForms.Slope(grid, uv[0], uv[^1], across),
            ReliefTool.ValleyMode => ReliefForms.Ridge(grid, uv, across, -height, profile),
            _ => ReliefForms.Ridge(grid, uv, across, height, profile),
        };
        return await Shaped(s, hud, sheet, grid, changed, mode);
    }

    static List<(List<Vec2> Points, double Metres)>? Lines(JsonElement a)
    {
        if (a.ValueKind != JsonValueKind.Object || !a.TryGetProperty("lines", out var given) || given.ValueKind != JsonValueKind.Array) return null;
        var lines = new List<(List<Vec2>, double)>();
        foreach (var line in given.EnumerateArray())
        {
            if (AgentTableTools.Points(line) is not { Count: >= 2 } points || line.Num("height") is not { } metres || lines.Count == MostContours) return null;
            lines.Add((points, Math.Clamp(metres, 0, Settings.HighestReliefLimit)));
        }
        return lines;
    }

    internal async Task<ToolResult> ShapeWhole(Session s, Hud hud, JsonElement a, string mode, Vec2? at = null)
    {
        bool contoured = mode == ReliefTool.ContoursMode;
        var lines = contoured ? Lines(a) : null;
        if (contoured && lines is not { Count: > 0 }) return ToolResult.Fail($"contours takes lines: [{{points: [[x, z], …], height: metres}}, …], at most {MostContours}, each of two or more points.");
        if (!contoured && a.Num("height") is null or 0) return ToolResult.Fail($"{mode} takes height, in metres.");
        if (Sheet(s, hud, a, at ?? lines?[0].Points[0]) is not { } target) return ToolResult.Fail(NoSheet);
        if (await Draft(s, target) is not var (sheet, grid)) return ToolResult.Fail("The sheet's relief could not be read.");
        double metre = s.View.Calibration.UnitsPerMeter!.Value, cell = Ground.FullWidth(sheet) / Math.Max(1, grid.Cols - 1), perMetre = PerMetre(s, sheet);
        double height = a.Num("height") ?? 0, rough = Math.Clamp(Math.Abs(height), Settings.LeastRough, Settings.MostRough);
        var changed = mode switch
        {
            ReliefTool.ContoursMode => ReliefContours.Raise(grid, lines!.Select(l => new ContourLine(l.Points.Select(p => Ground.UvOf(sheet, p)).ToList(), l.Metres * perMetre)).ToList()),
            ReliefTool.LiftMode => ReliefFinish.Lift(grid, Math.Clamp(height, -Settings.MostLift, Settings.MostLift) * perMetre),
            ReliefTool.ErodeMode => ReliefFinish.Erode(grid, rough * perMetre, cell / metre, 1 / perMetre),
            _ => ReliefFinish.Roughen(grid, rough * perMetre, Math.Clamp(a.Num("grain") ?? UsualGrain, Settings.FinestGrain, Settings.CoarsestGrain) * metre / cell, ReliefTool.RollSalt()),
        };
        return await Shaped(s, hud, sheet, grid, changed, contoured ? $"{lines!.Count} contour line{(lines.Count == 1 ? "" : "s")}" : mode);
    }
}
