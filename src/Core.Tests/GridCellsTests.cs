using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GridCellsTests
{
    const double Root3 = 1.7320508075688772;

    static GridDef Hex(bool flat = false) => new() { Kind = "hex", Pitch = 10, FlatTop = flat };

    static GridDef Square() => new() { Kind = "square", Pitch = 10, Subdivisions = 4 };

    public static IEnumerable<object[]> Grids() => new[] { new object[] { "hex", false }, new object[] { "hex", true }, new object[] { "square", false } };

    static GridDef Grid(string kind, bool flat) => kind == "hex" ? Hex(flat) : Square();

    static bool Inside(List<Vec2> ring, Vec2 p) => new SightingShape { Kind = "polygon", Polygon = ring }.Contains(p);

    [Theory]
    [MemberData(nameof(Grids))]
    public void The_cell_of_a_point_holds_it_and_its_centre_is_the_one_a_piece_snaps_to(string kind, bool flat)
    {
        var g = Grid(kind, flat);
        var rng = new Random(11);
        for (int i = 0; i < 3000; i++)
        {
            var p = new Vec2(rng.NextDouble() * 400 - 200, rng.NextDouble() * 400 - 200);
            var cell = GridCells.CellOf(g, p);
            Assert.True(Inside(GridCells.Corners(g, cell), p), $"{p} is not inside its cell {cell}");
            if (kind == "hex") Assert.True((GridSnap.Nearest(g, p) - GridCells.Centre(g, cell)).Length < 1e-9);
            Assert.Equal(cell, GridCells.CellOf(g, GridCells.Centre(g, cell)));
        }
    }

    [Fact]
    public void Pointed_hexes_sit_in_rows_and_flat_ones_in_columns()
    {
        var pointed = Hex();
        Assert.Equal(new Vec2(10, 0), GridCells.Centre(pointed, GridCells.CellOf(pointed, new Vec2(9, 1))));
        var home = GridCells.CellOf(pointed, new Vec2(0, 0));
        Assert.Equal(home, GridCells.CellOf(pointed, new Vec2(4.9, 0)));
        Assert.NotEqual(home, GridCells.CellOf(pointed, new Vec2(5.1, 0)));
        Assert.Equal(home, GridCells.CellOf(pointed, new Vec2(0, 5.6)));
        var flat = Hex(flat: true);
        Assert.Equal(new Vec2(0, 10), GridCells.Centre(flat, GridCells.CellOf(flat, new Vec2(1, 9))));
        home = GridCells.CellOf(flat, new Vec2(0, 0));
        Assert.Equal(home, GridCells.CellOf(flat, new Vec2(0, 4.9)));
        Assert.NotEqual(home, GridCells.CellOf(flat, new Vec2(0, 5.1)));
        Assert.Equal(home, GridCells.CellOf(flat, new Vec2(5.6, 0)));
        var top = GridCells.Corners(flat, new GridCell(0, 0));
        Assert.Equal(2, top.Count(c => Math.Abs(c.Z - 5) < 1e-9));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Rings_hold_one_seven_nineteen_hexes_or_odd_squares_and_every_cell_is_that_many_steps_or_fewer(string kind, bool flat)
    {
        var g = Grid(kind, flat);
        var middle = GridCells.CellOf(g, new Vec2(33, -71));
        for (int n = 0; n <= 4; n++)
        {
            var cells = GridCells.Within(g, middle, n).ToList();
            Assert.Equal(GridCells.Count(g, n), cells.Distinct().Count());
            Assert.Equal(kind == "hex" ? 3 * n * (n + 1) + 1 : (2 * n + 1) * (2 * n + 1), cells.Count);
            Assert.All(cells, c => Assert.True(GridCells.Steps(g, middle, c) <= n));
            Assert.Contains(cells, c => GridCells.Steps(g, middle, c) == n);
        }
    }

    [Fact]
    public void A_hex_is_one_step_from_its_six_neighbours_and_they_are_one_across_apart()
    {
        var g = Hex();
        var middle = new GridCell(3, -2);
        var near = GridCells.Within(g, middle, 1).Where(c => c != middle).ToList();
        Assert.Equal(6, near.Count);
        Assert.All(near, c => Assert.Equal(10, (GridCells.Centre(g, c) - GridCells.Centre(g, middle)).Length, 9));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void The_outline_of_a_ring_is_one_loop_round_exactly_its_cells(string kind, bool flat)
    {
        var g = Grid(kind, flat);
        var middle = GridCells.CellOf(g, new Vec2(-12, 40));
        for (int n = 0; n <= 5; n++)
        {
            var cells = GridCells.Within(g, middle, n).ToHashSet();
            var loops = GridCells.Outline(g, cells);
            Assert.Single(loops);
            Assert.Equal(kind == "hex" ? 6 * (2 * n + 1) : 4, loops[0].Count);
            foreach (var c in GridCells.Within(g, middle, n + 2))
                Assert.Equal(cells.Contains(c), Inside(loops[0], GridCells.Centre(g, c)));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_corner_of_a_hex_outline_is_where_three_cells_meet(bool flat)
    {
        var g = Hex(flat);
        var loop = GridCells.Outline(g, GridCells.Within(g, new GridCell(0, 0), 3)).Single();
        foreach (var corner in loop)
        {
            var round = GridCells.Within(g, GridCells.CellOf(g, corner), 2).Select(c => (GridCells.Centre(g, c) - corner).Length).OrderBy(d => d).ToList();
            Assert.Equal(10 / Root3, round[0], 6);
            Assert.Equal(10 / Root3, round[2], 6);
            Assert.True(round[3] > 10 / Root3 + 1);
        }
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Two_groups_apart_make_two_loops_and_their_joined_ring_holds_both_and_nothing_between(string kind, bool flat)
    {
        var g = Grid(kind, flat);
        var left = GridCells.CellOf(g, new Vec2(-60, 0));
        var right = GridCells.CellOf(g, new Vec2(60, 0));
        var cells = GridCells.Within(g, left, 2).Concat(GridCells.Within(g, right, 1)).ToHashSet();
        var loops = GridCells.Outline(g, cells);
        Assert.Equal(2, loops.Count);
        var ring = GridCells.Joined(loops);
        Assert.Equal(GridCells.JoinedCount(loops), ring.Count);
        foreach (var c in GridCells.Within(g, GridCells.CellOf(g, new Vec2(0, 0)), 8))
            Assert.Equal(cells.Contains(c), Inside(ring, GridCells.Centre(g, c)));
    }

    [Fact]
    public void Squares_touching_at_a_corner_still_outline_exactly_their_cells()
    {
        var g = Square();
        var cells = new HashSet<GridCell> { new(0, 0), new(1, 1), new(2, 0), new(5, 5) };
        var ring = GridCells.Joined(GridCells.Outline(g, cells));
        for (int a = -2; a <= 7; a++)
            for (int b = -2; b <= 7; b++)
                Assert.Equal(cells.Contains(new GridCell(a, b)), Inside(ring, GridCells.Centre(g, new GridCell(a, b))));
    }

    [Theory]
    [MemberData(nameof(Grids))]
    public void Farthest_reaches_every_corner_from_anywhere_in_the_middle_cell(string kind, bool flat)
    {
        var g = Grid(kind, flat);
        var rng = new Random(5);
        for (int n = 0; n <= 4; n++)
            for (int i = 0; i < 50; i++)
            {
                var eye = new Vec2(rng.NextDouble() * 40, rng.NextDouble() * 40);
                var corners = GridCells.Within(g, GridCells.CellOf(g, eye), n).SelectMany(c => GridCells.Corners(g, c));
                Assert.True(corners.Max(c => (c - eye).Length) <= GridCells.Farthest(g, n) + 1e-9);
            }
    }
}
