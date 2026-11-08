using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

static class ToolTests
{
    const string U = Layers.Umpire;

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    static async Task Carry(TestRunner r, OfflineTable t, Vec2 from, Vec2 to, System.Action? whileHeld = null)
    {
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        whileHeld?.Invoke();
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(5);
    }

    [NodeTest]
    static async Task Pieces_carried_are_set_down_where_the_hand_lets_go_and_one_undo_takes_them_back(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1", "p2" });
        int undos = t.S.MyUndoable.Count;
        await Carry(r, t, new Vec2(1, 0), new Vec2(6, 4));
        Check.Equal(new Vec2(6, 4), At(t, "p1"), "p1 under the hand");
        Check.Equal(new Vec2(7, 4), At(t, "p2"), "p2 where it was beside it");
        Check.Equal(new Vec2(3, 0), At(t, "p3"), "p3 not carried");
        Check.That(!t.Table.Hand.Holding, "the hand is empty");
        Check.Equal(undos + 1, t.S.MyUndoable.Count, "one thing to undo");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(new Vec2(1, 0), At(t, "p1"), "p1 back");
        Check.Equal(new Vec2(2, 0), At(t, "p2"), "p2 back");
    }

    [NodeTest]
    static async Task A_piece_turned_in_the_hand_is_set_down_turned(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.Table.Selection.Set(new[] { "p1" });
        await Carry(r, t, new Vec2(1, 0), new Vec2(1, 3), () => t.Table.Hand.Rotate(90));
        var p = t.S.View.Find(U, "p1")!;
        Check.That(System.Math.Abs(p.RotDeg - 90) < 1e-6 || System.Math.Abs(p.RotDeg - 270) < 1e-6, $"turned a quarter: {p.RotDeg}");
        Check.Equal(new Vec2(1, 3), p.Pos, "where it was let go");
    }

    static void ShakeFor(OfflineTable t, double seconds, double hz = 4)
    {
        for (double s = 0; s <= seconds; s += 1 / 60.0)
            t.Table.Hand.ShakeAt(100 + s, new System.Numerics.Vector2(500 + 80 * (float)System.Math.Sin(2 * System.Math.PI * hz * s), 400));
    }

    static double Spread(OfflineTable t) { var ps = t.S.View.Layer(U).Select(p => p.Pos).ToList(); return ps.Max(p => p.X) - ps.Min(p => p.X); }

    [NodeTest]
    static async Task A_hand_shaken_quickly_sets_its_pieces_down_in_a_clump_round_the_pointer(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        var heard = t.Table.Sound.Kept = new();
        t.Table.Selection.Set(Enumerable.Range(1, 6).Select(i => "p" + i));
        await Carry(r, t, new Vec2(1, 0), new Vec2(1, 6), () => ShakeFor(t, 0.6));
        Check.That(Spread(t) < 3.5, $"a clump, not a line: {Spread(t)} across");
        Check.That(t.S.View.Layer(U).All(p => p.Y < 0.01), "none on another");
        Check.Equal(6, t.S.View.Layer(U).Select(p => p.Pos).Distinct().Count(), "each in a place of its own");
        Check.That(heard.Any(h => h.Sound == Sound.Shuffle), "clattered as shaken");
        Check.That(heard.Any(h => h.Sound == Sound.WoodHeavy), "knocked together");
    }

    [NodeTest]
    static async Task A_slow_to_and_fro_leaves_the_group_as_it_was(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.Selection.Set(Enumerable.Range(1, 6).Select(i => "p" + i));
        await Carry(r, t, new Vec2(1, 0), new Vec2(1, 6), () => ShakeFor(t, 3, hz: 1));
        Check.Equal(5.0, Spread(t), "the line as it was");
    }

    static void PullFromDrawer(OfflineTable t, bool letGoOnTable)
    {
        var size = t.Table.GetViewport().GetVisibleRect().Size;
        t.Table.Palette.Begin("x", at => at.X < size.X / 10);
        t.Table.Palette.Tick(true, new Godot.Vector2(size.X / 20, size.Y / 2));
        Check.Equal(1, t.S.View.Layer(U).Count, "nothing made over the drawer");
        t.Table.Palette.Tick(true, size / 2);
        Check.That(t.Table.Hand.Holding, "in the hand as soon as it is out of the drawer");
        Check.Equal(2, t.S.View.Layer(U).Count, "made as soon as it is out of the drawer");
        t.Table.Palette.End(drop: letGoOnTable);
    }

