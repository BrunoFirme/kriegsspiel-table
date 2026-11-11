using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SightingDraftTests
{
    static SightingDraft Draft(bool square = false) => new() { Center = new Vec2(0, 0), Radius = 10, Format = square ? SightingFormat.Square : SightingFormat.Round };

    [Fact]
    public void A_point_traced_outside_the_picture_stops_at_its_edge()
    {
        Assert.Equal(new Vec2(10, 0), Draft().Clip(new Vec2(25, 0)));
        Assert.Equal(new Vec2(3, 4), Draft().Clip(new Vec2(3, 4)));
        Assert.Equal(new Vec2(10, -10), Draft(square: true).Clip(new Vec2(30, -12)));
    }

    [Fact]
    public void A_square_reaches_further_along_its_diagonal()
    {
        Assert.Equal(10, Draft().Reach(Math.PI / 4), 9);
        Assert.Equal(10 * Math.Sqrt(2), Draft(square: true).Reach(Math.PI / 4), 9);
        Assert.Equal(10, Draft(square: true).Reach(0), 9);
    }

    [Fact]
    public void A_wall_casts_a_shadow_and_what_is_behind_it_is_left_out()
    {
        var d = Draft();
        d.Walls.Add(new List<Vec2> { new(5, -2), new(5, 2) });
        var pieces = new[]
        {
            new PieceInstance { Id = "seen", Pos = new Vec2(0, 8) },
            new PieceInstance { Id = "behind", Pos = new Vec2(8, 0) },
            new PieceInstance { Id = "far", Pos = new Vec2(0, 20) },
        };
        d.Recompute(shape => pieces.Where(p => shape.Contains(p.Pos)));
        Assert.Equal(new[] { "seen" }, d.Included.Order());
        var east = d.Polygon.Where(p => Math.Abs(p.Z) < 0.5 && p.X > 0).ToList();
        Assert.NotEmpty(east);
        Assert.All(east, p => Assert.True(p.X <= 5.0001, $"the ray east stops at the wall, not at {p.X}"));
        Assert.Equal("polygon", d.Shape().Kind);
    }

    [Fact]
    public void However_many_corners_the_walls_have_the_lit_ground_fits_the_shader()
    {
        var d = Draft();
        var rng = new Random(1805);
        for (int w = 0; w < 40; w++)
            d.Walls.Add(Enumerable.Range(0, 20).Select(_ => new Vec2(rng.NextDouble() * 16 - 8, rng.NextDouble() * 16 - 8)).ToList());
        Assert.InRange(d.Visible().Count, 3, SightingDraft.MaxPoints);
    }

    [Fact]
    public void With_nothing_in_the_way_the_picture_is_its_plain_circle_or_square()
    {
        Assert.Empty(Draft().Visible());
        Assert.Equal(("circle", 10.0), (Draft().Shape().Kind, Draft().Shape().Radius));
        var square = Draft(square: true).Shape();
        Assert.Equal(("polygon", 4, 0.0), (square.Kind, square.Polygon!.Count, square.Radius));
    }

    [Fact]
    public void Inside_the_sharp_ring_nothing_blurs_and_the_blur_grows_ever_faster_to_its_most_at_the_edge()
    {
        var d = Draft();
        d.Sharp = 0.5;
        Assert.Equal(0, d.BlurAt(new Vec2(4, 0)));
        Assert.Equal(0, d.BlurAt(new Vec2(5, 0)));
        Assert.Equal(SightingDraft.BlurAtEdge * 10, d.BlurAt(new Vec2(10, 0)), 9);
        var steps = Enumerable.Range(0, 11).Select(i => d.BlurAt(new Vec2(5 + i * 0.5, 0))).ToList();
        for (int i = 1; i < steps.Count; i++) Assert.True(steps[i] > steps[i - 1]);
        Assert.True(steps[10] - steps[9] > 3 * (steps[2] - steps[1]), "the blur is gentle near the ring and hard near the edge");
        Assert.True(steps[5] < steps[10] * 0.15, $"half way out the blur is still light, not {steps[5] / steps[10]:0.00} of the edge's");
        for (int i = 2; i < steps.Count; i++)
            Assert.InRange(steps[i] / steps[i - 1], 1.25, 3.5);
        d.Blur = SightingDraft.MostBlur;
        Assert.Equal(SightingDraft.MostBlur * 10, d.BlurAt(new Vec2(10, 0)), 9);
        d.Sharp = 1;
        Assert.Equal(0, d.BlurAt(new Vec2(9.99, 0)));
    }

    [Fact]
    public void A_square_picture_measures_the_blur_as_a_square_does()
    {
        var d = Draft(square: true);
        d.Sharp = 0.5;
        Assert.Equal(d.BlurAt(new Vec2(8, 0)), d.BlurAt(new Vec2(8, 8)), 9);
        Assert.Equal(0, d.BlurAt(new Vec2(4, 4)));
    }

    static SightingDraft Drawn() => new() { Center = new Vec2(0, 0), Radius = 10, Format = SightingFormat.Drawn };

    static List<Vec2> Loop(double x0, double z0, double x1, double z1, int perSide = 8)
    {
        var corners = new[] { new Vec2(x0, z0), new Vec2(x1, z0), new Vec2(x1, z1), new Vec2(x0, z1) };
        var loop = new List<Vec2>();
        for (int c = 0; c < 4; c++)
        {
            Vec2 a = corners[c], b = corners[(c + 1) % 4];
            for (int i = 0; i < perSide; i++) loop.Add(new Vec2(a.X + (b.X - a.X) * i / perSide, a.Z + (b.Z - a.Z) * i / perSide));
        }
        return loop;
    }

    [Fact]
    public void A_drawn_outline_is_the_picture_and_what_lies_outside_it_is_left_out()
    {
        var d = Drawn();
        Assert.True(d.AddOutline(Loop(3, 3, 7, 7), 0.01));
        var pieces = new[]
        {
            new PieceInstance { Id = "inside", Pos = new Vec2(5, 5) },
            new PieceInstance { Id = "observer", Pos = new Vec2(0, 0) },
            new PieceInstance { Id = "beyond", Pos = new Vec2(-5, -5) },
        };
        d.Recompute(shape => pieces.Where(p => shape.Contains(p.Pos)));
        Assert.Equal(new[] { "inside" }, d.Included.Order());
        Assert.True(d.Edged);
        Assert.Equal(("polygon", 0.0), (d.Shape().Kind, d.Shape().Radius));
    }

    [Fact]
    public void A_small_loop_drawn_far_zoomed_out_still_closes()
    {
        var d = Drawn();
        Assert.True(d.AddOutline(Loop(1, 1, 3, 2), 300));
        Assert.InRange(d.Outlines[^1].Count, 4, SightingDraft.MaxPoints);
    }

    [Fact]
    public void A_drawn_outline_stops_at_the_square_frame()
    {
        var d = Drawn();
        Assert.True(d.AddOutline(Loop(0, -2, 25, 2), 0.01));
        d.Recompute(_ => Enumerable.Empty<PieceInstance>());
        Assert.Equal(10, d.Polygon.Max(p => p.X), 9);
    }

    [Fact]
    public void A_click_or_a_straight_stroke_draws_no_outline()
    {
        var d = Drawn();
        Assert.False(d.AddOutline(new List<Vec2> { new(1, 1), new(1.001, 1) }, 0.01));
        Assert.False(d.AddOutline(new List<Vec2> { new(0, 0), new(2, 0), new(4, 0), new(6, 0) }, 0.01));
        Assert.Empty(d.Outlines);
        d.Recompute(_ => Enumerable.Empty<PieceInstance>());
        Assert.False(d.Edged);
        Assert.Equal(("polygon", 4), (d.Shape().Kind, d.Shape().Polygon.Count));
    }

    [Fact]
    public void However_long_the_stroke_the_outline_fits_the_shader()
    {
        var d = Drawn();
        var rng = new Random(1866);
        var wobbly = Enumerable.Range(0, 6000).Select(i =>
        {
            double a = i * Math.Tau / 6000, r = 6 + rng.NextDouble() * 2;
            return new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
        }).ToList();
        Assert.True(d.AddOutline(wobbly, 1e-6));
        Assert.InRange(d.Outlines[^1].Count, 3, SightingDraft.MaxPoints);
    }

    [Fact]
    public void Outlines_and_traced_walls_are_kept_apart_when_the_format_changes()
    {
        var d = Draft();
        d.Walls.Add(new List<Vec2> { new(5, -2), new(5, 2) });
        d.Format = SightingFormat.Drawn;
        Assert.Empty(d.Strokes);
        Assert.True(d.AddOutline(Loop(-2, -2, 2, 2), 0.01));
        d.Format = SightingFormat.Round;
        Assert.Single(d.Strokes);
        Assert.Same(d.Walls, d.Strokes);
        d.Format = SightingFormat.Drawn;
        Assert.Same(d.Outlines, d.Strokes);
    }

    [Fact]
    public void A_drawn_picture_blurs_in_from_its_own_edge()
    {
        var d = Drawn();
        Assert.True(d.AddOutline(Loop(-4, -4, 4, 4), 0.01));
        d.Recompute(_ => Enumerable.Empty<PieceInstance>());
        Assert.Equal(4, d.Depth, 1);
        Assert.Equal(d.Depth, d.BlurReach);
        d.Sharp = 0.5;
        Assert.Equal(0, d.BlurAt(new Vec2(0, 0)));
        Assert.True(d.BlurAt(new Vec2(3.9, 0)) > 0);
        Assert.Equal(d.BlurAt(new Vec2(3.9, 0)), d.BlurAt(new Vec2(0, -3.9)), 6);
        Assert.True(d.BlurAt(new Vec2(3.9, 0)) > d.BlurAt(new Vec2(3, 0)));
    }
}
