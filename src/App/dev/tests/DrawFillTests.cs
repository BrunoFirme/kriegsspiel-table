using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class DrawFillTests
{
    const string U = Layers.Umpire;
    const double Half = 8, Pitch = 10;

    static List<Stroke> Drawn(OfflineTable t) => t.S.View.Drawings.GetValueOrDefault(U) ?? new();

    static async Task Rectangle(TestRunner r, OfflineTable t, Vec2 from, Vec2 to)
    {
        t.Table.SetTool(Tool.Shape);
        t.Table.ShapeKind = "rect";
        t.Table.Pen.PressShape(from);
        t.Table.Pen.MoveShape(to);
        t.Table.Pen.ReleaseShape();
        await r.Frames(2);
    }

    static async Task<Stroke?> FillAt(TestRunner r, OfflineTable t, Vec2 at)
    {
        int before = Drawn(t).Count;
        t.Table.SetTool(Tool.Shape);
        t.Table.ShapeKind = PenTool.FillKind;
        t.Table.Pen.PressShape(at);
        await r.Frames(2);
        return Drawn(t).Count > before ? Drawn(t)[^1] : null;
    }

    static (double X0, double Z0, double X1, double Z1) Bounds(Stroke s) => (s.Points.Min(p => p.X), s.Points.Min(p => p.Z), s.Points.Max(p => p.X), s.Points.Max(p => p.Z));

    static bool Near(double a, double b, double by) => Math.Abs(a - b) <= by;

    static bool Fills(Stroke s, double near, double tol) =>
        Bounds(s) is var (x0, z0, x1, z1) && Near(x0, -near, tol) && Near(x1, near, tol) && Near(z0, -near, tol) && Near(z1, near, tol);

    [NodeTest]
    static async Task The_fill_on_the_picked_layer_alone_floods_inside_a_drawn_shape_and_a_grid_cell_on_its_exact_edges(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g => g.Layer(U)[0].Pos = new Vec2(60, 60));
        await r.Frames(2);
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), 52f);
        await r.Frames(2);
        t.Table.Settings.FillStopsAtGrid = true;
        await Rectangle(r, t, new Vec2(-Half, -Half), new Vec2(Half, Half));
        var inside = await FillAt(r, t, new Vec2(1, 2)) ?? throw new TestFailure("the click inside the rectangle laid nothing");
        double tol = 1.0 / Math.Max(1e-6, t.Table.Rig.PixelsPerUnit()) * 3 + 0.2;
        Check.That(inside.Fill && inside.Width == 0 && inside.Color == Drawn(t)[0].Color, $"the fill is an area in the ink with no line: fill {inside.Fill}, width {inside.Width}, {inside.Color}");
        Check.That(Fills(inside, Half, tol), $"it floods the rectangle to its line: {Bounds(inside)}");
        var outside = await FillAt(r, t, new Vec2(Half + 2, 0)) ?? throw new TestFailure("the click outside laid nothing");
        Check.That(Bounds(outside) is var (ox0, _, ox1, _) && ox1 - ox0 > 4 * Half, $"outside, it runs on to the edge of the view: {Bounds(outside)}");
        Check.That(!Polyline.Inside(outside.Points, new Vec2(1, 2)) && Polyline.Inside(outside.Points, new Vec2(Half + 2, 0)), "and leaves the rectangle out, as a hole");

        t.S.Do(new Batch { Items = Drawn(t).Select(s => (Command)new EraseStroke { Layer = U, StrokeId = s.Id }).ToList() });
        t.S.Do(new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = Pitch } });
        await r.Frames(2);
        await Rectangle(r, t, new Vec2(-Half, -Half), new Vec2(Half, Half));
        var g = t.S.View.Grid!;
        var far = new Vec2(33, -21);
        var cell = await FillAt(r, t, far) ?? throw new TestFailure("the click in an empty hex laid nothing");
        var corners = GridCells.Corners(g, GridCells.CellOf(g, far));
        Check.That(cell.Points.Count == 6 && cell.Points.Zip(corners).All(p => (p.First - p.Second).Length < 1e-9), $"an empty hex is filled on its exact corners: {cell.Points.Count} points, {Bounds(cell)} against {(corners.Min(p => p.X), corners.Min(p => p.Z), corners.Max(p => p.X), corners.Max(p => p.Z))}, fill {cell.Fill}");
        var part = await FillAt(r, t, new Vec2(1, 2)) ?? throw new TestFailure("the click in the rectangle's hex laid nothing");
        var home = GridCells.Corners(g, GridCells.CellOf(g, new Vec2(1, 2)));
        Check.That(part.Points.All(p => Polyline.Inside(home, p) || SightingDraft.EdgeDistance(home, p) <= tol), "inside a drawn shape, the fill stops at the hex's edges too");

        t.Table.Settings.FillStopsAtGrid = false;
        var free = await FillAt(r, t, far) ?? throw new TestFailure("the click with the grid let go laid nothing");
        Check.That(free.Points.Count > 6, "with Stop at the grid off the fill runs past the cells");
    }

    [NodeTest]
    static async Task A_fill_round_a_hole_is_drawn_round_it_and_not_over_it(TestRunner r)
    {
        await r.Frame();
        var outer = new List<Vec2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        var holes = new[] { new List<Vec2> { new(3, 3), new(3, 6), new(6, 6), new(6, 3) }, new List<Vec2> { new(7, 1), new(7, 2), new(8, 2), new(8, 1) } };
        var tris = new List<Vector3>();
        Ribbon.AddFill(tris, Polyline.Keyholed(new() { outer, holes[0], holes[1] }), 0);
        double area = 0;
        for (int i = 0; i + 2 < tris.Count; i += 3)
        {
            Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
            area += Math.Abs((b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z)) / 2;
        }
        Check.That(Math.Abs(area - (100 - 9 - 1)) < 1e-3, $"the wash covers the ring and not its holes: {area:0.###} of 90");
    }

    [NodeTest]
    static async Task The_draw_panel_offers_the_fill_with_reach_and_its_ticks_and_stop_at_the_grid_only_with_a_grid(TestRunner r)
    {
        var h = await HudTests.Host(r);
        IHudShell shell = h.Hud;
        shell.OpenPanel("paint");
        await r.Frames(2);
        var body = shell.Panels["paint"].Body;
        List<Control> Shown() => WindowTests.Descendants(body).OfType<Control>().Where(c => c.IsVisibleInTree()).ToList();
        Button Tile() => Shown().OfType<Button>().FirstOrDefault(b => b.TooltipText.StartsWith("Fill", StringComparison.Ordinal))
            ?? throw new TestFailure($"no Fill tile (tool {h.Table.Tool}): " + string.Join(" | ", Shown().OfType<Button>().Select(b => b.TooltipText.Length > 0 ? b.TooltipText : b.Text)));
        bool Ticked(string text) => Shown().OfType<CheckBox>().Any(c => c.Text == text);
        bool Says(string text) => Shown().OfType<Label>().Any(l => l.Text == text);
        Tile().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Table.Tool == Tool.Shape && h.Table.ShapeKind == PenTool.FillKind, "the Fill tile takes up the bucket");
        Check.That(Says("Reach") && !Ticked("Stop at the grid"), "it offers Reach, and no grid tick without a grid");
        Check.That(!Shown().OfType<HSlider>().Any(s => s.TooltipText.StartsWith("How thick the line", StringComparison.Ordinal)), "and no pen width: a fill has no line");
        Check.That(h.Table.Pen.ShapeHint() == "Click an area to flood it with the ink", "the hint says what a click does: " + h.Table.Pen.ShapeHint());
        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = Pitch } });
        await r.Frames(3);
        Check.That(Ticked("Stop at the grid"), "a grid laid while the panel is open brings Stop at the grid");
        h.S.Do(new AddBoard { Name = SightBoard.Named, Sight = true });
        for (int i = 0; i < 60 && !h.S.View.Boards.Any(b => b.Sight); i++) await r.Frame();
        (Shown().OfType<Button>().FirstOrDefault(b => b.Text == SightBoard.Named) ?? throw new TestFailure("no Sight layer row")).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(Tile().Disabled, "the fill is not for sight lines");
    }

    [NodeTest]
    static async Task The_agent_fills_ground_inside_one_grid_cell_only_on_a_gridded_table(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        const string Filled = """{"mode":"paint","kind":"grass","fill":true,"cell":true,"points":[[0,0]]}""";
        Check.That((await AgentRecordTests.Ask(agent, "shape_ground", Filled)).error, "a fill kept to a cell needs the scenario's grid");
        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = Pitch } });
        await r.Frames(2);
        Check.That((await AgentRecordTests.Ask(agent, "shape_ground", """{"mode":"paint","kind":"grass","cell":true,"points":[[0,0]]}""")).error, "cell goes with fill");
        var (error, said) = await AgentRecordTests.Ask(agent, "shape_ground", Filled);
        Check.That(!error, "the hex is filled: " + said);
        await r.Frames(3);
        byte grass = GroundKinds.Of("grass")!.Value;
        Check.That(h.S.Ground.KindAt(new Vec2(1, 1)) == grass && h.S.Ground.KindAt(new Vec2(2 * Pitch, 0)) != grass, "grass in the hex clicked and none two hexes on");
    }

    [NodeTest]
    static async Task Every_shown_drawing_layer_stops_the_fill_and_the_map_hidden_layers_and_pieces_never_do(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "", Pos = new Vec2(0, 0), Size = new Size2(80, 80) });
            g.Layer(U)[0].Pos = new Vec2(2, 1);
        });
        await r.Frames(2);
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), 52f);
        t.S.Do(new AddBoard { Name = "Orders" });
        for (int i = 0; i < 60 && t.S.View.Boards.Count < 2; i++) await r.Frame();
        var (first, orders) = (t.S.View.Boards[0].Id, t.S.View.Boards[^1].Id);
        t.Table.Board = orders;
        await Rectangle(r, t, new Vec2(-Half, -Half), new Vec2(Half, Half));
        t.Table.Board = first;
        double tol = 1.0 / Math.Max(1e-6, t.Table.Rig.PixelsPerUnitAt(Coords.ToWorld(new Vec2(0, 0)))) * 3 + 0.2;
        var held = await FillAt(r, t, new Vec2(-3, -2)) ?? throw new TestFailure("the click inside laid nothing");
        Check.That(held.Board == first && Fills(held, Half, tol), $"a rectangle on another shown layer holds the fill, laid on the picked layer, round the piece: {Bounds(held)}");
        t.S.Do(new Batch { Items = Drawn(t).Where(s => s.Board == first).Select(s => (Command)new EraseStroke { Layer = U, StrokeId = s.Id }).ToList() });
        await r.Frames(2);
        t.Table.ShowBoard(orders, false);
        var hidden = await FillAt(r, t, new Vec2(-3, -2)) ?? throw new TestFailure("the click past a hidden layer laid nothing");
        Check.That(Bounds(hidden) is var (h0, _, h1, _) && h1 - h0 > 4 * Half, $"a hidden layer's lines do not stop it: {Bounds(hidden)}");
    }
}
