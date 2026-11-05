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
        Session.Do(new SetLighting { Lighting = look });
        return look;
    }

    const double DemoMetresPerUnit = 50;

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
        ("bridge3d", 51, -31.5, 9, 40, 20),
    ];

    const double TourSettleSeconds = 6, ViewSettleSeconds = 1.2;
    const int TimedFrames = 30;

    static readonly string[] ReseedViews = ["swatches", "near-a", "near-b", "village"];
    const byte ReseedSalts = 100;

    async System.Threading.Tasks.Task GroundTour(Hud hud, string dir)
    {
        await _main.ToSignal(Tree.CreateTimer(TourSettleSeconds), SceneTreeTimer.SignalName.Timeout);
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
        static byte K(string name) => GroundKinds.Of(name)!.Value;
        byte salt = saltFrom;
        void Paint(string kind, double r, double density, params (double, double)[] pts) =>
            ReliefBrush.Stroke(g, pts, r, new Brush(BrushMode.Paint, 0, 1, K(kind), Density: density, Salt: ++salt, Detail: GroundKinds.Levels(50, 25)));
        void PaintAt(string kind, int first, int second, double u0, double v0, double u1, double v1) =>
            ReliefBrush.Shape(g, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)], new Brush(BrushMode.Paint, 0, 1, K(kind), Salt: ++salt, Detail: GroundKinds.Levels(first, second)));
        void Raise(double r, double strength, params (double, double)[] pts) => ReliefBrush.Stroke(g, pts, r, new Brush(BrushMode.Raise, 0, strength));
        void Lower(double r, double strength, params (double, double)[] pts) => ReliefBrush.Stroke(g, pts, r, new Brush(BrushMode.Lower, 0, strength));

        Raise(14, 1.6, (0.04, 0.33), (0.96, 0.33));
        for (int i = 0; i < 3; i++) Raise(30, 1, (0.86, 0.72));
        for (int i = 0; i < 3; i++) Raise(22, 1, (0.9, 0.5));
        Raise(12, 0.8, (0.86, 0.72), (0.9, 0.5));
        Lower(10, 0.6, (0.74, 0.8), (0.8, 0.62), (0.86, 0.45));
        for (int i = 0; i < SwatchKinds.Length; i++)
        {
            double u0 = 0.035 + i * 0.094, u1 = u0 + 0.078;
            for (double v = 0.07; v <= 0.32; v += 0.025) Paint(SwatchKinds[i], 7, 1, (u0 + 0.012, v), (u1 - 0.012, v + 0.004));
            Paint(SwatchKinds[i], 6, 0.35, (u0, 0.37), (u1, 0.38));
        }

        for (int i = 0; i < 4; i++) Raise(40, 1, (0.24, 0.68));
        Raise(26, 0.5, (0.13, 0.62), (0.3, 0.7));
        for (double v = 0.47; v <= 0.97; v += 0.03) Paint("grass", 9, 1, (0.02, v), (0.98, v));
        for (double v = 0.47; v <= 0.6; v += 0.025) Paint("wood", 9, 1, (0.03, v), (0.17 + 0.03 * System.Math.Sin(v * 40), v));
        Paint("wood", 7, 0.3, (0.18, 0.5), (0.24, 0.55));
        Paint("wood", 6, 1, (0.29, 0.6), (0.33, 0.72), (0.31, 0.8));
        Paint("rock", 8, 1, (0.22, 0.66), (0.26, 0.69));
        Paint("snow", 5, 1, (0.24, 0.68));
        Paint("water", 20, 1, (0.7, 0.6), (0.76, 0.63));
        Paint("water", 3, 1, (0.76, 0.66), (0.82, 0.78), (0.9, 0.86), (0.98, 0.97));
        Paint("marsh", 10, 1, (0.62, 0.55), (0.62, 0.68), (0.66, 0.73));
        Paint("sand", 6, 1, (0.68, 0.7), (0.74, 0.71));
        Paint("road", 2.5, 1, (0.02, 0.88), (0.2, 0.86), (0.4, 0.8), (0.55, 0.78), (0.62, 0.84), (0.8, 0.88), (0.98, 0.9));
        Paint("road", 1.8, 1, (0.5, 0.78), (0.45, 0.62), (0.4, 0.48));
        Paint("earth", 4, 1, (0.47, 0.76), (0.53, 0.76));
        Paint("earth", 3, 1, (0.5, 0.82), (0.56, 0.83));
        for (int i = 0; i < 4; i++) Paint("field", 6, 1, (0.36 + i * 0.03, 0.88), (0.37 + i * 0.03, 0.96));
        for (int i = 0; i < 3; i++) Paint("field", 6, 1, (0.6 + i * 0.035, 0.92), (0.63 + i * 0.035, 0.95));
        Paint("field", 7, 1, (0.4, 0.66), (0.47, 0.7));
        Paint("wood", 1.5, 1, (0.35, 0.86), (0.48, 0.86));
        for (int i = 0; i < 10; i++) Raise(4, 1, (0.05, 0.745), (0.21, 0.745));
        for (int i = 0; i < 4; i++) Raise(9, 1, (0.05, 0.725), (0.21, 0.725));
        Paint("wood", 5, 1, (0.135, 0.73), (0.21, 0.73), (0.21, 0.76), (0.135, 0.76));
        for (int i = 0; i < 3; i++) Raise(26, 1, (0.53, 0.57));
        Paint("field", 8, 1, (0.5, 0.54), (0.56, 0.56), (0.5, 0.59));
        (int plots, int buildings)[] strips = [(50, 0), (50, 15), (50, 100), (0, 25), (80, 25)];
        for (int i = 0; i < strips.Length; i++) PaintAt("field", strips[i].plots, strips[i].buildings, 0.02 + i * 0.062, 0.92, 0.078 + i * 0.062, 0.985);
        PaintAt("wood", 20, 0, 0.49, 0.92, 0.545, 0.985);
        PaintAt("wood", 80, 0, 0.555, 0.92, 0.61, 0.985);
        PaintAt("town", 20, 0, 0.49, 0.735, 0.54, 0.79);
        PaintAt("town", 65, 30, 0.6, 0.79, 0.7, 0.91);
        PaintAt("town", 100, 100, 0.33, 0.8, 0.45, 0.92);
    }
}
