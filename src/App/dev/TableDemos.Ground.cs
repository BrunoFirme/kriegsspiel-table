using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

sealed partial class TableDemos
{
    const double GroundWidth = 120, GroundDepth = 80;
    static readonly string[] SwatchKinds = ["grass", "field", "wood", "marsh", "water", "road", "earth", "rock", "sand", "snow"];

    Lighting GroundLook(string how)
    {
        var parts = how.Split(',');
        var look = Json.Clone(Session.View.Lighting);
        look.MapStyle = parts[0] == "survey" ? MapStyles.Survey : parts[0];
        look.Contours = parts.Contains("contours");
        look.SnowLine = parts.Contains("snow") ? DemoSnowLineMetres : 0;
        look.Streams = parts.Contains("streams");
        Session.Do(new SetLighting { Lighting = look });
        return look;
    }

    const double DemoMetresPerUnit = 50, DemoSnowLineMetres = 45;

    void DemoGround(Hud hud, string how)
    {
        var look = GroundLook(how);
        Session.Do(new SetCalibration { UnitsPerMeter = 1 / DemoMetresPerUnit });
        hud.Map.Reliefs.AddBlank(GroundWidth, GroundDepth, why =>
        {
            if (why != null) { GD.Print("[ground] blank failed: " + why); return; }
            Tree.CreateTimer(ReliefSettleSeconds).Timeout += () =>
            {
                var sheet = Session.View.Sheets.Last();
                var grid = Session.Relief.Grid(sheet)?.Copy();
                if (grid == null) { GD.Print("[ground] no relief"); return; }
                PaintGround(grid);
                ReliefTool.Commit(Session, Json.Clone(sheet), grid, hud.Map.Reliefs.Share, m => GD.Print("[ground] " + m));
                GD.Print($"[ground] painted {look.MapStyle} on {sheet.Size.W}x{sheet.Size.H} at {sheet.Pos.X},{sheet.Pos.Z}, grid {grid.Cols}x{grid.Rows}");
                if (_args.TryGetValue("ground-shots", out var dir)) Callable.From(() => { _ = GroundTour(hud, dir); }).CallDeferred();
            };
        });
    }

    static readonly (string Name, double X, double Z, float Size, float PitchDeg, float YawDeg)[] GroundViews =
    [
        ("table", 0, 0, 82, 90, 0),
        ("tilt", 0, -12, 62, 38, 0),
        ("swatches", 0, 22, 40, 90, 0),
        ("near-a", -40, 22, 15, 90, 0),
        ("near-b", -6, 22, 15, 90, 0),
        ("near-c", 28, 22, 15, 90, 0),
        ("near-d", 44, 22, 15, 90, 0),
        ("lake", 22, -12, 20, 34, 0),
        ("hill", -30, -16, 22, 30, 20),
        ("hillnear", -30, -16, 9, 35, 20),
        ("route", -8, -6, 46, 90, 0),
        ("flow", 36, -4, 14, 90, 0),
        ("flow3d", 36, -4, 12, 40, 20),
        ("culvert", 37.8, -9.6, 6, 90, 0),
        ("lanebridge", 37.8, -5.5, 5, 90, 0),
        ("roadgrass", 37.8, -14, 5, 90, 0),
        ("names", 8, -22, 50, 90, 0),
        ("names3d", 8, -22, 40, 40, 10),
        ("hilltop", -32, -14, 9, 90, 0),
        ("cliff", -50, -19, 10, 90, 0),
        ("cliff3d", -50, -19, 10, 35, 10),
        ("cliffwood", -39, -19, 10, 90, 0),
        ("cliffwood3d", -39, -19, 10, 35, 10),
        ("terrace", 4, -5, 14, 90, 0),
        ("terrace3d", 4, -5, 14, 35, 0),
        ("farm", -39, 24, 9, 90, 0),
        ("stands", -28, 24, 9, 90, 0),
        ("plots", -40, -36, 26, 90, 0),
        ("trees", 6, -36, 10, 90, 0),
        ("village", 2, -22, 16, 90, 0),
        ("town", 18, -28, 16, 90, 0),
        ("town3d", 18, -28, 16, 40, 15),
        ("citywall", -13, -29, 18, 90, 0),
        ("citynear", -19.5, -29, 6, 90, 0),
        ("macro", -40, 26, 5, 90, 0),
        ("swatch3d", -6, 16, 26, 32, 0),
        ("bridge", 51, -31.5, 8, 90, 0),
        ("road-a", 8, -24, 5, 90, 0),
        ("road-b", -2, -21.5, 5, 90, 0),
        ("meadow", 20, -14, 6, 90, 0),
        ("bridge3d", 51, -31.5, 9, 40, 20),
    ];