    [NodeTest]
    static async Task A_piece_pulled_from_the_drawer_is_heard_taken_up_and_set_down_where_let_go(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        var heard = t.Table.Sound.Kept = new();
        PullFromDrawer(t, letGoOnTable: true);
        await r.Frames(30);
        Check.Equal(2, t.S.View.Layer(U).Count, "on the table");
        Check.That(!t.Table.Hand.Holding, "the hand is empty");
        Check.That(heard.Any(h => h.Sound == Sound.Lift), "heard taken up");
        Check.That(heard.Any(h => h.Sound == Sound.Wood), "heard set down");
    }

    [NodeTest]
    static async Task A_piece_pulled_from_the_drawer_and_let_go_over_it_goes_back_and_undo_brings_it_again(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        PullFromDrawer(t, letGoOnTable: false);
        await r.Frame();
        Check.Equal(1, t.S.View.Layer(U).Count, "back in the drawer");
        Check.That(!t.Table.Hand.Holding, "the hand is empty");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(2, t.S.View.Layer(U).Count, "brought back by undo");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_carried_group_put_in_rows_lands_in_that_many_rows(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 6);
        await r.Frame();
        t.Table.Selection.Set(Enumerable.Range(1, 6).Select(i => "p" + i));
        await Carry(r, t, new Vec2(1, 0), new Vec2(0, 10), () => t.Table.Hand.FormRows(2));
        var rows = t.S.View.Layer(U).Select(p => System.Math.Round(p.Pos.Z, 3)).Distinct().Count();
        Check.Equal(2, rows, "rows");
    }

