using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Client.Studio;

namespace KTable.App;

static class ShapePickTests
{
    static void DragScreen(StudioCanvas c, Vector2 a, Vector2 b)
    {
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = a });
        c._GuiInput(new InputEventMouseMotion { Position = (a + b) / 2, Relative = (b - a) / 2, ButtonMask = MouseButtonMask.Left });
        c._GuiInput(new InputEventMouseMotion { Position = b, Relative = (b - a) / 2, ButtonMask = MouseButtonMask.Left });
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = b });
    }

    static void Drag(StudioCanvas c, Vector2 fromDoc, Vector2 toDoc) => DragScreen(c, c.ToScreen(fromDoc), c.ToScreen(toDoc));

    static void Click(StudioCanvas c, Vector2 doc)
    {
        var at = c.ToScreen(doc);
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
    }

    static bool Press(StudioCanvas c, Key key) => c.Key(new InputEventKey { Keycode = key, Pressed = true });

    static async Task<StudioCanvas> Canvas(TestRunner r, ArtDoc doc)
    {
        var c = new StudioCanvas { Size = new Vector2(800, 600) };
        r.AddChild(c);
        await r.Frame();
        c.Open(doc);
        c.Fit();
        return c;
    }

    static ArtDoc Blank()
    {
        var doc = new ArtDoc { W = 200, H = 200 };
        doc.Layers.Add(new ArtLayer { Name = "Paint", Kind = "paint" });
        return doc;
    }

    static Rect2 Span(PaintOp op)
    {
        var pts = op.Pts();
        var b = new Rect2(pts[0], Vector2.Zero);
        foreach (var p in pts) b = b.Expand(p);
        return b;
    }

    static bool Near(Rect2 a, float x0, float y0, float x1, float y1) =>
        Mathf.Abs(a.Position.X - x0) < 0.6f && Mathf.Abs(a.Position.Y - y0) < 0.6f && Mathf.Abs(a.End.X - x1) < 0.6f && Mathf.Abs(a.End.Y - y1) < 0.6f;

    [NodeTest]
    static async Task A_shape_just_drawn_stays_in_hand_and_moves_resizes_and_turns_by_its_handles(TestRunner r)
    {
        var c = await Canvas(r, Blank());
        var layer = c.Doc.Layers[0];
        c.P.ShapeKind = "rect";
        c.P.ShapeFill = "line";
        c.P.Line = 4;
        c.SetTool("shape");

        Drag(c, new Vector2(40, 40), new Vector2(100, 80));
        Check.That(layer.Ops.Count == 1 && c.Picked.Any && c.Tool == "shape", $"one rectangle, still in hand, the tool still Shapes (ops {layer.Ops.Count}, picked {c.Picked.Any}, tool {c.Tool})");
        Check.That(c.Picked.Handles().Length == 8 && c.Picked.Knob() != null, "a rectangle has eight handles and a turn knob");

        Drag(c, new Vector2(55, 40), new Vector2(65, 50));
        Check.That(layer.Ops.Count == 1 && Near(Span(layer.Ops[0]), 50, 50, 110, 90), $"dragged by its line it moved, and no new shape was drawn (ops {layer.Ops.Count}, at {Span(layer.Ops[0])})");

        var right = c.Picked.Handles()[5];
        DragScreen(c, right, right + new Vector2(20 * c.Zoom, 0));
        Check.That(Near(Span(layer.Ops[0]), 50, 50, 130, 90), $"the right handle widened it by what it was dragged (at {Span(layer.Ops[0])})");

        var knob = c.Picked.Knob()!.Value.Knob;
        var middle = c.ToScreen(new Vector2(90, 70));
        DragScreen(c, knob, middle + new Vector2((knob - middle).Length(), 0));
        Check.That(Near(Span(layer.Ops[0]), 70, 30, 110, 110) && Mathf.Abs((layer.Ops[0].R ?? 0) - 90) < 0.5f, $"the knob turned it a quarter turn about its middle (at {Span(layer.Ops[0])}, turn {layer.Ops[0].R})");

        var corner = c.Picked.Handles()[2];
        DragScreen(c, corner, corner + (corner - middle) * 0.5f);
        var grown = Span(layer.Ops[0]);
        Check.That(Mathf.Abs(grown.Size.X / grown.Size.Y - 0.5f) < 0.03f && grown.Size.Y is > 100 and < 105,$"a corner of the turned shape grows it in its own proportions (size {grown.Size})");
        c.Undo();
        layer = c.Doc.Layers[0];
        Check.That(Near(Span(layer.Ops[0]), 70, 30, 110, 110) && c.Picked.Any, "one undo takes the resize back and the shape stays in hand");

        Drag(c, new Vector2(90, 70), new Vector2(100, 80));
        Check.That(layer.Ops.Count == 2 && c.Picked.Ops.Single() == layer.Ops[1], $"a drag begun inside the outline draws the next shape, which is now the one in hand (ops {layer.Ops.Count})");

        Check.That(Press(c, Key.Escape) && !c.Picked.Any && layer.Ops.Count == 2, "Esc lets the shape go and leaves it drawn");
        await r.Frames(2);
        c.QueueFree();
    }

    [NodeTest]
    static async Task The_move_tool_picks_one_drawn_shape_a_line_by_its_ends_and_the_layer_by_its_freehand(TestRunner r)
    {
        var doc = Blank();
        doc.Layers[0].Ops.AddRange(new[]
        {
            new PaintOp { K = "shape", P = new() { 20, 20, 80, 20, 80, 60, 20, 60 }, W = 4, C = "#00ff00ff" },
            new PaintOp { K = "path", P = new() { 100, 100, 160, 100 }, W = 4, C = "#ff0000ff" },
            new PaintOp { K = "path", P = new() { 20, 150, 40, 160, 60, 150, 80, 160 }, W = 6 },
        });
        var c = await Canvas(r, doc);
        var layer = c.Doc.Layers[0];
        c.Ink = Colors.Blue;
        c.SetTool("move");
        await r.Frame();

        Drag(c, new Vector2(130, 100), new Vector2(130, 120));
        Check.Equal("100 120 160 120", string.Join(" ", layer.Ops[1].P), "the line dragged down");
        Check.That(Near(Span(layer.Ops[0]), 20, 20, 80, 60) && layer.X == 0 && layer.Y == 0, "the rectangle and the layer stayed where they were");
        Check.That(c.Ink == Colors.Red && c.Picked.Handles().Length == 2 && c.Picked.Knob() == null, $"a picked line has a handle on each end and its colour becomes the ink (ink {c.Ink}, handles {c.Picked.Handles().Length})");

        DragScreen(c, c.Picked.Handles()[0], c.ToScreen(new Vector2(110, 140)));
        Check.Equal("110 140 160 120", string.Join(" ", layer.Ops[1].P), "the line with its first end pulled away");

        c.P.Line = 10;
        c.Picked.Restyle("Line width");
        c.Ink = Colors.Black;
        c.Picked.Restyle("Shape colour");
        Check.That(layer.Ops[1].W == 10 && layer.Ops[1].C == "#000000ff" && layer.Ops[0].C == "#00ff00ff", $"the picked line takes the new width and colour, the rectangle keeps its own (w {layer.Ops[1].W}, c {layer.Ops[1].C})");

        Drag(c, new Vector2(40, 160), new Vector2(50, 160));
        Check.That(!c.Picked.Any && Mathf.Abs(layer.X - 10) < 0.01f, $"freehand is not picked alone: the drag moved the whole layer (picked {c.Picked.Any}, layer x {layer.X})");

        Drag(c, new Vector2(60, 40), new Vector2(60, 45));
        Check.That(!c.Picked.Any && Mathf.Abs(layer.Y - 5) < 0.01f, $"the empty middle of an outline is not the outline: the layer moved (picked {c.Picked.Any}, layer y {layer.Y})");

        Click(c, new Vector2(60, 25));
        Check.That(c.Picked.Ops.SingleOrDefault() == layer.Ops[0], "a click on the rectangle's line picks the rectangle");
        Check.That(Press(c, Key.Right) && Near(Span(layer.Ops[0]), 21, 20, 81, 60), $"an arrow key nudges the picked shape a pixel (at {Span(layer.Ops[0])})");
        Check.That(Press(c, Key.Delete) && layer.Ops.Count == 2 && !c.Picked.Any, $"Delete removes the picked shape alone (ops {layer.Ops.Count})");
        c.Undo();
        Check.That(c.Doc.Layers[0].Ops.Count == 3, "undo brings the shape back");

        Click(c, new Vector2(140 + 10, 130 + 5));
        Check.That(c.Picked.Any && c.Picked.Duplicate() && c.Doc.Layers[0].Ops.Count == 4 && c.Picked.Ops.Single() == c.Doc.Layers[0].Ops[3], "a picked shape copies beside itself and the copy is the one picked");
        await r.Frames(2);
        c.QueueFree();
    }

    [NodeTest]
    static async Task Curves_areas_and_arrows_are_drawn_as_on_the_table_and_picked_whole(TestRunner r)
    {
        var c = await Canvas(r, Blank());
        var layer = c.Doc.Layers[0];
        c.P.Line = 4;
        c.P.ShapeFill = "line";
        c.SetTool("shape");

        c.P.ShapeKind = Polyline.Curve;
        foreach (var knot in new[] { new Vector2(20, 100), new Vector2(60, 60), new Vector2(100, 100) }) Click(c, knot);
        Check.That(Press(c, Key.Enter) && layer.Ops.Count == 1, $"Enter ends the curve (ops {layer.Ops.Count})");
        var curve = layer.Ops[0];
        Check.That(curve.K == "path" && curve.P.Count > 24 && curve.G != null && c.Picked.Any, $"a curve is one smooth line that stays in hand ({curve.P.Count / 2} points)");
        Check.That(curve.Pts()[0].DistanceTo(new Vector2(20, 100)) < 0.1f && curve.Pts()[^1].DistanceTo(new Vector2(100, 100)) < 0.1f, "it runs from the first bend to the last");
        Check.That(c.Picked.Handles().Length == 8, "a curve is resized by a box, not by ends");

        c.P.ShapeKind = Polyline.CurvedArrow;
        c.Picked.Drop();
        foreach (var knot in new[] { new Vector2(20, 150), new Vector2(60, 120), new Vector2(100, 150) }) Click(c, knot);
        Press(c, Key.Enter);
        Check.That(layer.Ops.Count == 3 && layer.Ops[1].G != null && layer.Ops[1].G == layer.Ops[2].G && c.Picked.Ops.Count == 2, $"a curved arrow is a line and a head picked as one (ops {layer.Ops.Count})");
        Check.That(layer.Ops[2].Pts()[0].DistanceTo(new Vector2(100, 150)) < 0.1f && layer.Ops[2].Fc != null, "its head is filled and its tip is the last bend");

        c.P.ShapeKind = Polyline.Area;
        c.P.ShapeFill = "fill";
        c.Picked.Drop();
        foreach (var knot in new[] { new Vector2(120, 20), new Vector2(180, 20), new Vector2(150, 70), new Vector2(120, 20) }) Click(c, knot);
        Check.That(layer.Ops.Count == 4 && layer.Ops[3] is { K: "shape", W: 0, Fc: not null }, $"a click on the first point closes the area, filled solid (ops {layer.Ops.Count})");

        c.P.ShapeKind = "arrow";
        Drag(c, new Vector2(20, 20), new Vector2(100, 20));
        Check.That(layer.Ops.Count == 6 && c.Picked.Handles().Length == 2, $"a straight arrow has a handle on its tail and its tip (ops {layer.Ops.Count}, handles {c.Picked.Handles().Length})");
        DragScreen(c, c.Picked.Handles()[1], c.ToScreen(new Vector2(100, 60)));
        Check.That(layer.Ops[5].Pts()[0].DistanceTo(new Vector2(100, 60)) < 0.1f && layer.Ops[4].Pts()[0].DistanceTo(new Vector2(20, 20)) < 0.1f, "the tip pulled away takes the head with it and the tail stays");
        Check.That(layer.Ops.Count == 6, "pulling an end drew nothing new");
        await r.Frames(2);
        c.QueueFree();
    }

    [NodeTest]
    static async Task A_shape_is_laid_where_the_pointer_is_let_go_and_mirrored_arrows_keep_heads_to_their_line(TestRunner r)
    {
        var c = await Canvas(r, Blank());
        var layer = c.Doc.Layers[0];
        c.P.ShapeKind = "rect";
        c.P.ShapeFill = "line";
        c.P.Line = 4;
        c.SetTool("shape");

        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = c.ToScreen(new Vector2(40, 40)) });
        c._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = c.ToScreen(new Vector2(100, 80)) });
        Check.That(layer.Ops.Count == 1 && Near(Span(layer.Ops[0]), 40, 40, 100, 80), $"let go far from the press with no move between, the rectangle still spans the two (ops {layer.Ops.Count})");

        c.Picked.Drop();
        string symmetry = c.P.Symmetry;
        c.P.Symmetry = "x";
        c.P.ShapeKind = "arrow";
        Drag(c, new Vector2(20, 150), new Vector2(80, 150));
        c.P.Symmetry = symmetry;
        Check.That(layer.Ops.Count == 5 && c.Picked.Ops.Count == 4 && c.Picked.Handles().Length == 8, $"an arrow drawn mirrored is two arrows in hand as one, in a box (ops {layer.Ops.Count}, picked {c.Picked.Ops.Count})");
        Check.That(Mathf.Abs(layer.Ops[3].Pts()[1].X - 62) < 0.1f && Mathf.Abs(layer.Ops[4].Pts()[1].X - 138) < 0.1f, $"each head sits on its own tip (at {layer.Ops[3].Pts()[1].X} and {layer.Ops[4].Pts()[1].X})");

        c.P.Line = 8;
        c.Picked.Restyle("Line width");
        Check.That(layer.Ops[1].W == 8 && layer.Ops[2].W == 8, "both lines take the new width");
        Check.That(Mathf.Abs(layer.Ops[3].Pts()[1].X - 53) < 0.1f && Mathf.Abs(layer.Ops[4].Pts()[1].X - 147) < 0.1f, $"and both heads grow to match (at {layer.Ops[3].Pts()[1].X} and {layer.Ops[4].Pts()[1].X})");
        Check.That(layer.Ops[3].Pts()[0].DistanceTo(new Vector2(80, 150)) < 0.1f && layer.Ops[4].Pts()[0].DistanceTo(new Vector2(120, 150)) < 0.1f, "with their tips where they were");
        await r.Frames(2);
        c.QueueFree();
    }
}
