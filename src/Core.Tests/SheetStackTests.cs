using KTable.Core.Model;
using Xunit;

namespace KTable.Core.Tests;

public class SheetStackTests
{
    static SheetDef Sheet(string id, int z, double x = 0, double side = 10, double turn = 0) =>
        new() { Id = id, Z = z, Pos = new Vec2(x, 0), Size = new Size2(side, side), RotDeg = turn };

    static string Ids(IEnumerable<SheetDef> sheets) => string.Join(" ", sheets.Select(s => s.Id));

    [Fact]
    public void The_stack_reads_top_first_and_of_two_with_one_order_the_earlier_lies_on_top_as_its_relief_does()
    {
        var sheets = new[] { Sheet("a", 1), Sheet("b", 3), Sheet("c", 1), Sheet("d", 0) };
        Assert.Equal("b a c d", Ids(SheetStack.TopDown(sheets)));
        Assert.Equal(4, SheetStack.Above(sheets));
        Assert.Equal(0, SheetStack.Above(Array.Empty<SheetDef>()));
    }

    [Fact]
    public void A_sheet_moved_in_the_stack_gives_every_sheet_an_order_of_its_own()
    {
        var sheets = new List<SheetDef> { Sheet("a", 1), Sheet("b", 3), Sheet("c", 1), Sheet("d", 0) };
        foreach (var (sheet, z) in SheetStack.Placed(sheets, "d", 0)) sheet.Z = z;
        Assert.Equal("d b a c", Ids(SheetStack.TopDown(sheets)));
        Assert.Equal(new[] { 0, 1, 2, 3 }, sheets.Select(s => s.Z).OrderBy(z => z));
        foreach (var (sheet, z) in SheetStack.Placed(sheets, "b", 99)) sheet.Z = z;
        Assert.Equal("d a c b", Ids(SheetStack.TopDown(sheets)));
        Assert.Empty(SheetStack.Placed(sheets, "b", 3));
        Assert.Empty(SheetStack.Placed(sheets, "nope", 0));
    }

    [Fact]
    public void Only_a_sheet_lying_over_another_is_raised_and_one_over_two_stands_above_both()
    {
        var sheets = new[] { Sheet("west", 0, x: 0), Sheet("east", 1, x: 10), Sheet("far", 2, x: 40), Sheet("inset", 5, x: 5, side: 4), Sheet("label", 9, x: 5, side: 1) };
        var levels = SheetStack.Levels(sheets);
        Assert.Equal((0, 0, 0), (levels["west"], levels["east"], levels["far"]));
        Assert.Equal(1, levels["inset"]);
        Assert.Equal(2, levels["label"]);
    }

    [Fact]
    public void A_turned_sheet_is_taken_by_the_room_it_fills()
    {
        var sheets = new[] { Sheet("base", 0, x: 0, side: 10), Sheet("turned", 1, x: 11.5, side: 10, turn: 45) };
        Assert.Equal(1, SheetStack.Levels(sheets)["turned"]);
        sheets[1].RotDeg = 0;
        Assert.Equal(0, SheetStack.Levels(sheets)["turned"]);
    }

    [Fact]
    public void Sheets_laid_edge_to_edge_a_hair_off_square_lie_side_by_side_and_an_inset_on_them_is_raised()
    {
        var turns = new[] { 179.966, 0.006, 180, 0.02, 180, 179.999, 359.995, 180, 0.034 };
        var sheets = new List<SheetDef>();
        for (int i = 0; i < 9; i++)
            sheets.Add(new() { Id = $"s{i}", Z = i, Pos = new Vec2(i % 3 * 58.6667, i / 3 * 44.0), Size = new Size2(58.6667, 44), RotDeg = turns[i] });
        sheets.Add(new() { Id = "inset", Z = 9, Pos = new Vec2(58.6667, 44), Size = new Size2(10, 10), RotDeg = 0.03 });
        var levels = SheetStack.Levels(sheets);
        Assert.All(sheets.Take(9), s => Assert.Equal(0, levels[s.Id]));
        Assert.Equal(1, levels["inset"]);
        var slivers = SheetStack.Slivers(sheets);
        Assert.Contains(slivers["s0"], s => s.Id == "s1");
        Assert.DoesNotContain(slivers["s1"], s => s.Id == "s0");
        Assert.DoesNotContain(slivers["s2"], s => s.Id == "s4");
        Assert.DoesNotContain(slivers["s4"], s => s.Id == "inset");
    }
}