    [NodeTest]
    static async Task A_line_drawn_is_on_the_board_and_the_rubber_through_it_leaves_its_two_ends(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        double rub = t.Table.Pen.RubRadius();
        t.Table.Pen.PressDraw(new Vec2(-10 * rub, 0));
        for (int x = -9; x <= 10; x++) t.Table.Pen.MoveDraw(new Vec2(x * rub, 0));
        t.Table.Pen.ReleaseDraw();
        await r.Frame();
        var lines = t.S.View.Drawings.GetValueOrDefault(U) ?? new();
        Check.Equal(1, lines.Count, "one stroke");
        Check.Equal(21, lines[0].Points.Count, "its points");
        t.Table.Pen.PressErase(new Vec2(0, 0));
        await r.Frame();
        lines = t.S.View.Drawings.GetValueOrDefault(U) ?? new();
        Check.Equal(2, lines.Count, "the two ends left");
        Check.That(lines.All(l => l.Points.All(p => System.Math.Abs(p.X) >= rub * 0.999)), "nothing left under the rubber");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "the rub taken back as one");
    }

    [NodeTest]
    static async Task A_rectangle_dragged_out_is_one_closed_line(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = "rect";
        t.Table.Pen.PressShape(new Vec2(0, 0));
        t.Table.Pen.MoveShape(new Vec2(4, 2));
        t.Table.Pen.ReleaseShape();
        await r.Frame();
        var line = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.Equal(5, line.Points.Count, "corners and back to the start");
        Check.Equal(line.Points[0], line.Points[^1], "closed");
    }

    [NodeTest]
    static async Task A_curve_is_clicked_bend_by_bend_and_laid_with_a_double_click(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = Polyline.CurvedArrow;
        t.Table.Pen.PressShape(new Vec2(0, 0));
        t.Table.Pen.PressShape(new Vec2(40, 30));
        t.Table.Pen.PressShape(new Vec2(80, 0));
        Check.Equal(3, t.Table.Pen.Knots, "a bend for each click");
        t.Table.Pen.PressShape(new Vec2(80, 0), twice: true);
        await r.Frame();
        var lines = t.S.View.Drawings.GetValueOrDefault(U) ?? new();
        Check.Equal(2, lines.Count, "the curve and its head");
        Check.That(lines[0].Points.Count > 6, "the curve is smooth, not three straight runs");
        Check.That(lines[0].Points.Any(p => (p - new Vec2(40, 30)).Length < 1e-9), "it passes through the middle click");
        Check.That(!t.Table.Pen.Shaping, "laid, nothing left in hand");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(0, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "undo takes curve and head together");
    }

    [NodeTest]
    static async Task An_area_closes_on_its_first_point_washed_in_the_ink_and_the_rubber_takes_it_from_inside(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = Polyline.Area;
        t.Table.Pen.PressShape(new Vec2(0, 0));
        t.Table.Pen.MoveShape(new Vec2(10, 0));
        t.Table.Pen.ReleaseShape();
        t.Table.Pen.PressShape(new Vec2(10, 10));
        t.Table.Pen.PressShape(new Vec2(0, 10));
        t.Table.Pen.PressShape(new Vec2(0.01, 0.01));
        await r.Frame();
        var area = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(area.Fill, "an area is filled");
        Check.Equal(area.Points[0], area.Points[^1], "closed");
        Check.That(t.Table.Drawings.DrawnSurfaces() >= 2, "the wash and its outline are drawn");
        t.Table.Pen.PressErase(new Vec2(5, 5));
        await r.Frame();
        Check.Equal(0, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "rubbed out whole from its middle");
    }

    [NodeTest]
    static async Task Backspace_takes_a_bend_back_and_escape_drops_the_curve(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.SetTool(Tool.Shape);
        t.Table.ShapeKind = Polyline.Curve;
        t.Table.Pen.PressShape(new Vec2(0, 0));
        t.Table.Pen.PressShape(new Vec2(40, 30));
        Check.That(t.Table.Pen.PathKey(new InputEventKey { Keycode = Key.Backspace, Pressed = true }), "Backspace is taken");
        Check.Equal(1, t.Table.Pen.Knots, "one bend taken back");
        t.Table.Pen.PressShape(new Vec2(60, 10));
        Check.That(t.Table.Pen.PathKey(new InputEventKey { Keycode = Key.Enter, Pressed = true }), "Enter is taken");
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "Enter lays it");
        t.Table.Pen.PressShape(new Vec2(0, 50));
        t.Table.Pen.PressShape(new Vec2(30, 50));
        t.Table.Pen.Drop();
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "a dropped curve lays nothing");
    }

    [NodeTest]
    static async Task A_name_laid_on_the_map_is_a_stroke_with_text_that_the_rubber_takes_whole(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Letters.Lay(new Vec2(3, 2), "Boggs Mill");
        await r.Frame();
        var name = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.Equal("Boggs Mill", name.Text, "the name is the stroke's text");
        Check.Equal(0.0, name.Width, "no width, so an older client draws nothing");
        Check.Equal(1, name.Points.Count, "placed at its point on bare paper");
        t.Table.Pen.PressErase(new Vec2(3, 2));
        await r.Frame();
        Check.Equal(0, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "the rubber takes a name whole");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "and undo brings it back");
    }

    [NodeTest]
    static Task The_ruler_s_marks_fall_on_round_numbers(TestRunner r)
    {
        Check.Equal(2.0, RulerTool.NiceStep(7), "a line of 7");
        Check.Equal(20.0, RulerTool.NiceStep(80), "a line of 80");
        Check.Equal(500.0, RulerTool.NiceStep(1234), "a line of 1234");
        Check.Equal(1.0, RulerTool.NiceStep(0), "no line");
        return Task.CompletedTask;
    }

    const int ShapeWaitFrames = 300;

    [NodeTest]
    static async Task A_rectangle_and_a_polygon_painted_on_the_ground_fill_their_outlines_and_each_is_one_change(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(40, 40);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new KTable.Core.Protocol.AddSheet
        {
            Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = KTable.Core.Protocol.UploadAsset.ReliefPackage, Size = new Size2(40, 40), RotDeg = 180, Relief = sha, Height = 1 },
        });
        int shared = 0;
        t.Table.ReliefShaped += (_, c) => { shared++; t.S.Do(c); };
        await r.Frame();
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        var relief = t.Table.Relief;
        cfg.ReliefMode = "paint:field";
        cfg.ReliefPaint = "rect";
        relief.Press(new Vec2(-10, -10), false, false);
        relief.Motion(new Vec2(10, 0));
        relief.Release();
        await r.Frames(3);
        Check.Equal(0, shared, "no stroke lands before the map has a scale");
        t.S.Do(new KTable.Core.Protocol.SetCalibration { UnitsPerMeter = 0.02 });
        await r.Frame();
        relief.Press(new Vec2(-10, -10), false, false);
        relief.Motion(new Vec2(10, 0));
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && shared < 1; i++) await r.Frame();
        Check.Equal(1, shared, "the rectangle shared as one change");
        Check.Equal(GroundKinds.Of("field")!.Value, t.S.Ground.KindAt(new Vec2(0, -5)), "field inside the rectangle");
        Check.Equal(GroundKinds.None, t.S.Ground.KindAt(new Vec2(0, 12)), "nothing beyond it");

        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "polygon";
        foreach (var corner in new[] { new Vec2(-15, 5), new Vec2(15, 5), new Vec2(0, 15) })
        {
            relief.Press(corner, false, false);
            relief.Release();
        }
        await r.Frames(3);
        Check.Equal(1, shared, "nothing shared while the polygon is open");
        relief.Press(new Vec2(-15, 5), false, false);
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && shared < 2; i++) await r.Frame();
        Check.Equal(2, shared, "the polygon shared as one change once closed");
        Check.Equal(GroundKinds.Water, t.S.Ground.KindAt(new Vec2(0, 8)), "water inside the polygon");
        Check.Equal(GroundKinds.None, t.S.Ground.KindAt(new Vec2(-14, 14)), "nothing outside it");

        relief.Press(new Vec2(-5, -15), false, false);
        Check.That(t.Table.Router.DropReliefShape(), "Esc drops an open polygon");
        Check.That(!relief.DropShape(), "and nothing is left open");
        t.S.UndoLast();
        await r.Frames(3);
        Check.Equal(GroundKinds.None, t.S.Ground.KindAt(new Vec2(0, 8)), "undo takes the polygon back");
        Check.Equal(GroundKinds.Of("field")!.Value, t.S.Ground.KindAt(new Vec2(0, -5)), "and leaves the rectangle");

        cfg.ReliefMode = "paint:road";
        cfg.ReliefPaint = "route";
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), 40);
        await r.Frame();
        relief.Press(new Vec2(-15, -15), false, false);
        relief.Release();
        relief.Press(new Vec2(15, -15), false, false);
        relief.Release();
        await r.Frames(3);
        Check.Equal(2, shared, "nothing shared while the route is open");
        relief.Press(new Vec2(15, -15), false, false, twice: true);
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && shared < 3; i++) await r.Frame();
        Check.Equal(3, shared, "the route shared as one change");
        Check.Equal(GroundKinds.Road, t.S.Ground.KindAt(new Vec2(-15, -15)), "road at the route's start");
        Check.Equal(GroundKinds.Road, t.S.Ground.KindAt(new Vec2(15, -15)), "road at its end");
        t.S.UndoLast();
        await r.Frames(3);
        Check.Equal(GroundKinds.None, t.S.Ground.KindAt(new Vec2(15, -15)), "undo takes the route back");
    }

    [NodeTest]
    static async Task A_brush_stroke_patches_the_sheet_under_it_and_only_the_commit_rebuilds_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(40, 40);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new KTable.Core.Protocol.AddSheet
        {
            Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = KTable.Core.Protocol.UploadAsset.ReliefPackage, Size = new Size2(40, 40), RotDeg = 180, Relief = sha, Height = 1 },
        });
        t.S.Do(new KTable.Core.Protocol.SetCalibration { UnitsPerMeter = 0.02 });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        await r.Frames(12);
        Check.That(t.Table.Sheets.TryGet("g", out var node), "the ground sheet has a node");
        Check.That(node.Built >= 1, "the sheet was built once its relief arrived");
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        var relief = t.Table.Relief;
        cfg.ReliefMode = "paint:wood";
        cfg.ReliefPaint = "brush";
        int built = node.Built;
        relief.Press(new Vec2(-8, 0), false, false);
        for (int i = 1; i <= 6; i++)
        {
            relief.Motion(new Vec2(-8 + i * 2, 0));
            await r.Frames(12);
        }
        Check.That(node.Patched >= 1, "the stroke patched the sheet while the brush moved");
        Check.That(node.Built <= built + 1, "and rebuilt it at most once, for the first paint on bare ground");
        byte wood = GroundKinds.Of("wood")!.Value;
        Check.Equal(wood, t.S.Ground.KindAt(new Vec2(0, 0)), "the draft already shows wood under the brush");
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && t.S.View.Sheets[0].Relief == sha; i++) await r.Frame();
        Check.That(t.S.View.Sheets[0].Relief != sha, "the stroke committed a new relief");
        await r.Frames(6);
        Check.That(t.Table.Sheets.TryGet("g", out node), "the committed relief recuts the sheet into a new node");
        Check.That(node.Built >= 1, "built whole from the committed relief");
        int rebuilt = node.Built, patched = node.Patched;
        cfg.ReliefMode = "raise";
        relief.Press(new Vec2(0, 6), false, false);
        for (int i = 1; i <= 4; i++)
        {
            relief.Motion(new Vec2(i * 2, 6));
            await r.Frames(12);
        }
        Check.That(node.Patched > patched, "raising patched the heights under the brush");
        Check.Equal(rebuilt, node.Built, "without rebuilding the sheet");
        Check.That(t.S.Ground.At(new Vec2(4, 6)) > 0, "the draft is already higher under the brush");
        var committed = t.S.View.Sheets[0].Relief;
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && t.S.View.Sheets[0].Relief == committed; i++) await r.Frame();
        Check.That(t.S.View.Sheets[0].Relief != committed, "the raise committed a new relief");
        await r.Frames(6);
        Check.That(t.Table.Sheets.TryGet("g", out node) && node.Built >= 1 && node.Patched == 0, "the commit rebuilds the sheet whole in a fresh node");
    }
}
