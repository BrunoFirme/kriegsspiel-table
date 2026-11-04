using KTable.Core.Model;

namespace KTable.Client.Tests;

public class PolylineTests
{
    [Fact]
    public void Rubbing_through_the_middle_of_a_line_leaves_its_two_ends_cut_at_the_rubber_s_edge()
    {
        var line = new List<Vec2> { new(-10, 0), new(10, 0) };
        Assert.True(Polyline.Rubs(line, new Vec2(0, 1), 2));
        Assert.False(Polyline.Rubs(line, new Vec2(0, 5), 2));
        var pieces = Polyline.Cut(line, new Vec2(0, 0), 2);
        Assert.Equal(2, pieces.Count);
        Assert.Equal(new Vec2(-10, 0), pieces[0][0]);
        Assert.Equal(-2, pieces[0][^1].X, 9);
        Assert.Equal(2, pieces[1][0].X, 9);
        Assert.Equal(new Vec2(10, 0), pieces[1][^1]);
        Assert.Empty(Polyline.Cut(line, new Vec2(0, 0), 20));
    }

    [Fact]
    public void A_traced_line_is_thinned_to_its_corners()
    {
        var wobbly = Enumerable.Range(0, 50).Select(i => new Vec2(i, i % 2 * 0.01)).Append(new Vec2(49, 30)).ToList();
        Assert.Equal(new[] { new Vec2(0, 0), new Vec2(49, 0.01), new Vec2(49, 30) }, Polyline.Thin(wobbly, 0.1));
        Assert.Equal(5, Polyline.ToSegment(new Vec2(3, 5), new Vec2(0, 0), new Vec2(10, 0)), 9);
        Assert.Equal(5, Polyline.ToSegment(new Vec2(-3, 4), new Vec2(0, 0), new Vec2(10, 0)), 9);
    }

    [Fact]
    public void A_pen_stroke_drops_the_points_a_hand_at_rest_sends()
    {
        var pts = new List<Vec2> { new(0, 0), new(0.01, 0), new(0.02, 0), new(1, 0), new(1.01, 0) };
        Assert.Equal(new[] { new Vec2(0, 0), new Vec2(1, 0) }, Polyline.Simplify(pts));
        Assert.Equal(2, Polyline.Simplify(new List<Vec2> { new(0, 0), new(0.001, 0) }).Count);
    }

    [Fact]
    public void Shapes_are_polylines_and_an_arrow_has_its_head_apart()
    {
        var a = new Vec2(0, 0);
        var b = new Vec2(4, 2);
        var rect = Polyline.ShapePoints("rect", a, b);
        Assert.Equal(5, rect.Count);
        Assert.Equal(rect[0], rect[^1]);
        var ellipse = Polyline.ShapePoints("ellipse", a, b);
        Assert.Equal(57, ellipse.Count);
        Assert.All(ellipse, p => Assert.Equal(1, Math.Pow((p.X - 2) / 2, 2) + Math.Pow((p.Z - 1) / 1, 2), 6));
        Assert.Equal(new[] { a, b }, Polyline.ShapePoints("arrow", a, b));
        var head = Polyline.ShapeHead("arrow", a, b)!;
        Assert.Equal(3, head.Count);
        Assert.Equal(b, head[1]);
        Assert.Null(Polyline.ShapeHead("arrow", a, a));
        Assert.Null(Polyline.ShapeHead("rect", a, b));
    }

    [Fact]
    public void A_rectangle_and_an_ellipse_lie_along_the_views_axes_when_the_view_is_turned()
    {
        var a = new Vec2(0, 0);
        var b = new Vec2(4, 2);
        Assert.Equal(Polyline.ShapePoints("rect", a, b), Polyline.ShapePoints("rect", a, b, new Vec2(3, 0)));
        var across = new Vec2(1, 1);
        var u = new Vec2(Math.Sqrt(0.5), Math.Sqrt(0.5));
        var v = new Vec2(-u.Z, u.X);
        static double Dot(Vec2 p, Vec2 q) => p.X * q.X + p.Z * q.Z;
        var rect = Polyline.ShapePoints("rect", a, b, across);
        Assert.Equal(new[] { a, b, a }, new[] { rect[0], rect[2], rect[4] });
        Assert.Equal(0, Dot(rect[1] - a, v), 9);
        Assert.Equal(0, Dot(rect[3] - a, u), 9);
        Assert.Equal(0, Dot(rect[1] - rect[2], u), 9);
        var c = new Vec2(2, 1);
        double ru = Dot(b - a, u) / 2, rv = Dot(b - a, v) / 2;
        Assert.All(Polyline.ShapePoints("ellipse", a, b, across), p => Assert.Equal(1, Math.Pow(Dot(p - c, u) / ru, 2) + Math.Pow(Dot(p - c, v) / rv, 2), 6));
    }
}
