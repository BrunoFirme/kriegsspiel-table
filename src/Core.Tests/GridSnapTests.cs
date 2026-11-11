using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class GridSnapTests
{
    const double Root3 = 1.7320508075688772;

    static void Near(Vec2 want, Vec2 got) => Assert.True((want - got).Length < 1e-9, $"{want} != {got}");

    [Fact]
    public void Squares_snap_to_the_centre_of_the_cell_the_point_is_in_and_divided_squares_to_the_smaller_cell()
    {
        var g = new GridDef { Kind = "square", Pitch = 10, Snap = true };
        Near(new Vec2(15, 25), GridSnap.Nearest(g, new Vec2(13.2, 27.9)));
        Near(new Vec2(-5, -15), GridSnap.Nearest(g, new Vec2(-0.1, -19.9)));
        g.Subdivisions = 5;
        Near(new Vec2(13, 27), GridSnap.Nearest(g, new Vec2(13.2, 27.9)));
    }

    [Fact]
    public void Hexes_snap_to_the_nearest_centre_of_the_lattice_the_grid_draws_flats_left_and_right()
    {
        var g = new GridDef { Kind = "hex", Pitch = 10, Snap = true };
        Near(new Vec2(0, 0), GridSnap.Nearest(g, new Vec2(3, 4)));
        Near(new Vec2(5, 5 * Root3), GridSnap.Nearest(g, new Vec2(4, 7)));
        Near(new Vec2(10, 0), GridSnap.Nearest(g, new Vec2(9, -1)));
        Near(new Vec2(-5, -5 * Root3), GridSnap.Nearest(g, new Vec2(-5.5, -9)));
    }

    [Fact]
    public void Every_point_snaps_to_a_centre_no_farther_than_the_hex_corner_and_no_other_centre_is_nearer()
    {
        var g = new GridDef { Kind = "hex", Pitch = 4, Snap = true };
        var rng = new Random(7);
        for (int i = 0; i < 2000; i++)
        {
            var p = new Vec2(rng.NextDouble() * 200 - 100, rng.NextDouble() * 200 - 100);
            var c = GridSnap.Nearest(g, p);
            Assert.True((p - c).Length <= 4 / Root3 + 1e-9);
            foreach (var step in new[] { new Vec2(4, 0), new Vec2(-4, 0), new Vec2(2, 2 * Root3), new Vec2(-2, 2 * Root3), new Vec2(2, -2 * Root3), new Vec2(-2, -2 * Root3) })
                Assert.True((p - c).Length <= (p - (c + step)).Length + 1e-9);
        }
    }

    [Fact]
    public void Only_a_drawn_grid_with_snap_ticked_snaps()
    {
        Assert.False(GridSnap.Snaps(null));
        Assert.False(GridSnap.Snaps(new GridDef { Kind = "square" }));
        Assert.False(GridSnap.Snaps(new GridDef { Kind = "none", Snap = true }));
        Assert.True(GridSnap.Snaps(new GridDef { Kind = "square", Snap = true }));
        Assert.True(GridSnap.Snaps(new GridDef { Kind = "hex", Snap = true }));
    }

    [Fact]
    public void Snap_is_kept_and_undone_with_the_grid_and_a_grid_without_it_is_written_as_before()
    {
        Assert.DoesNotContain("snap", Json.Serialize(new GridDef { Kind = "square" }), StringComparison.OrdinalIgnoreCase);
        Assert.True(Json.Deserialize<GridDef>(Json.Serialize(new GridDef { Kind = "hex", Snap = true }))!.Snap);
        var g = new GameState { Grid = new GridDef { Kind = "square", Pitch = 10 } };
        Assert.Null(StateMachine.Apply(g, new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 10, Snap = true } }, out var inverse));
        Assert.True(g.Grid.Snap);
        StateMachine.Apply(g, inverse!, out _);
        Assert.False(g.Grid.Snap);
    }
}
