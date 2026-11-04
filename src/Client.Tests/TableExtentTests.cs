using KTable.Client;
using KTable.Core.Model;
using Xunit;

namespace KTable.Client.Tests;

public sealed class TableExtentTests
{
    [Fact]
    public void An_empty_table_starts_big_around_the_middle()
    {
        var a = TableExtent.Of(new GameState());
        Assert.Equal((0.0, 0.0), (a.Centre.X, a.Centre.Z));
        Assert.Equal((TableExtent.LeastSide, TableExtent.LeastSide), (a.Size.W, a.Size.H));
    }

    [Fact]
    public void The_table_grows_to_hold_everything_on_it_with_room_to_spare()
    {
        var g = new GameState();
        g.Sheets.Add(new SheetDef { Pos = new Vec2(1000, 0), Size = new Size2(400, 300) });
        g.Layers[Layers.Umpire] = new List<PieceInstance> { new() { Id = "p-1", Pos = new Vec2(-900, 50) } };
        g.Drawings[Layers.Umpire] = new List<Stroke> { new() { Points = { new Vec2(0, 700), new Vec2(10, 710) } } };
        var a = TableExtent.Of(g);
        double half = a.Size.W / 2, deep = a.Size.H / 2;
        foreach (var p in new[] { new Vec2(1250, 0), new Vec2(-900, 50), new Vec2(10, 710) })
        {
            Assert.InRange(p.X, a.Centre.X - half + TableExtent.LeastMargin, a.Centre.X + half - TableExtent.LeastMargin);
            Assert.InRange(p.Z, a.Centre.Z - deep + TableExtent.LeastMargin, a.Centre.Z + deep - TableExtent.LeastMargin);
        }
    }

    [Fact]
    public void A_table_holds_a_smaller_one_inside_it_and_not_one_that_reaches_past_its_edge()
    {
        var big = new TableArea(new Vec2(0, 0), new Size2(600, 600));
        Assert.True(big.Holds(new TableArea(new Vec2(100, 0), new Size2(300, 300))));
        Assert.False(big.Holds(new TableArea(new Vec2(200, 0), new Size2(300, 300))));
    }
}