    const double TourSettleSeconds = 6, ViewSettleSeconds = 1.2;
    const int TimedFrames = 30;

    static readonly string[] ReseedViews = ["swatches", "near-a", "near-b", "village"];
    const byte ReseedSalts = 100;

    async System.Threading.Tasks.Task GroundTour(Hud hud, string dir)
    {
        await _main.ToSignal(Tree.CreateTimer(TourSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        NameGround();
        await _main.ToSignal(Tree.CreateTimer(ViewSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        hud.Visible = false;
        RenderingServer.ViewportSetMeasureRenderTime(_main.GetViewport().GetViewportRid(), true);
        var views = _args.TryGetValue("ground-views", out var only) ? GroundViews.Where(g => only.Split(',').Contains(g.Name)).ToList() : GroundViews.ToList();
        foreach (var v in views) await Shoot(dir, v);
        if (_args["demo-ground"].Split(',').Contains("reseed"))
        {
            var sheet = Session.View.Sheets.Last();
            var grid = Session.Relief.Grid(sheet)!.Copy();
            PaintGround(grid, ReseedSalts);
            ReliefTool.Commit(Session, Json.Clone(sheet), grid, hud.Map.Reliefs.Share, m => GD.Print("[ground] " + m));
            await _main.ToSignal(Tree.CreateTimer(TourSettleSeconds), SceneTreeTimer.SignalName.Timeout);
            var again = System.IO.Path.Combine(dir, "reseed");
            System.IO.Directory.CreateDirectory(again);
            foreach (var v in GroundViews.Where(g => ReseedViews.Contains(g.Name))) await Shoot(again, v);
        }
        _main.QuitApp();
    }

    static readonly (string Text, double U, double V)[] DemoNames =
    [
        ("Pogue", 0.515, 0.77), ("Boggs Mill", 0.66, 0.815), ("Carrick", 0.39, 0.86),
        ("Black Wood", 0.1, 0.53), ("Long Marsh", 0.62, 0.62), ("The Knoll", 0.24, 0.6), ("Far Field", 0.53, 0.56),
        ("Mill Water", 0.9, 0.86), ("Carrick Burn", 0.55, 0.3),
    ];

    void NameGround()
    {
        var sheet = Session.View.Sheets.Last();
        foreach (var (text, u, v) in DemoNames)
        {
            var at = Ground.FromUv(sheet, u, v);
            Session.Do(new Draw { Layer = Layers.Umpire, Stroke = LetterTool.Named(Session.Ground.NameAt(at, text).Baseline, text, "") });
        }
    }

    async System.Threading.Tasks.Task Shoot(string dir, (string Name, double X, double Z, float Size, float PitchDeg, float YawDeg) v)
    {
        var rig = _table.Rig;
        bool flat = v.PitchDeg >= 89;
        if (flat != rig.TopDown) rig.ToggleTopDown();
        rig.Pitch = -Mathf.DegToRad(System.Math.Min(v.PitchDeg, 85));
        rig.Yaw = Mathf.Pi - Mathf.DegToRad(v.YawDeg);
        rig.Frame(Coords.ToWorld(new Vec2(v.X, v.Z)), v.Size);
        await _main.ToSignal(Tree.CreateTimer(ViewSettleSeconds), SceneTreeTimer.SignalName.Timeout);
        var path = System.IO.Path.Combine(dir, v.Name + ".png");
        var viewport = _main.GetViewport();
        viewport.GetTexture().GetImage().SavePng(path);
        double gpuMs = 0;
        for (int i = 0; i < TimedFrames; i++)
        {
            await _main.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
            gpuMs += RenderingServer.ViewportGetMeasuredRenderTimeGpu(viewport.GetViewportRid());
        }
        GD.Print($"[ground] shot {path}: gpu {gpuMs / TimedFrames:0.00} ms a frame");
    }

    static void PaintGround(ReliefGrid g, byte saltFrom = 0)
    {
        var gp = new GroundPainter(g, GroundWidth / System.Math.Max(1, g.Cols - 1) * DemoMetresPerUnit, DemoMetresPerUnit, saltFrom);

        gp.Raise(14, 1.6, (0.04, 0.33), (0.96, 0.33));
        for (int i = 0; i < 3; i++) gp.Raise(30, 1, (0.86, 0.72));
        for (int i = 0; i < 3; i++) gp.Raise(22, 1, (0.9, 0.5));
        gp.Raise(90, 0.4, (0.9, 0.6));
        gp.Hollow(26, (0.69, 0.6), (0.77, 0.63));
        foreach (var (r, depth) in new[] { (48, 0.012), (36, 0.015), (24, 0.02), (16, 0.03), (10, 0.05), (6, 0.08), (3, 0.15), (1, 0.3) }) gp.Lower(r, depth, (0.885, 0.6), (0.8, 0.63));
        gp.Lower(10, 0.6, (0.74, 0.8), (0.8, 0.62), (0.86, 0.45));
        for (int i = 0; i < SwatchKinds.Length; i++)
        {
            double u0 = 0.035 + i * 0.094, u1 = u0 + 0.078;
            for (double v = 0.07; v <= 0.32; v += 0.025) gp.Paint(SwatchKinds[i], 7, 1, (u0 + 0.012, v), (u1 - 0.012, v + 0.004));
            gp.Paint(SwatchKinds[i], 6, 0.35, (u0, 0.37), (u1, 0.38));
        }

        for (int i = 0; i < 4; i++) gp.Raise(40, 1, (0.24, 0.68));
        gp.Raise(26, 0.5, (0.13, 0.62), (0.3, 0.7));
        for (double v = 0.47; v <= 0.97; v += 0.03) gp.Paint("grass", 9, 1, (0.02, v), (0.98, v));
        for (double v = 0.47; v <= 0.6; v += 0.025) gp.Paint("wood", 9, 1, (0.03, v), (0.17 + 0.03 * System.Math.Sin(v * 40), v));
        gp.Paint("wood", 7, 0.3, (0.18, 0.5), (0.24, 0.55));
        gp.Paint("wood", 6, 1, (0.29, 0.6), (0.33, 0.72), (0.31, 0.8));
        gp.Paint("rock", 8, 1, (0.22, 0.66), (0.26, 0.69));
        gp.Paint("snow", 5, 1, (0.24, 0.68));
        gp.Paint("water", 20, 1, (0.7, 0.6), (0.76, 0.63));
        gp.Paint("water", 3, 1, (0.76, 0.66), (0.82, 0.78), (0.9, 0.86), (0.98, 0.97));
        gp.Paint("marsh", 10, 1, (0.62, 0.55), (0.62, 0.68), (0.66, 0.73));
        gp.Paint("sand", 6, 1, (0.68, 0.7), (0.74, 0.71));
        gp.PaintRoad(0, 2, (0.5, 0.78), (0.45, 0.62), (0.4, 0.48));
        gp.PaintRoad(50, 3, (0.62, 0.84), (0.66, 0.7), (0.7, 0.6));
        gp.PaintRoad(100, 4, (0.02, 0.88), (0.2, 0.86), (0.4, 0.8), (0.55, 0.78), (0.62, 0.84), (0.8, 0.88), (0.98, 0.9));
        gp.Paint("earth", 4, 1, (0.47, 0.76), (0.53, 0.76));
        gp.Paint("earth", 3, 1, (0.5, 0.82), (0.56, 0.83));
        for (int i = 0; i < 4; i++) gp.Paint("field", 6, 1, (0.36 + i * 0.03, 0.88), (0.37 + i * 0.03, 0.96));
        for (int i = 0; i < 3; i++) gp.Paint("field", 6, 1, (0.6 + i * 0.035, 0.92), (0.63 + i * 0.035, 0.95));
        gp.Paint("field", 7, 1, (0.4, 0.66), (0.47, 0.7));
        gp.Paint("wood", 1.5, 1, (0.35, 0.86), (0.48, 0.86));
        for (int i = 0; i < 10; i++) gp.Raise(4, 1, (0.05, 0.745), (0.21, 0.745));
        for (int i = 0; i < 4; i++) gp.Raise(9, 1, (0.05, 0.725), (0.21, 0.725));
        gp.Paint("wood", 5, 1, (0.135, 0.73), (0.21, 0.73), (0.21, 0.76), (0.135, 0.76));
        for (int i = 0; i < 3; i++) gp.Raise(26, 1, (0.53, 0.57));
        gp.Paint("field", 8, 1, (0.5, 0.54), (0.56, 0.56), (0.5, 0.59));
        (int plots, int buildings)[] strips = [(50, 0), (50, 15), (50, 100), (0, 25), (80, 25)];
        for (int i = 0; i < strips.Length; i++) gp.PaintAt("field", strips[i].plots, strips[i].buildings, 0.02 + i * 0.062, 0.92, 0.078 + i * 0.062, 0.985);
        gp.PaintAt("wood", 20, 0, 0.49, 0.92, 0.545, 0.985);
        gp.PaintAt("wood", 80, 0, 0.555, 0.92, 0.61, 0.985);
        gp.PaintShape("town", 20, 0, (0.49, 0.745), (0.515, 0.735), (0.54, 0.75), (0.545, 0.775), (0.53, 0.795), (0.5, 0.8), (0.485, 0.78));
        gp.PaintShape("town", 65, 30, (0.61, 0.8), (0.655, 0.79), (0.695, 0.81), (0.705, 0.85), (0.69, 0.9), (0.65, 0.915), (0.61, 0.9), (0.595, 0.85));
        gp.PaintShape("town", 100, 100, (0.34, 0.815), (0.39, 0.8), (0.44, 0.81), (0.455, 0.85), (0.445, 0.9), (0.4, 0.925), (0.345, 0.91), (0.325, 0.86));
        gp.Route(50, 3, (0.515, 0.755), (0.53, 0.5), (0.3, 0.45));
        gp.River(1.5, 0.885, 0.485);
        gp.PaintRoad(50, 3, (0.815, 0.44), (0.815, 0.76));
    }

    const double SceneSettleSeconds = 14;

    async System.Threading.Tasks.Task ScenarioTour(Hud hud, string dir, string views)
    {
        double settle = _args.TryGetValue("tour-after", out var after) ? double.Parse(after, System.Globalization.CultureInfo.InvariantCulture) : SceneSettleSeconds;
        await _main.ToSignal(Tree.CreateTimer(settle), SceneTreeTimer.SignalName.Timeout);
        hud.Visible = false;
        System.IO.Directory.CreateDirectory(dir);
        RenderingServer.ViewportSetMeasureRenderTime(_main.GetViewport().GetViewportRid(), true);
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var view in views.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var halves = view.Split('=');
            var f = halves[^1].Split(',').Select(x => float.Parse(x, inv)).ToArray();
            if (halves[0].StartsWith(ZoomFilm.Prefix, System.StringComparison.Ordinal)) await ZoomFilm.Shot(_main, _table.Rig, dir, halves[0], f);
            else await Shoot(dir, (halves[0], f[0], f[1], f[2], f.Length > 3 ? f[3] : 90, f.Length > 4 ? f[4] : 0));
        }
        _main.QuitApp();
    }
}
