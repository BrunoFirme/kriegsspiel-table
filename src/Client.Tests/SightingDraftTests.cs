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
}
