using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PovPictureTests
{
    const string U = Layers.Umpire;
    const uint RegionLayers = Table.WorldLayer | Table.TextLayer;
    const int Pixels = 64;

    static IEnumerable<VisualInstance3D> Visuals(Node root) =>
        root.FindChildren("*", nameof(VisualInstance3D), true, false).Append(root).OfType<VisualInstance3D>();

    static uint PictureLayers(SubViewport shot) => ((Camera3D)shot.GetChild(0)).CullMask;

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    [NodeTest]
    static async Task An_umpire_s_working_aids_are_on_no_layer_a_picture_draws(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "" });
            g.Zones.Add(new Zone { Id = "z1", Name = "West", Owner = "Tester", Polygon = { new(-4, -4), new(4, -4), new(4, 4), new(-4, 4) } });
        });
        await r.Frame();
        t.Table.ShowRemoteCursor(7, "Other umpire", new Vec2(2, 2));
        t.Table.SelectSheet("s0");
        var cast = CastPreview.Show(t.Table, new ShowCast { Shape = "box", From = [0, 1, 0], To = [4, 1, 0], Size = [1, 1, 1], Seconds = 5 });
        await r.Frames(2);
        var zones = Visuals(t.Table.GetNode("Zones")).ToList();
        var hands = t.Table.GetChildren().OfType<PointerHand>().SelectMany(Visuals).ToList();
        var outlines = Visuals(t.Table.Sheets.Root).Where(v => v is MeshInstance3D { Mesh: ImmediateMesh }).ToList();
        Check.That(zones.Count > 0 && hands.Count > 0 && outlines.Count > 0, $"a zone, a hand and a sheet outline are on the table: {zones.Count}, {hands.Count}, {outlines.Count}");
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        var shot = await t.Table.Sighting.OpenPicture(t.Table.Draft!, Pixels);
        try
        {
            uint drawn = PictureLayers(shot) | RegionLayers;
            Check.That((drawn & Table.LiveOnlyLayer) == 0, "no picture draws the live-only layer");
            foreach (var (aid, visuals) in new[] { ("a zone", zones), ("another umpire's hand", hands), ("the picked sheet's outline", outlines), ("a script's cast", Visuals(cast).ToList()) })
                Check.That(visuals.All(v => (v.Layers & drawn) == 0), aid + " is on no layer a POV picture or a region capture draws");
        }
        finally { t.Table.Sighting.ClosePicture(shot); }
        t.Table.Sighting.Cancel();
        cast.QueueFree();
    }

    static (uint layers, GeometryInstance3D.ShadowCastingSetting? shadow) Shown(VisualInstance3D v) => (v.Layers, (v as GeometryInstance3D)?.CastShadow);

    static async Task<(OfflineTable t, SightingDraft d, Dictionary<VisualInstance3D, (uint, GeometryInstance3D.ShadowCastingSetting?)> live)> SightingFromP1(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4, arrange: g => g.Layer(U)[3].Pos = new Vec2(60, 0));
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(10), "the tool begins on the picked piece");
        var d = t.Table.Draft!;
        Check.Equal("p1,p2,p3", InSight(d), "in sight when the tool begins");
        return (t, d, t.Table.Pieces.Nodes.SelectMany(Visuals).ToDictionary(v => v, Shown));
    }

    static bool Drawn(OfflineTable t, SubViewport shot, string id) => Visuals(t.Node(id)).Any(v => (v.Layers & PictureLayers(shot)) != 0);

    static void CheckAllBack(OfflineTable t, Dictionary<VisualInstance3D, (uint, GeometryInstance3D.ShadowCastingSetting?)> live)
    {
        Check.That(t.Table.Pieces.Nodes.All(n => !t.Table.Sighting.LeftOut(n) && Visuals(n).All(v => (v.Layers & Table.OutOfPictureLayer) == 0)), "every piece is back in every view when the picture is done");
        var kept = live.Where(x => GodotObject.IsInstanceValid(x.Key) && !x.Key.IsQueuedForDeletion()).ToList();
        Check.That(kept.Count > 0 && kept.All(x => Shown(x.Key) == x.Value), "each piece is drawn on the layers it was on and casts the shadow it cast before the picture");
        t.Table.Sighting.Cancel();
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_pov_picture_holds_the_pieces_in_sight_as_the_table_stands_when_it_is_taken(TestRunner r)
    {
        var (t, d, live) = await SightingFromP1(r);
        t.S.Do(new MovePiece { Layer = U, Id = "p3", Pos = new Vec2(50, 0) });
        t.S.Do(new MovePiece { Layer = U, Id = "p4", Pos = new Vec2(4, 0) });
        var shot = await t.Table.Sighting.OpenPicture(d, Pixels);
        try
        {
            Check.Equal("p1,p2,p4", InSight(d), "the picture takes who is in sight when it is taken, not when the tool began");
            Check.That(Drawn(t, shot, "p4") && !Drawn(t, shot, "p3"), "the piece moved into sight is drawn and the one moved out of it is not");
            t.S.Do(new SetTint { Layer = U, Id = "p3", Value = "#b03a3a" });
            Check.That(!Drawn(t, shot, "p3"), "a piece out of the picture stays out when a change rebuilds it");
        }
        finally { t.Table.Sighting.ClosePicture(shot); }
        CheckAllBack(t, live);
    }

    [ScreenTest]
    static async Task A_pov_picture_kept_open_follows_the_table_frame_by_frame(TestRunner r)
    {
        var (t, d, live) = await SightingFromP1(r);
        var shot = await t.Table.Sighting.OpenPicture(d, Pixels);
        try
        {
            Check.That(Drawn(t, shot, "p2") && Drawn(t, shot, "p3"), "both are in the first frame");
            t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(40, 5) });
            t.S.Do(new SpawnPiece { Layer = U, Def = "x", Id = "p5", Pos = new Vec2(45, 0) });
            t.S.Do(new MovePiece { Layer = U, Id = "p4", Pos = new Vec2(4, 0) });
            await t.Table.Sighting.DrawFrame(shot, d);
            Check.Equal("p1,p3,p4", InSight(d), "the next frame follows the table");
            Check.That(!Drawn(t, shot, "p2") && !Drawn(t, shot, "p5"), "a piece moved out of sight leaves the picture, and one placed out of sight never enters it");
            Check.That(Drawn(t, shot, "p4") && Drawn(t, shot, "p1"), "a piece moved into sight enters it");
        }
        finally { t.Table.Sighting.ClosePicture(shot); }
        CheckAllBack(t, live);
    }

    [NodeTest]
    static async Task The_pov_panel_counts_the_pieces_in_sight_as_they_move(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var d = h.Table.Draft ?? throw new TestFailure("no POV capture began");
        var panel = h.Hud.Pov.Panel;
        string Count() => panel.FindChildren("*", nameof(Label), true, false).OfType<Label>().Single(l => l.Text.EndsWith("in the picture")).Text;
        var slider = panel.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().First();
        Check.Equal("3 pieces in the picture", Count(), "all three are within reach");
        h.S.Do(new MovePiece { Layer = U, Id = "p3", Pos = new Vec2(d.Radius * 3, 0) });
        await r.Frames(2);
        Check.Equal("p1,p2", InSight(d), "the list follows a piece moved out of reach");
        Check.Equal("2 pieces in the picture", Count(), "and the panel says so");
        Check.That(GodotObject.IsInstanceValid(slider) && !slider.IsQueuedForDeletion(), "without rebuilding the sliders under the hand");
        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(d.Radius * 3 + 1, 0) });
        await r.Frames(2);
        Check.Equal("p1,p3", InSight(d), "when the observer moves, its reach moves with it");
        h.Table.CancelSighting();
    }

    static async Task<(HudTests.Rig h, string folder)> ReadyToCapture(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var folder = Path.Combine(Path.GetTempPath(), "ktable-povtake-" + Guid.NewGuid().ToString("N"));
        h.Hud.Cfg.CaptureFolder = folder;
        h.Hud.Cfg.CaptureToFolder = true;
        h.Hud.Cfg.CaptureToClipboard = false;
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        return (h, folder);
    }

    static int Saved(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder).Length : 0;

    [ScreenTest]
    static async Task A_pov_picture_cancelled_while_it_is_being_taken_is_not_handed_over(TestRunner r)
    {
        var (h, folder) = await ReadyToCapture(r);
        try
        {
            var taking = h.Hud.CaptureSighting();
            h.Table.CancelSighting();
            await taking;
            Check.Equal(0, Saved(folder), "cancelled while it was being taken, the picture goes nowhere");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }

    [ScreenTest]
    static async Task A_second_press_while_a_pov_picture_is_being_taken_takes_no_other(TestRunner r)
    {
        var (h, folder) = await ReadyToCapture(r);
        try
        {
            var first = h.Hud.CaptureSighting();
            var second = h.Hud.CaptureSighting();
            Check.That(second.IsCompleted && !first.IsCompleted, "the second press is let go while the first picture is being taken");
            await first;
            Check.That(Saved(folder) == 1 && h.Table.Draft == null, "one picture is handed over and the tool is put down");
        }
        finally { KTable.Core.BestEffort.DeleteFolder(folder); }
    }

    const double Reach = 12, HillAt = 21, FarReach = 30;
    const int HillStrokes = 4, HillCells = 70, EyePixels = 360, Differs = 8, Stray = 20, BlockPixels = 150;

    static async Task<OfflineTable> HillTable(TestRunner r)
    {
        var block = new PieceDef { Id = "block", Name = "Tall block", Size = new Size2(3, 1.5), Thickness = 0.6, Tint = "#b03a3a" };
        var t = OfflineTable.Make(r, pieces: 2, packs: new[] { FirmeEffects.PackId }, prepare: store =>
        {
            StandardPacks.Ensure(store);
            store.RegisterDef(new PackManifest { Id = "blocks", Name = "Blocks" }, block);
        }, arrange: g =>
        {
            g.VisionRadius = Reach;
            g.Lighting = new Lighting { SunEnergy = 0.9, Ambient = 0.4, Shadows = true };
            var board = g.Layer(U);
            board[0].Pos = new Vec2(0, 0);
            board[0].RotDeg = 180;
            board[1].Pos = new Vec2(-3, 6);
            g.Zones.Add(new Zone { Id = "z1", Name = "Near", Owner = "Tester", Polygon = { new(-8, 3), new(8, 3), new(8, 10), new(-8, 10) } });
        });
        var grid = ReliefGrid.Flat(80, 80);
        for (int i = 0; i < HillStrokes; i++) ReliefBrush.Stroke(grid, new[] { (0.1, 0.14), (0.9, 0.14) }, HillCells, new Brush(BrushMode.Raise, 0, 0.6));
        var (sha, _) = ReliefImport.Save(grid, t.S.Store.Root);
        t.S.Relief.Remember(sha, grid);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(80, 80), RotDeg = 180, Relief = sha, Height = 1 } });
        await r.Seconds(1.5);
        Check.That(t.S.Ground.At(new Vec2(0, HillAt)) > 1, "a hill stands north of the observer, beyond its reach");
        return t;
    }

    static int Differing(Image a, Image b)
    {
        foreach (var img in new[] { a, b }) if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);
        byte[] x = a.GetData(), y = b.GetData();
        if (x.Length != y.Length) return int.MaxValue;
        int n = 0;
        for (int i = 0; i + 3 < x.Length; i += 4)
            if (Math.Abs(x[i] - y[i]) > Differs || Math.Abs(x[i + 1] - y[i + 1]) > Differs || Math.Abs(x[i + 2] - y[i + 2]) > Differs) n++;
        return n;
    }

    [ScreenTest]
    static async Task Nothing_but_the_pieces_in_sight_marks_a_first_person_pov_picture(TestRunner r)
    {
        var t = await HillTable(r);
        var fire = t.S.Store.Pack(FirmeEffects.PackId)!.Pieces.First(p => p.Effect is { Kind: "fire" });
        t.S.Do(new SpawnPiece { Layer = U, Def = "block", Id = "far", Pos = new Vec2(-4, HillAt), RotDeg = 180 });
        t.S.Do(new SpawnPiece { Layer = U, Def = fire.Id, Id = "fire", Pos = new Vec2(2, HillAt - 1), RotDeg = 180 });
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture began");
        t.Table.Sighting.SetFirstPerson(true);
        t.Table.Sighting.Face("N");
        await r.Seconds(1.5);
        t.Table.ShowRemoteCursor(7, "Other umpire", new Vec2(1, 5));
        await r.Frames(3);
        async Task<Image> Picture(string what) => await t.Table.RenderSighting(d, EyePixels) ?? throw new TestFailure("no picture " + what);
        await Picture("to warm the ground up");
        Check.Equal("p1,p2", InSight(d), "the block and the fire on the hill are out of reach");
        var busy = await Picture("of the table as the umpires see it");
        t.Table.ForgetCursor(7);
        t.S.Do(new RemoveZone { Id = "z1" });
        await r.Frames(3);
        var noAids = await Picture("without the zone and the hand");
        Check.That(Differing(busy, noAids) <= Stray, $"a zone and another umpire's hand, or its shadow, leave no mark on the picture: {Differing(busy, noAids)} pixels differ");
        t.S.Do(new DeletePiece { Layer = U, Id = "far" });
        t.S.Do(new DeletePiece { Layer = U, Id = "fire" });
        await r.Frames(3);
        var bare = await Picture("without the pieces out of sight");
        Check.That(Differing(noAids, bare) <= Stray, $"pieces left out of the picture leave no shadow, light or smoke in it: {Differing(noAids, bare)} pixels differ");
        d.Radius = FarReach;
        var wide = await Picture("with the hill within reach");
        t.S.Do(new SpawnPiece { Layer = U, Def = "block", Id = "far", Pos = new Vec2(-4, HillAt), RotDeg = 180 });
        await r.Frames(3);
        var seen = await Picture("with the block back on the hill");
        Check.That(Differing(wide, seen) > BlockPixels, $"the same block within reach does show, so the comparison sees the hill: {Differing(wide, seen)} pixels differ");
        t.Table.CancelSighting();
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
