using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SightingGridTests
{
    static GridDef Hexes(bool flat = false) => new() { Kind = "hex", Pitch = 10, FlatTop = flat };

    static PieceInstance Piece(string id, Vec2 at) => new() { Id = id, Def = "inf", Pos = at };

    static SightingDraft Gridded(GridDef g, int rings, params (string Id, Vec2 At)[] watchers)
    {
        var d = new SightingDraft { Format = SightingFormat.Grid, Grid = g, Center = watchers[0].At, Watchers = watchers.Select(w => w.Id).ToList() };
        d.SetRings(rings);
        d.Place(watchers);
        d.Look();
        return d;
    }

    static void Recompute(SightingDraft d, params PieceInstance[] pieces) => d.Recompute(shape => pieces.Where(p => shape.Contains(p.Pos)));

    [Fact]
    public void Rings_and_the_reach_are_one_value_so_the_scenario_keeps_either()
    {
        var d = new SightingDraft { Grid = Hexes(), Radius = 40 };
        Assert.Equal(4, d.Rings);
        d.SetRings(2);
        Assert.Equal(25, d.Radius, 9);
        Assert.Equal(2, d.Rings);
        d.SetRings(0);
        Assert.Equal(0, d.Rings);
        d.SetRings(GridCells.MostRings + 5);
        Assert.Equal(GridCells.MostRings, d.Rings);
        Assert.Equal(GridCells.MostRings, new SightingDraft { Grid = Hexes(), Radius = 1e6 }.Rings);
    }

    [Fact]
    public void Every_format_a_draft_has_is_one_the_settings_and_profiles_keep()
    {
        Assert.Equal(Enum.GetValues<SightingFormat>().Select(SightingDraft.NameOf), KTable.Client.Prefs.Settings.SightingFormats);
        Assert.Contains("grid", KTable.Client.Prefs.Settings.SightingFormats);
    }

    [Fact]
    public void Grid_is_a_picture_cut_to_the_cells_only_while_there_is_a_grid()
    {
        var d = new SightingDraft { Format = SightingFormat.Grid, Grid = Hexes() };
        Assert.True(d.Gridded && d.Outlined && !d.Drawn);
        d.Grid = new GridDef { Kind = GridDef.NoGrid, Pitch = 10 };
        Assert.False(d.Gridded || d.Outlined);
        Assert.Equal(SightingFormat.Grid, SightingDraft.FormatNamed("grid"));
        Assert.Equal("grid", SightingDraft.NameOf(SightingFormat.Grid));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Two_hexes_out_is_nineteen_hexes_and_takes_in_only_the_pieces_standing_in_them(bool flat)
    {
        var g = Hexes(flat);
        var eye = new Vec2(3, -2);
        var d = Gridded(g, 2, ("eye", eye));
        var home = GridCells.CellOf(g, eye);
        var near = Piece("near", GridCells.Centre(g, GridCells.Within(g, home, 2).First(c => GridCells.Steps(g, home, c) == 2)));
        var far = Piece("far", GridCells.Centre(g, GridCells.Within(g, home, 3).First(c => GridCells.Steps(g, home, c) == 3)));
        Recompute(d, near, far, Piece("eye", eye));
        Assert.True(d.Edged);
        Assert.Single(d.Loops);
        Assert.Equal(6 * 5, d.Polygon.Count);
        Assert.Equal(new[] { "eye", "near" }, d.Included.Order());
        foreach (var c in GridCells.Within(g, home, 4))
            Assert.Equal(GridCells.Steps(g, home, c) <= 2, d.Shape().Contains(GridCells.Centre(g, c)));
    }

    [Fact]
    public void The_cut_follows_the_cells_past_the_reach_circle_and_the_frame_holds_them_all()
    {
        var g = Hexes();
        var d = Gridded(g, 1, ("eye", new Vec2(0, 0)));
        Recompute(d);
        var shape = d.Shape();
        Assert.Equal(0, shape.Radius);
        Assert.All(d.Polygon, v => Assert.True(Math.Max(Math.Abs(v.X - d.Center.X), Math.Abs(v.Z - d.Center.Z)) <= d.Frame + 1e-9));
        Assert.True(d.Polygon.Max(v => (v - d.Center).Length) > d.Radius);
        Assert.All(d.Polygon, v => Assert.True((v - d.Center).Length <= d.SeenReach + 1e-9));
    }

    [Fact]
    public void Several_pieces_far_apart_see_two_patches_of_cells_and_nothing_between()
    {
        var g = Hexes();
        var d = Gridded(g, 1, ("a", new Vec2(-80, 0)), ("b", new Vec2(80, 0)));
        Recompute(d, Piece("mid", new Vec2(0, 0)), Piece("by-a", new Vec2(-70, 0)));
        Assert.Equal(2, d.Loops.Count);
        Assert.Equal(new[] { "by-a" }, d.Included);
        Assert.True(d.Shape().Contains(new Vec2(80, 0)));
        Assert.False(d.Shape().Contains(new Vec2(0, 0)));
    }

    [Fact]
    public void Lines_are_not_traced_in_a_grid_picture()
    {
        var d = Gridded(Hexes(), 2, ("eye", new Vec2(0, 0)));
        d.Lines.Add(new SightLine(SightLine.Block, new List<Vec2> { new(5, -20), new(5, 20) }));
        d.Look();
        Assert.True(d.EyesSee(new Vec2(15, 0)));
        Assert.False(d.BlursPastLines);
    }

    [Fact]
    public void Squares_count_their_corner_neighbours_as_one_step()
    {
        var g = new GridDef { Kind = "square", Pitch = 10, Subdivisions = 5 };
        var d = Gridded(g, 1, ("eye", new Vec2(5, 5)));
        Recompute(d);
        Assert.Equal(4, d.Polygon.Count);
        Assert.True(d.Shape().Contains(new Vec2(19, 19)));
        Assert.False(d.Shape().Contains(new Vec2(21, 5)));
    }

    [Fact]
    public void A_union_too_long_to_outline_is_held_to_fewer_rings()
    {
        var g = Hexes();
        var many = Enumerable.Range(0, 6).Select(i => ($"p{i}", new Vec2(i * 1000, 0))).ToArray();
        var d = Gridded(g, GridCells.MostRings, many);
        Recompute(d);
        Assert.True(d.Polygon.Count <= SightingDraft.MaxPoints);
        Assert.True(d.Rings < GridCells.MostRings);
        Assert.Equal(6, d.Loops.Count);
    }
}
