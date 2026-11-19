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
    static async Task A_line_is_as_thick_on_the_map_at_any_zoom_starts_a_tenth_of_a_unit_wide_and_the_rubber_is_as_wide_as_the_line(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        t.Table.Settings.PenUnits = null;
        t.Table.Settings.PenMetres = null;
        await r.Frame();
        Check.That(System.Math.Abs(t.Table.Pen.PenThickness - PenTool.FirstPenUnits) < 1e-9, $"a new pen is a tenth of a unit wide: {t.Table.Pen.PenThickness}");
        t.Table.Rig.Distance *= 3;
        await r.Frames(3);
        Check.That(System.Math.Abs(t.Table.Pen.PenThickness - PenTool.FirstPenUnits) < 1e-9, "zoomed out before anything is drawn, the pen is as wide");
        double Drawn(double z)
        {
            t.Table.Pen.PressDraw(new Vec2(0, z));
            t.Table.Pen.MoveDraw(new Vec2(40, z));
            t.Table.Pen.ReleaseDraw();
            return t.S.View.Drawings[U][^1].Width;
        }
        double first = Drawn(0);
        Check.That(System.Math.Abs(first - PenTool.FirstPenUnits) < 1e-9, $"the first line is as thick as the pen: {first}");
        t.Table.Rig.Distance *= 4;
        await r.Frames(3);
        Check.That(System.Math.Abs(Drawn(5) - first) < 1e-9, "zoomed out, the next line is as thick on the map");
        t.Table.Pen.PenThickness = 12.5;
        Check.That(System.Math.Abs(Drawn(10) - 12.5) < 1e-9, "a typed thickness is the line's width in table units");
        Check.That(System.Math.Abs(t.Table.Pen.RubRadius() - 12.5 / 2) < 1e-9, $"the rubber is as wide as the line: {t.Table.Pen.RubRadius()}");
        t.Table.Pen.Resize(larger: true);
        Check.That(t.Table.Pen.PenThickness > 12.5 && System.Math.Abs(t.Table.Pen.RubRadius() - t.Table.Pen.PenThickness / 2) < 1e-9, "a step larger thickens it, and the rubber with it");
        t.Table.Pen.PenThickness = 1e9;
        Check.That(t.Table.Pen.PenThickness == PenTool.MostPen, "no thicker than the widest");
        t.S.Do(new KTable.Core.Protocol.SetCalibration { UnitsPerMeter = 0.02, Units = Calibration.Paces });
        await r.Frames(2);
        t.Table.Pen.PenShown = 100;
        double paced = Drawn(15);
        Check.That(System.Math.Abs(paced - t.S.View.Calibration.ToTableUnits(100)) < 1e-9 && t.Table.Pen.PenSaid == "100 paces", $"on a map measured in paces a line 100 paces thick is that wide: {paced}, {t.Table.Pen.PenSaid}");
        await r.Frame();
    }

    [NodeTest]
    static async Task The_draw_panel_shows_the_pen_s_thickness_takes_a_typed_one_and_follows_the_keys(TestRunner r)
    {
        var h = await HudTests.Host(r);
        IHudShell shell = h.Hud;
        h.Table.Pen.PenThickness = 2;
        shell.TogglePanel("paint");
        await r.Frames(3);
        var body = shell.Panels["paint"].Body;
        HSlider Slid() => WindowTests.Descendants(body).OfType<HSlider>().First(s => !s.IsQueuedForDeletion() && s.TooltipText.Contains("thick", System.StringComparison.Ordinal));
        LineEdit Typed() => Slid().GetParent().GetChildren().OfType<LineEdit>().Single();
        Check.Equal("2 u", Typed().Text, "the box says the thickness in the map's measure");
        Typed().GrabFocus();
        Typed().Text = "37.5";
        Typed().ReleaseFocus();
        await r.Frames(3);
        Check.That(System.Math.Abs(h.Table.Pen.PenThickness - 37.5) < 1e-6, "a typed thickness is the pen's: " + h.Table.Pen.PenThickness);
        Check.That(System.Math.Abs(System.Math.Pow(10, Slid().Value) - 37.5) < 0.05, $"and the slider follows it: {System.Math.Pow(10, Slid().Value)}");
        h.Table.SetTool(Tool.Draw);
        h.Table.Router.ToolKey(new InputEventKey { Keycode = Key.Equal, Pressed = true }, h.Hud.Cfg.Keys);
        await r.Frames(3);
        Check.That(h.Table.Pen.PenThickness > 37.5 && Typed().Text == h.Table.Pen.PenSaid, $"= thickens the pen and the box follows: {Typed().Text}");
        Slid().Value = 0;
        await r.Frames(3);
        Check.That(System.Math.Abs(h.Table.Pen.PenThickness - 1) < 1e-6 && Typed().Text == "1 u", "the slider sets it too: " + Typed().Text);
        h.Table.SetTool(Tool.Select);
        shell.TogglePanel("paint");
    }

    [NodeTest]
    static async Task A_die_set_down_keeps_its_face_and_one_let_go_on_the_move_rolls(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0, packs: new[] { "std.dice" }, prepare: StandardPacks.Ensure);
        await r.Frame();
        t.S.Do(new KTable.Core.Protocol.SpawnPiece { Layer = U, Def = "std.dice.d6", Id = "d1", Pos = new Vec2(0, 0) });
        await r.Frames(5);
        t.Table.Selection.Set(new[] { "d1" });
        int face = t.S.View.Find(U, "d1")!.ShownFace;
        await Carry(r, t, new Vec2(0, 0), new Vec2(6, 4));
        Check.That(!t.Node("d1").Rolling, "carried and set down, it does not roll");
        Check.That((At(t, "d1") - new Vec2(6, 4)).Length < 1e-6, $"it lies where the hand let go: {At(t, "d1")}");
        Check.Equal(face, t.S.View.Find(U, "d1")!.ShownFace, "the face it showed");
        t.Table.Hand.Start(new Vec2(6, 4));
        for (int i = 1; i <= 5; i++)
        {
            t.Table.Hand.Move(new Vec2(6 + i * 2, 4));
            await r.Seconds(0.03);
        }
        t.Table.Hand.Drop();
        Check.That(t.Node("d1").Rolling, "let go while the hand moves, it rolls");
        for (int i = 0; i < 600 && t.Node("d1").Rolling; i++) await r.Frame();
        Check.That(!t.Node("d1").Rolling, "and comes to rest");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
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
    static async Task A_curve_is_shift_clicked_bend_by_bend_and_a_plain_click_lays_the_last(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = Polyline.CurvedArrow;
        t.Table.Pen.PressShape(new Vec2(0, 0), shift: true);
        t.Table.Pen.ReleaseShape();
        t.Table.Pen.PressShape(new Vec2(40, 30), shift: true);
        t.Table.Pen.ReleaseShape();
        Check.Equal(2, t.Table.Pen.Knots, "a bend for each Shift+click");
        Check.That(!t.Table.Pen.PathKey(new InputEventKey { Keycode = Key.Enter, Pressed = true }), "Enter lays nothing");
        Check.Equal(0, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "nothing is laid before the plain click");
        t.Table.Pen.PressShape(new Vec2(80, 0));
        t.Table.Pen.ReleaseShape();
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
    static async Task A_curve_dragged_out_is_laid_along_its_course_on_release(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = Polyline.Curve;
        t.Table.Pen.PressShape(new Vec2(0, 0));
        foreach (var x in new[] { 10, 20, 30, 40, 50, 60 }) t.Table.Pen.MoveShape(new Vec2(x, 12 * System.Math.Sin(x / 20.0)));
        t.Table.Pen.ReleaseShape();
        await r.Frame();
        var line = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(line.Points[0] == new Vec2(0, 0) && (line.Points[^1] - new Vec2(60, 12 * System.Math.Sin(3))).Length < 1e-6, "it runs from the press to the release");
        Check.That(!t.Table.Pen.Shaping, "laid, nothing left in hand");
        t.Table.Pen.PressShape(new Vec2(80, 80));
        t.Table.Pen.ReleaseShape();
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "a plain click alone lays nothing");
    }

    [NodeTest]
    static async Task A_table_with_more_round_strokes_and_inks_than_a_mesh_holds_draws_them_all_and_takes_more(TestRunner r)
    {
        const int Inks = DrawingLayer.MostSurfaces + 44;
        var t = OfflineTable.Make(r, pieces: 0, arrange: g => g.Drawings[U] = Enumerable.Range(0, Inks * 2).Select(i => new Stroke
        {
            Id = $"d-{i + 1:D6}", Round = true, Width = 0.5, Color = $"#{i % Inks:X6}",
            Points = new() { new Vec2(i % 40, i / 40), new Vec2(i % 40 + 0.5, i / 40 + 0.5) },
        }).ToList());
        await r.Frame();
        Check.Equal(Inks, t.Table.Drawings.DrawnSurfaces(), "a surface for each ink, across two meshes");
        t.Table.PenColor = Colors.Red;
        t.Table.Pen.PressDraw(new Vec2(-5, -5));
        t.Table.Pen.MoveDraw(new Vec2(-1, -5));
        t.Table.Pen.ReleaseDraw();
        await r.Frame();
        Check.Equal(Inks * 2 + 1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "the new stroke is laid");
        Check.Equal(Inks + 1, t.Table.Drawings.DrawnSurfaces(), "and drawn in its own ink");
    }

    [NodeTest]
    static async Task An_area_closes_on_its_first_point_washed_in_the_ink_and_the_rubber_takes_it_from_inside(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ShapeKind = Polyline.Area;
        t.Table.Pen.PressShape(new Vec2(0, 0), shift: true);
        t.Table.Pen.ReleaseShape();
        t.Table.Pen.PressShape(new Vec2(10, 0), shift: true);
        t.Table.Pen.PressShape(new Vec2(10, 10), shift: true);
        t.Table.Pen.PressShape(new Vec2(0, 10), shift: true);
        t.Table.Pen.PressShape(new Vec2(0.01, 0.01), shift: true);
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
        t.Table.Pen.PressShape(new Vec2(0, 0), shift: true);
        t.Table.Pen.PressShape(new Vec2(40, 30), shift: true);
        Check.That(t.Table.Pen.PathKey(new InputEventKey { Keycode = Key.Backspace, Pressed = true }), "Backspace is taken");
        Check.Equal(1, t.Table.Pen.Knots, "one bend taken back");
        t.Table.Pen.PressShape(new Vec2(60, 10));
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "a plain click lays it");
        t.Table.Pen.PressShape(new Vec2(0, 50), shift: true);
        t.Table.Pen.PressShape(new Vec2(30, 50), shift: true);
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
    static async Task A_name_is_typed_on_the_map_where_it_goes_at_its_letter_size_and_laid_by_enter(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var letters = t.Table.Letters;
        t.Table.SetTool(Tool.Letter);
        letters.LetterKept = 2.5;
        letters.Press(new Vec2(3, 2), false);
        letters.Release();
        Check.That(letters.Open, "a click starts a name in place");
        letters.Type("Uru");
        await r.Frames(4);
        Check.That(t.Table.MapLettering.Previewed > 0 && t.Table.MapLettering.Caret is { } caret && caret.At.X > 3, "the letters stand on the map as they are typed, the caret after them");
        Check.Equal(0, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "nothing is laid while typing");
        letters.Type("Uru-Anna");
        letters.Commit();
        await r.Frame();
        var name = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.Equal("Uru-Anna", name.Text, "enter lays the name");
        Check.Equal(2.5, name.Letter ?? 0, "at the letter size of the panel");
        Check.Equal(new Vec2(3, 2), name.Points.Single(), "where it was clicked");
        Check.That(!letters.Open && t.Table.MapLettering.Previewed == 0 && t.Table.MapLettering.Caret == null, "and the typing ends");
        await r.Frames(4);
        var quads = t.Table.MapLettering.Root.GetChildren().OfType<MeshInstance3D>().ToList();
        Check.That(quads.Count > 0 && quads.All(q => q.Transform.Basis.Determinant() > 0), "a name on plain paper reads the right way, not mirrored");

        letters.Resize(true, new Vec2(3, 2));
        await r.Frame();
        var larger = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(larger.Letter > 2.5 && larger.Text == "Uru-Anna" && larger.Points.Single() == new Vec2(3, 2), "= over a name makes its letters larger");

        letters.Press(new Vec2(3, 2), true);
        Check.That(letters.Open && letters.Typed == "Uru-Anna" && t.Table.MapLettering.Hidden == larger.Id, "a double-click retypes the name in place, the laid one hidden meanwhile");
        letters.Type("Ur");
        letters.Commit();
        await r.Frame();
        var retyped = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(retyped.Text == "Ur" && retyped.Letter == larger.Letter && t.Table.MapLettering.Hidden == null, "the retyped name keeps its place and size");

        letters.Press(new Vec2(-5, -5), false);
        letters.Release();
        letters.Type("Gone");
        letters.Drop();
        await r.Frame();
        Check.Equal(1, (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Count, "Esc drops a name being typed");
        Check.Equal(0, t.Table.MapLettering.Previewed, "and its letters with it");
    }

    [NodeTest]
    static async Task A_name_keeps_the_panel_s_ink_spacing_and_alignment_and_a_double_click_takes_them_back_into_the_panel(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        var cfg = t.Table.Settings;
        cfg.NameUnits = null;
        cfg.NameMetres = null;
        await r.Frame();
        var letters = t.Table.Letters;
        Check.That(System.Math.Abs(letters.LetterKept - GroundNames.PlaceLetter) < 1e-9, $"new letters stand as tall as the map's place names: {letters.LetterKept}");
        t.Table.Rig.Distance *= 3;
        await r.Frames(3);
        Check.That(System.Math.Abs(letters.LetterKept - GroundNames.PlaceLetter) < 1e-9, "at any zoom");
        t.Table.SetTool(Tool.Letter);
        cfg.NameInk = "#3366cc";
        cfg.NameAlign = Stroke.AlignRight;
        cfg.NameSpacing = 0.25;
        letters.Press(new Vec2(3, 2), false);
        letters.Release();
        letters.Type("Uru");
        await r.Frames(4);
        Check.That(t.Table.MapLettering.Caret is { } caret && System.Math.Abs(caret.At.X - 3) < 0.05, $"right-aligned, the name ends at the click: {t.Table.MapLettering.Caret?.At}");
        letters.Commit();
        await r.Frame();
        var name = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(name is { Color: "#3366cc", Align: Stroke.AlignRight, Spacing: 0.25, Text: "Uru" }, $"the name keeps its ink, alignment and spacing: {name.Color} {name.Align} {name.Spacing}");

        cfg.NameInk = "";
        cfg.NameAlign = Stroke.AlignLeft;
        cfg.NameSpacing = 0;
        letters.Press(new Vec2(2.2, 2.1), true);
        Check.That(letters.Open && cfg.NameInk == "#3366cc" && cfg.NameAlign == Stroke.AlignRight && cfg.NameSpacing == 0.25,
            "a double-click on the name, left of its point, retypes it and the panel takes its style");
        cfg.NameAlign = Stroke.AlignCentre;
        letters.Restyle(keys: false);
        await r.Frames(2);
        Check.That(t.Table.MapLettering.Caret is { } centred && centred.At.X > 3.1, "the panel restyles the name in hand: centred, it now runs past the click");
        letters.Commit();
        await r.Frame();
        var restyled = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single();
        Check.That(restyled is { Align: Stroke.AlignCentre, Color: "#3366cc", Text: "Uru" } && restyled.Points.Single() == new Vec2(3, 2), "laid again where it was, centred");

        cfg.NameAlign = Stroke.AlignSpread;
        letters.Press(new Vec2(-10, -5), false);
        for (int x = -9; x <= 0; x++) letters.Motion(new Vec2(x, -5));
        letters.Release();
        letters.Type("Long Way");
        letters.Commit();
        await r.Frame();
        var path = (t.S.View.Drawings.GetValueOrDefault(U) ?? new()).Single(st => st.Text == "Long Way");
        Check.That(path.Points.Count >= 2 && path.Align == Stroke.AlignSpread, "a dragged path carries the name spread along it");
        Check.That(t.Errors.Count == 0, "no command refused: " + string.Join("; ", t.Errors));
        cfg.NameInk = "";
        cfg.NameAlign = Stroke.AlignLeft;
        cfg.NameSpacing = 0;
    }

    [NodeTest]
    static async Task A_click_with_the_ground_ellipse_paints_a_circle_and_one_with_the_hill_raises_a_hill_of_the_radius(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(40, 40);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new KTable.Core.Protocol.AddSheet
        {
            Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = KTable.Core.Protocol.UploadAsset.ReliefPackage, Size = new Size2(40, 40), Relief = sha, Height = 1 },
        });
        t.S.Do(new KTable.Core.Protocol.SetCalibration { UnitsPerMeter = 0.02 });
        int shared = 0;
        t.Table.ReliefShaped += (_, c) => { shared++; t.S.Do(c); };
        await r.Frame();
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        var relief = t.Table.Relief;
        cfg.ReliefMode = "paint:field";
        cfg.ReliefPaint = "ellipse";
        cfg.ReliefCircle = 6;
        relief.Press(new Vec2(5, 5), false, false);
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && shared < 1; i++) await r.Frame();
        Check.Equal(1, shared, "a click lays the circle as one change");
        var field = GroundKinds.Of("field")!.Value;
        Check.That(t.S.Ground.KindAt(new Vec2(5, 10)) == field && t.S.Ground.KindAt(new Vec2(0.5, 5)) == field, "field out to the radius");
        Check.Equal(GroundKinds.None, t.S.Ground.KindAt(new Vec2(5, 13)), "nothing past it");

        cfg.ReliefMode = ReliefTool.HillMode;
        cfg.ReliefCircle = 8;
        relief.Press(new Vec2(-10, -10), false, false);
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && shared < 2; i++) await r.Frame();
        Check.Equal(2, shared, "a click with the hill lays one");
        Check.That(t.S.Ground.At(new Vec2(-10, -10)) > t.S.Ground.At(new Vec2(-10, -4)) && t.S.Ground.At(new Vec2(-10, -4)) > 0 && t.S.Ground.At(new Vec2(-10, 3)) < 1e-6,
            $"its summit at the click, its foot at the radius: {t.S.Ground.At(new Vec2(-10, -10)):0.##} {t.S.Ground.At(new Vec2(-10, -4)):0.##} {t.S.Ground.At(new Vec2(-10, 3)):0.##}");
        cfg.ReliefMode = "raise";
        cfg.ReliefPaint = "brush";
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

    }

    const float CloseView = 3, FarView = 400, MostLiftPx = 2.5f, MostLiftUnits = 0.05f;

    [NodeTest]
    static async Task Tool_lines_lie_on_the_ground_however_close_the_view(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), CloseView);
        await r.Frames(2);
        float close = t.Table.Overlay.Lift;
        Check.That(close * t.Table.Rig.PixelsPerUnit() <= MostLiftPx, $"a tool line is {close * t.Table.Rig.PixelsPerUnit():0.0} pixels off the ground in a close view");
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), FarView);
        await r.Frames(2);
        Check.That(t.Table.Overlay.Lift <= MostLiftUnits && t.Table.Overlay.Lift >= close, $"and never more than {MostLiftUnits} units off it from afar: {t.Table.Overlay.Lift:0.000}");
    }

    [NodeTest]
    static async Task A_brush_stroke_patches_the_sheet_under_it_and_lands_without_rebuilding_it(TestRunner r)
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
        var painted = node;
        int builtPainting = node.Built;
        relief.Release();
        for (int i = 0; i < ShapeWaitFrames && t.S.View.Sheets[0].Relief == sha; i++) await r.Frame();
        Check.That(t.S.View.Sheets[0].Relief != sha, "the stroke committed a new relief");
        await r.Frames(6);
        Check.That(t.Table.Sheets.TryGet("g", out node) && node == painted, "the committed relief keeps the sheet's node");
        Check.Equal(builtPainting, node.Built, "the stroke lands as it was drawn, with no rebuild of the sheet");
        Check.Equal(wood, t.S.Ground.KindAt(new Vec2(0, 0)), "and the wood is still under where the brush went");
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
        Check.That(t.Table.Sheets.TryGet("g", out var raised) && raised == node && node.Built == rebuilt, "the raise lands in the same node with no rebuild");
        Check.That(t.S.Ground.At(new Vec2(4, 6)) > 0, "and the ground is still higher there");
        t.S.Relief.Invalidate();
        await r.Frames(6);
        Check.That(node.Built > rebuilt && t.S.Ground.At(new Vec2(4, 6)) > 0 && t.S.Ground.KindAt(new Vec2(0, 0)) == wood, "a full rebuild shows the same ground");
    }
}
