using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SightingDraftTests
{
    static SightingDraft Draft(bool square = false) => new() { Center = new Vec2(0, 0), Radius = 10, Square = square };

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
}
