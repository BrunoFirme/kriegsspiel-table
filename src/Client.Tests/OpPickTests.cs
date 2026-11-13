using System.Numerics;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class OpPickTests
{
    static PaintOp Line(float x0, float y0, float x1, float y1, string? group = null) => new() { K = "path", P = new() { x0, y0, x1, y1 }, W = 4, G = group };
    static PaintOp Box(string? fill = null, float line = 4) => new() { K = "shape", P = new() { 10, 10, 50, 10, 50, 40, 10, 40 }, W = line, Fc = fill };

    [Fact]
    public void Shapes_lines_and_grouped_curves_can_be_picked_but_not_freehand_fills_or_rubbing()
    {
        Assert.True(OpPick.Pickable(Box()));
        Assert.True(OpPick.Pickable(Line(0, 0, 10, 0)));
        var freehand = new PaintOp { K = "path", P = new() { 0, 0, 5, 3, 10, 0, 15, 4 } };
        Assert.False(OpPick.Pickable(freehand));
        freehand.G = "g1";
        Assert.True(OpPick.Pickable(freehand));
        Assert.False(OpPick.Pickable(new PaintOp { K = "path", P = new() { 0, 0, 10, 0 }, E = true }));
        Assert.False(OpPick.Pickable(new PaintOp { K = "fill", P = new() { 8, 8 }, M = "mask.png" }));
    }

    [Fact]
    public void A_line_is_hit_within_its_width_and_an_outline_only_on_its_line_while_a_filled_shape_is_hit_inside()
    {
        Assert.True(OpPick.Hit(Line(0, 0, 100, 0), new Vector2(50, 2), 0));
        Assert.False(OpPick.Hit(Line(0, 0, 100, 0), new Vector2(50, 3), 0));
        Assert.True(OpPick.Hit(Line(0, 0, 100, 0), new Vector2(50, 5), 4));
        Assert.True(OpPick.Hit(Box(), new Vector2(30, 11), 0));
        Assert.True(OpPick.Hit(Box(), new Vector2(10, 25), 0));
        Assert.False(OpPick.Hit(Box(), new Vector2(30, 25), 0));
        Assert.True(OpPick.Hit(Box(fill: "#ff0000ff", line: 0), new Vector2(30, 25), 0));
        Assert.False(OpPick.Hit(Box(fill: "#ff0000ff", line: 0), new Vector2(60, 25), 0));
    }

    [Fact]
    public void What_one_drag_drew_is_picked_together()
    {
        var ops = new List<PaintOp> { Line(0, 0, 10, 0), Line(0, 5, 10, 5, "a"), Box(), Line(0, 9, 10, 9, "a"), new() { K = "path", P = new() { 0, 0, 9, 9 }, G = "a", E = true } };
        Assert.Equal(new[] { 0 }, OpPick.Mates(ops, 0));
        Assert.Equal(new[] { 1, 3 }, OpPick.Mates(ops, 3));
        Assert.Equal(new[] { 2 }, OpPick.Mates(ops, 2));
    }

    [Fact]
    public void The_shapes_on_a_layer_are_listed_whole_named_for_their_kind_and_change_places_whole()
    {
        var head = new PaintOp { K = "shape", P = new() { 9, 0, 7, 1, 7, -1 }, W = 0, Fc = "#112233ff", G = "b", S = "arrow" };
        var ops = new List<PaintOp>
        {
            new() { K = "shape", P = new() { 0, 0, 4, 0, 4, 4 }, W = 0, Fc = "#ff0000ff", C = "#00ff00ff", S = "rect" },
            new() { K = "path", P = new() { 0, 0, 5, 3, 9, 0, 12, 4 } },
            new() { K = "path", P = new() { 0, 0, 7, 0 }, C = "#445566ff", G = "b", S = "arrow" }, head,
            Line(0, 9, 9, 9),
            new() { K = "shape", P = new() { 0, 0, 4, 0, 4, 4 }, W = 2 },
        };
        var listed = OpPick.Objects(ops);
        Assert.Equal(new[] { "0", "2 3", "4", "5" }, listed.Select(o => string.Join(" ", o)));
        Assert.Equal(new[] { "Rectangle", "Arrow", "Line", "Shape" }, listed.Select(o => OpPick.Named(o.Select(i => ops[i]).ToList())));
        Assert.Equal(new[] { "#ff0000ff", "#445566ff" }, listed.Take(2).Select(o => OpPick.Tint(o.Select(i => ops[i]).ToList())));
        Assert.Equal(1, OpPick.Freehand(ops));
        Assert.Equal("Curved arrow", ArtShapes.Name(Polyline.CurvedArrow));

        Assert.Null(OpPick.Restack(ops, new[] { 5 }, forward: true));
        Assert.Null(OpPick.Restack(ops, new[] { 0 }, forward: false));
        Assert.Equal(new[] { 2 }, OpPick.Restack(ops, new[] { 4 }, forward: false));
        Assert.Same(head, ops[4]);
        Assert.Equal(new[] { 2, 3 }, OpPick.Restack(ops, new[] { 3, 4 }, forward: false));
        Assert.Equal(new[] { 1, 2 }, OpPick.Restack(ops, new[] { 2, 3 }, forward: false));
        Assert.Equal(new[] { 2, 3 }, OpPick.Restack(ops, new[] { 1, 2 }, forward: true));
        Assert.Equal(new[] { 3, 4 }, OpPick.Restack(ops, new[] { 2, 3 }, forward: true));
        Assert.Same(head, ops[4]);
    }

    [Fact]
    public void An_arrow_pulled_by_an_end_keeps_its_head_on_the_tip_and_a_line_takes_the_new_end()
    {
        Vector2 a = new(10, 10), b = new(110, 10);
        var arrow = new List<PaintOp>
        {
            new() { K = "path", P = PaintOp.Flat(new[] { a, ArtShapes.ArrowShaftEnd(a, b, 4) }), W = 4, G = "g" },
            new() { K = "shape", P = PaintOp.Flat(ArtShapes.ArrowHead(a, b, 4)), W = 0, Fc = "#000000ff", G = "g" },
        };
        Assert.True(OpPick.IsArrow(arrow));
        Assert.Equal((a, b), OpPick.Ends(arrow));
        OpPick.SetEnds(arrow, a, new Vector2(10, 90));
        Assert.Equal((a, new Vector2(10, 90)), OpPick.Ends(arrow));
        Assert.Equal(10f, arrow[0].P[2]);
        Assert.InRange(arrow[0].P[3], 60, 89);
        Assert.Equal(6, arrow[1].P.Count);

        var line = new List<PaintOp> { Line(0, 0, 10, 0) };
        OpPick.SetEnds(line, new Vector2(3, 4), new Vector2(10, 0));
        Assert.Equal(new float[] { 3, 4, 10, 0 }, line[0].P);
        Assert.Null(OpPick.Ends(new List<PaintOp> { Box() }));
    }

    [Fact]
    public void Mirrored_arrows_given_a_wider_line_each_take_a_head_to_match_and_other_shapes_stay_as_drawn()
    {
        static PaintOp Shaft(Vector2 a, Vector2 b) => new() { K = "path", P = PaintOp.Flat(new[] { a, ArtShapes.ArrowShaftEnd(a, b, 4) }), W = 4, G = "g" };
        static PaintOp Head(Vector2 a, Vector2 b) => new() { K = "shape", P = PaintOp.Flat(ArtShapes.ArrowHead(a, b, 4)), W = 0, Fc = "#000000ff", G = "g" };
        Vector2 tail = new(10, 10), tip = new(110, 10), mirrorTail = new(390, 10), mirrorTip = new(290, 10);
        var pair = new List<PaintOp> { Shaft(tail, tip), Shaft(mirrorTail, mirrorTip), Head(tail, tip), Head(mirrorTail, mirrorTip) };
        Assert.Equal(92f, pair[2].P[2], 3);

        pair[0].W = pair[1].W = 8;
        OpPick.RefitHeads(pair);
        Assert.Equal(new float[] { 110, 10 }, pair[2].P.Take(2));
        Assert.Equal(new float[] { 290, 10 }, pair[3].P.Take(2));
        Assert.Equal(74f, pair[2].P[2], 3);
        Assert.Equal(326f, pair[3].P[2], 3);
        Assert.Equal(81.2f, pair[0].P[2], 3);
        Assert.Equal(318.8f, pair[1].P[2], 3);

        var boxes = new List<PaintOp> { Box(), Box() };
        var drawn = boxes[0].P.ToList();
        OpPick.RefitHeads(boxes);
        Assert.Equal(drawn, boxes[0].P);
    }

    [Fact]
    public void A_curve_passes_through_its_bends_and_a_curved_arrow_ends_in_a_head_on_the_last_one()
    {
        var knots = new List<Vector2> { new(0, 0), new(40, 30), new(80, 0) };
        var curve = ArtShapes.Smooth(knots, closed: false);
        Assert.True(curve.Count > knots.Count * 4);
        foreach (var k in knots) Assert.Contains(curve, p => (p - k).Length() < 0.01f);

        var ring = ArtShapes.Smooth(knots, closed: true);
        Assert.True((ring[0] - ring[^1]).Length() > 0.5f);

        var (body, head) = ArtShapes.CurvedArrow(knots, 4);
        Assert.Equal(3, head.Count);
        Assert.Equal(knots[^1], head[0]);
        Assert.True((body[^1] - knots[^1]).Length() > 4);
        Assert.Equal(knots[0], body[0]);
    }

    [Fact]
    public void The_shape_kinds_say_which_are_clicked_out_which_close_and_which_take_no_fill()
    {
        Assert.True(ArtShapes.IsPath("poly") && ArtShapes.IsPath(Polyline.Curve) && ArtShapes.IsPath(Polyline.CurvedArrow) && ArtShapes.IsPath(Polyline.Area));
        Assert.False(ArtShapes.IsPath("rect") || ArtShapes.IsPath("line"));
        Assert.True(ArtShapes.Closes("poly") && ArtShapes.Closes(Polyline.Area) && !ArtShapes.Closes(Polyline.Curve));
        Assert.True(ArtShapes.Open("line") && ArtShapes.Open("arrow") && ArtShapes.Open(Polyline.Curve) && ArtShapes.Open(Polyline.CurvedArrow));
        Assert.False(ArtShapes.Open("rect") || ArtShapes.Open(Polyline.Area) || ArtShapes.Open("star"));
    }

    [Fact]
    public void A_step_amended_replaces_the_last_one_so_a_slider_dragged_is_one_undo()
    {
        var h = new UndoHistory();
        h.Start("a");
        Assert.True(h.Amend("b"));
        Assert.True(h.Amend("c"));
        Assert.False(h.Amend("c"));
        Assert.Equal("a", h.Undo());
        Assert.False(h.CanUndo);
        Assert.Equal("c", h.Redo());
        Assert.True(h.Record("d"));
        Assert.True(h.Amend("e"));
        Assert.Equal("c", h.Undo());
    }

    [Fact]
    public void An_op_keeps_its_group_and_turn_when_copied_and_saved()
    {
        var doc = new ArtDoc();
        doc.Layers.Add(new ArtLayer { Ops = { new PaintOp { K = "shape", P = new() { 0, 0, 4, 0, 4, 4 }, G = "g7", R = 30 }, Line(0, 0, 1, 1) } });
        var back = ArtJson.Deserialize(ArtJson.Serialize(doc))!;
        Assert.Equal("g7", back.Layers[0].Ops[0].Clone().G);
        Assert.Equal(30f, back.Layers[0].Ops[0].Clone().R);
        Assert.Null(back.Layers[0].Ops[1].G);
        Assert.DoesNotContain("\"R\":", ArtJson.Serialize(new ArtDoc { Layers = { new ArtLayer { Ops = { Line(0, 0, 1, 1) } } } }));
    }
}
