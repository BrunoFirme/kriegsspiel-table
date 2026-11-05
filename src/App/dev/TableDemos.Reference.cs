using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class TableDemos
{
    const int ReferenceColumns = 3, ReferenceRows = 2;
    const string ReferenceName = "Terrain reference";
    static readonly string[] ReferenceSheetNames = ["North-west", "North", "North-east", "South-west", "South", "South-east"];

    static readonly (string Name, double U, double V, float Size, float PitchDeg, float YawDeg)[] ReferenceViews =
    [
        ("nw", 0.5, 0.5, 85, 90, 0), ("n", 1.5, 0.5, 85, 90, 0), ("ne", 2.5, 0.5, 85, 90, 0),
        ("sw", 0.5, 1.5, 85, 90, 0), ("s", 1.5, 1.5, 85, 90, 0), ("se", 2.5, 1.5, 85, 90, 0),
        ("mountain", 0.3, 0.42, 28, 90, 0), ("pass", 0.25, 0.55, 14, 90, 0), ("lake", 0.45, 0.47, 14, 90, 0),
        ("town", 1.1, 0.84, 14, 90, 0), ("city", 1.5, 1.18, 30, 90, 0), ("suburb", 1.63, 1.1, 10, 90, 0),
        ("village", 1.3, 0.58, 12, 90, 0), ("hamlet", 0.8, 1.2, 8, 90, 0), ("farms", 1.9, 0.95, 12, 90, 0),
        ("plots", 2.25, 1.14, 12, 90, 0), ("forest", 1.2, 0.42, 22, 90, 0), ("copse", 1.7, 0.8, 8, 90, 0),
        ("southwood", 0.9, 1.7, 16, 90, 0), ("river", 0.95, 0.94, 14, 90, 0), ("delta", 2.6, 1.4, 16, 90, 0),
        ("coast", 2.4, 1.55, 16, 90, 0), ("port", 2.72, 1.32, 12, 90, 0), ("headland", 2.95, 0.88, 12, 90, 0),
        ("lowlake", 1.3, 1.6, 16, 90, 0), ("terraces", 0.65, 1.3, 14, 90, 0),
        ("valley-3d", 1.1, 0.9, 70, 35, 15), ("coast-3d", 2.6, 1.4, 60, 35, 0), ("mountain-3d", 0.35, 0.5, 50, 35, 20),
    ];

    static Vec2 SheetCentre(int i) => new(-GroundWidth + i % ReferenceColumns * GroundWidth, GroundDepth / 2 - i / ReferenceColumns * GroundDepth);

    static Vec2 ReferenceWorld(double u, double v) => new(-GroundWidth * ReferenceColumns / 2 + u * GroundWidth, GroundDepth * ReferenceRows / 2 - v * GroundDepth);

    string? ReferenceDir()
    {
        var scenarios = System.IO.Path.Combine(Session.Store.Root, "scenarios");
        if (!System.IO.Directory.Exists(scenarios)) return null;
        foreach (var dir in System.IO.Directory.GetDirectories(scenarios))
        {
            var file = System.IO.Path.Combine(dir, "scenario.json");
            if (System.IO.File.Exists(file) && Json.PeekString(file, "name") == ReferenceName) return System.IO.Path.GetFileName(dir);
        }
        return null;
    }

    async System.Threading.Tasks.Task DemoReference(Hud hud, string how)
    {
        if (!_args.ContainsKey("packages")) { GD.Print("[reference] needs --packages at a scratch folder: it writes a scenario"); return; }
        var look = GroundLook(how);
        Session.Do(new SetCalibration { UnitsPerMeter = 1 / DemoMetresPerUnit });
        var only = how.Split(',').FirstOrDefault(part => part.StartsWith("only="))?["only=".Length..].Split('+');
        var ids = new Dictionary<int, string>();
        for (int i = 0; i < ReferenceSheetNames.Length; i++)
        {
            if (only != null && !only.Contains(ReferenceSheetNames[i])) continue;
            var added = new System.Threading.Tasks.TaskCompletionSource<string?>();
            hud.Map.Reliefs.AddBlank(GroundWidth, GroundDepth, why => added.TrySetResult(why));
            if (await added.Task is { } why) { GD.Print("[reference] blank failed: " + why); return; }
            await _main.ToSignal(Tree.CreateTimer(ReliefSettleSeconds), SceneTreeTimer.SignalName.Timeout);
            var sheet = Session.View.Sheets.Last();
            var grid = Session.Relief.Grid(sheet)?.Copy();
            if (grid == null) { GD.Print("[reference] no relief on sheet " + i); return; }
            var painter = new GroundPainter(grid, GroundWidth / System.Math.Max(1, grid.Cols - 1) * DemoMetresPerUnit, DemoMetresPerUnit, 0, i % ReferenceColumns, i / ReferenceColumns);
            ReferenceCountry.Paint(painter);
            var named = Json.Clone(sheet);
            named.Name = ReferenceSheetNames[i];
            ReliefTool.Commit(Session, named, grid, hud.Map.Reliefs.Share, m => GD.Print("[reference] " + m));
            ids[i] = sheet.Id;
            GD.Print($"[reference] painted {ReferenceSheetNames[i]} on {sheet.Id}");
        }
        await _main.ToSignal(Tree.CreateTimer(ReliefSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        foreach (var (i, id) in ids)
        {
            var sheet = Session.View.Sheets.First(s => s.Id == id);
            Session.Do(new MoveSheet { Id = sheet.Id, Pos = SheetCentre(i), RotDeg = sheet.RotDeg, Z = i, Size = sheet.Size });
        }
        await _main.ToSignal(Tree.CreateTimer(ReliefSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        var dir = ScenarioFiles.SaveTable(Session.Store, Session.View, ReferenceName, ReferenceDir());
        GD.Print($"[reference] saved '{ReferenceName}' as {look.MapStyle} in {dir}");
        if (_args.TryGetValue("ground-shots", out var shots)) await ReferenceTour(hud, shots, ids.Keys);
    }

    async System.Threading.Tasks.Task ReferenceTour(Hud hud, string dir, IEnumerable<int> sheets)
    {
        await _main.ToSignal(Tree.CreateTimer(TourSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        hud.Visible = false;
        RenderingServer.ViewportSetMeasureRenderTime(_main.GetViewport().GetViewportRid(), true);
        var only = _args.TryGetValue("ground-views", out var names) ? names.Split(',') : null;
        foreach (var v in ReferenceViews.Where(v => sheets.Contains((int)v.U + (int)v.V * ReferenceColumns) && (only == null || only.Contains(v.Name))))
        {
            var at = ReferenceWorld(v.U, v.V);
            await Shoot(dir, (v.Name, at.X, at.Z, v.Size, v.PitchDeg, v.YawDeg));
        }
        _main.QuitApp();
    }
}
