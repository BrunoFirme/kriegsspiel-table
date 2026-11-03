using KTable.Client.Agent;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class FormationsTests
{
    [Fact]
    public void A_line_spreads_across_the_centre_and_turns_with_the_facing()
    {
        var line = Formations.Positions(3, "line", new Vec2(0, 0), 0, 2, 1);
        Assert.Equal(new[] { -2.0, 0.0, 2.0 }, line.Select(p => Math.Round(p.X, 6)));
        Assert.All(line, p => Assert.Equal(0, p.Z, 6));
        var turned = Formations.Positions(3, "line", new Vec2(10, 10), 90, 2, 1);
        Assert.All(turned, p => Assert.Equal(10, p.X, 6));
        Assert.Equal(new[] { 8.0, 10.0, 12.0 }, turned.Select(p => Math.Round(p.Z, 6)));
    }

    [Fact]
    public void Two_lines_split_the_pieces_front_and_back_and_a_grid_is_nearly_square()
    {
        var two = Formations.Positions(5, "two-lines", new Vec2(0, 0), 0, 2, 1);
        Assert.Equal(3, two.Count(p => p.Z < 0));
        Assert.Equal(2, two.Count(p => p.Z > 0));
        var grid = Formations.Positions(9, "grid", new Vec2(0, 0), 0, 1, 1);
        Assert.Equal(3, grid.Select(p => Math.Round(p.X, 6)).Distinct().Count());
        Assert.Equal(3, grid.Select(p => Math.Round(p.Z, 6)).Distinct().Count());
    }

    [Fact]
    public void A_column_runs_front_to_back_and_a_wedge_opens_behind_its_point()
    {
        var col = Formations.Positions(4, "column", new Vec2(0, 0), 0, 2, 1.5);
        Assert.All(col, p => Assert.Equal(0, p.X, 6));
        Assert.Equal(4.5, col.Max(p => p.Z) - col.Min(p => p.Z), 6);
        var wedge = Formations.Positions(5, "wedge", new Vec2(0, 0), 0, 1, 1);
        Assert.Equal((0.0, 0.0), (wedge[0].X, wedge[0].Z));
        Assert.True(wedge.Skip(1).All(p => p.Z > 0));
    }
}
