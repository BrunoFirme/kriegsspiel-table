using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PenPassTests
{
    const string U = Layers.Umpire;
    const double Near = 1e-6;

    static List<Stroke> Drawn(OfflineTable t) => t.S.View.Drawings.GetValueOrDefault(U) ?? new();

    static double Bearing(Vec2 from, Vec2 to) => Math.Atan2(to.X - from.X, to.Z - from.Z) * 180 / Math.PI;

    static bool OnAStep(double degrees, double step) => Math.Abs(degrees / step - Math.Round(degrees / step)) < 1e-6;

    [NodeTest]
    static async Task A_click_with_the_pen_leaves_a_dot_as_wide_as_the_pen(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.Pen.PressDraw(new Vec2(-40, -40));
        t.Table.Pen.MoveDraw(new Vec2(-20, -40));
        t.Table.Pen.ReleaseDraw();
        await r.Frame();
        double pen = Drawn(t).Single().Width;
        var at = new Vec2(2, 3);
        t.Table.Pen.PressDraw(at);
        t.Table.Pen.ReleaseDraw();
        await r.Frame();
        var dot = Drawn(t).Last();
        double reach = dot.Points.Max(p => (p - at).Length) + dot.Width / 2;
        Check.That((dot.Points[0] - dot.Points[^1]).Length < Near, "the dot is a closed ring");
        Check.That(Math.Abs(dot.Points.Max(p => (p - at).Length) - dot.Width / 2) < Near, "drawn so thick that it is filled to its middle");
        Check.That(Math.Abs(reach * 2 - pen) < Near, $"and as wide as a line the pen draws: {reach * 2:0.###} against {pen:0.###}");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(1, Drawn(t).Count, "one undo takes the dot away");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task With_shift_a_drawn_line_runs_straight_by_fifteen_degrees_and_an_oval_is_a_true_circle(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var pen = t.Table.Pen;
        pen.PressDraw(new Vec2(0, 0));
        pen.MoveDraw(new Vec2(2, 0.2), straight: true);
        pen.MoveDraw(new Vec2(5, 1.1), straight: true);
        pen.ReleaseDraw();
        await r.Frame();
        var line = Drawn(t).Single();
        Check.Equal(2, line.Points.Count, "with Shift the stroke is one straight run");
        Check.That(OnAStep(Bearing(line.Points[0], line.Points[1]), 15), $"on a step of 15°: {Bearing(line.Points[0], line.Points[1]):0.###}°");

        pen.ShapeKind = "ellipse";
        pen.PressShape(new Vec2(10, 10));
        pen.MoveShape(new Vec2(16, 12), even: true);
        pen.ReleaseShape();
        await r.Frame();
        var ring = Drawn(t).Last();
        double across = ring.Points.Max(p => p.X) - ring.Points.Min(p => p.X), deep = ring.Points.Max(p => p.Z) - ring.Points.Min(p => p.Z);
        Check.That(Math.Abs(across - deep) < 1e-3 && across > 5, $"with Shift the oval is as deep as it is wide: {across:0.###} by {deep:0.###}");

        pen.ShapeKind = "line";
        pen.PressShape(new Vec2(0, 5));
        pen.MoveShape(new Vec2(4, 6), even: true);
        pen.ReleaseShape();
        await r.Frame();
        var ruled = Drawn(t).Last();
        Check.That(OnAStep(Bearing(ruled.Points[0], ruled.Points[^1]), 15), "and a ruled line keeps to a step of 15°");
        Check.That(pen.Hint().Contains("Shift", StringComparison.Ordinal) && pen.Hint().Contains("dot", StringComparison.Ordinal), "the top line says so: " + pen.Hint());
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task With_the_ellipse_a_click_lays_a_circle_of_the_set_radius_in_the_scenarios_measure(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var pen = t.Table.Pen;
        t.S.Do(new SetCalibration());
        await r.Frame();
        pen.ShapeKind = "ellipse";
        t.Table.SetTool(Tool.Shape);
        t.Table.CircleReach = 7;
        var at = new Vec2(3, -4);
        pen.PressShape(at);
        pen.ReleaseShape();
        await r.Frame();
        var ring = Drawn(t).Single();
        Check.That(ring.Points.All(p => Math.Abs((p - at).Length - 7) < Near), $"every point 7 units from the click: {ring.Points.Min(p => (p - at).Length):0.###} to {ring.Points.Max(p => (p - at).Length):0.###}");
        Check.That((ring.Points[0] - ring.Points[^1]).Length < Near, "the circle is closed");
        Check.That(pen.ShapeHint().Contains("radius 7.0 u", StringComparison.Ordinal), "the top line names the radius: " + pen.ShapeHint());

        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        await r.Frame();
        t.Table.CircleReach = 400;
        pen.PressShape(at);
        pen.ReleaseShape();
        await r.Frame();
        var scaled = Drawn(t).Last();
        Check.That(scaled.Points.All(p => Math.Abs((p - at).Length - 8) < Near), $"on a scaled table 400 m is 8 units: {scaled.Points.Max(p => (p - at).Length):0.###}");
        t.S.Do(new SetCalibration());
        await r.Frame();
        Check.That(Math.Abs(t.Table.CircleReach - 7) < Near, "each measure keeps its own radius: " + t.Table.CircleReach);

        pen.PressShape(new Vec2(20, 20));
        pen.MoveShape(new Vec2(30, 24));
        pen.ReleaseShape();
        await r.Frame();
        var oval = Drawn(t).Last();
        double across = oval.Points.Max(p => p.X) - oval.Points.Min(p => p.X);
        Check.That(Math.Abs(across - 10) < 1e-3, $"a drag still draws the oval dragged: {across:0.###} across");

        int laid = Drawn(t).Count;
        pen.ShapeKind = "rect";
        pen.PressShape(at);
        pen.ReleaseShape();
        await r.Frame();
        Check.Equal(laid, Drawn(t).Count, "a click with the rectangle lays nothing");
        t.S.UndoLast();
        await r.Frame();
        Check.Equal(laid - 1, Drawn(t).Count, "one undo takes the last shape away");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task Holding_tab_while_drawing_measures_and_letting_go_goes_back_to_the_pen(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.SetTool(Tool.Draw);
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        Check.Equal(Tool.Ruler, t.Table.Tool, "Tab held: the ruler is in hand");
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Tab, Pressed = true, Echo = true });
        Check.Equal(Tool.Ruler, t.Table.Tool, "the key repeating changes nothing");
        t.Table.Ruler.Press(new Vec2(0, 0));
        t.Table.Ruler.Drag(new Vec2(3, 0), straight: false);
        Check.That(t.Table.Ruler.Measured is > 2.9 and < 3.1, "and it measures");
        t.Table.Keys.OnKeyUp(new InputEventKey { Keycode = Key.Tab, Pressed = false });
        Check.Equal(Tool.Draw, t.Table.Tool, "Tab let go: back to the pen");
        Check.That(t.Table.Ruler.A == null, "and the measure is gone");
        t.Table.SetTool(Tool.Select);
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Tab, Pressed = true });
        Check.Equal(Tool.Select, t.Table.Tool, "with no pen in hand Tab does nothing");
        t.Table.Keys.OnKeyUp(new InputEventKey { Keycode = Key.Tab, Pressed = false });
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_stretch_told_to_keep_the_interval_stands_the_pieces_that_far_apart_however_long_the_line(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4);
        await r.Frames(2);
        var four = new[] { "p1", "p2", "p3", "p4" };
        bool was = t.Table.Settings.StretchKeepsSpacing;
        t.Table.SetTool(Tool.Formation);
        t.Table.Selection.Set(four);
        t.Table.Settings.Formation = "stretch";
        var forms = t.Table.Forms;
        double across = four.Select(id => t.Node(id).Footprint.X).Max() * (1 + forms.Gap);
        async Task<List<double>> Gaps(bool keep, int far = 40)
        {
            forms.SetKeepsSpacing(keep);
            Check.That(forms.Press(new Vec2(0, 6)), "the line is drawn from where the drag starts");
            for (int i = 1; i <= far; i++) forms.Drag(new Vec2(i * across, 6));
            forms.Release();
            for (int i = 0; i < 200 && t.Table.Hand.Holding; i++) await r.Frame();
            t.Table.Poses.Flush();
            await r.Frames(2);
            var xs = four.Select(id => t.S.View.Find(U, id)!.Pos.X).OrderBy(x => x).ToList();
            return xs.Zip(xs.Skip(1), (a, b) => b - a).ToList();
        }
        var spread = await Gaps(keep: false);
        Check.That(spread.All(g => g > across * 5), "left alone, the four spread to the ends of a long line: " + string.Join(" ", spread.Select(g => g.ToString("0.##"))));
        var kept = await Gaps(keep: true);
        Check.That(kept.All(g => Math.Abs(g - across) < 1e-3), $"told to stop at the interval, on a long line each stands {across:0.###} from the next: " + string.Join(" ", kept.Select(g => g.ToString("0.###"))));
        var close = await Gaps(keep: true, far: 2);
        double touching = across / (1 + forms.Gap);
        Check.That(close.All(g => g < across - 1e-3 && g > touching - 1e-3 && Math.Abs(g - close[0]) < 1e-3),
            $"and on a line shorter than that they close up evenly along it, never nearer than touching ({touching:0.###}): " + string.Join(" ", close.Select(g => g.ToString("0.###"))));
        t.Table.Settings.StretchKeepsSpacing = was;
        t.Table.QueueFree();
    }
}
