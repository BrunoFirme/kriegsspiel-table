using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class MapTurnTests
{
    static SheetDef Sheet(string id, double x, double z, double w = 10, double h = 10, double rot = 180) =>
        new() { Id = id, Pos = new Vec2(x, z), Size = new Size2(w, h), RotDeg = rot };

    [Fact]
    public void Sheets_that_touch_through_a_chain_of_neighbours_are_one_map_and_a_sheet_apart_is_not()
    {
        var sheets = new List<SheetDef> { Sheet("a", 0, 0), Sheet("b", 10, 0), Sheet("c", 10, 10), Sheet("far", 40, 0), Sheet("d", 10.05, 20) };
        Assert.Equal(new[] { "a", "b", "c", "d" }, MapTurn.Joined(sheets, "a").Select(s => s.Id).OrderBy(i => i));
        Assert.Equal(new[] { "far" }, MapTurn.Joined(sheets, "far").Select(s => s.Id));
        Assert.Empty(MapTurn.Joined(sheets, "gone"));
    }

    [Fact]
    public void A_half_turn_swaps_two_sheets_and_turns_the_pieces_on_them_but_not_those_carried_or_off_the_map()
    {
        var group = new List<SheetDef> { Sheet("a", 0, 0), Sheet("b", 10, 0) };
        var pieces = new List<PieceInstance>
        {
            new() { Id = "on", Pos = new Vec2(2, 1), RotDeg = 10, Stack = 1, Y = 0.5, Attached = { "carried" } },
            new() { Id = "carried", Pos = new Vec2(2.5, 1) },
            new() { Id = "off", Pos = new Vec2(50, 50) },
        };
        var moves = MapTurn.Turned(group, pieces, Layers.Umpire, 180).Items;
        var sheets = moves.OfType<MoveSheet>().ToDictionary(m => m.Id);
        Assert.Equal(new Vec2(10, 0), sheets["a"].Pos);
        Assert.Equal(new Vec2(0, 0), sheets["b"].Pos);
        Assert.Equal(360, sheets["a"].RotDeg);
        var piece = Assert.Single(moves.OfType<MovePiece>());
        Assert.Equal(("on", new Vec2(8, -1), 190.0, 1, 0.5), (piece.Id, piece.Pos, piece.RotDeg, piece.Stack, piece.Y));
    }

    [Fact]
    public void A_sheet_moved_turned_and_stretched_with_its_pieces_keeps_each_on_the_same_spot_of_the_map()
    {
        var sheet = Sheet("a", 0, 0);
        var pieces = new List<PieceInstance>
        {
            new() { Id = "on", Pos = new Vec2(2, 1), RotDeg = 10, Stack = 1, Y = 0.5, Attached = { "carried" } },
            new() { Id = "carried", Pos = new Vec2(2.5, 1) },
            new() { Id = "off", Pos = new Vec2(50, 50) },
        };
        var to = new MoveSheet { Id = "a", Pos = new Vec2(100, 0), RotDeg = 270, Z = 0, Size = new Size2(20, 10) };
        Assert.Same(to, MapTurn.Moved(sheet, to, pieces, Layers.Umpire, carry: false));
        var moves = Assert.IsType<Batch>(MapTurn.Moved(sheet, to, pieces, Layers.Umpire, carry: true)).Items;
        Assert.Same(to, moves[0]);
        var piece = Assert.Single(moves.OfType<MovePiece>());
        var after = new SheetDef { Id = "a", Pos = to.Pos, RotDeg = to.RotDeg, Size = to.Size!.Value };
        var before = Ground.Uv(sheet, new Vec2(2, 1))!.Value;
        var now = Ground.Uv(after, piece.Pos)!.Value;
        Assert.True(Math.Abs(before.u - now.u) < 1e-3 && Math.Abs(before.v - now.v) < 1e-3, $"{before} became {now}");
        Assert.Equal(("on", 100.0, 1, 0.5), (piece.Id, piece.RotDeg, piece.Stack, piece.Y));
        var alone = MapTurn.Moved(sheet, to, pieces.Where(p => p.Id == "off").ToList(), Layers.Umpire, carry: true);
        Assert.Same(to, alone);
    }

    [Fact]
    public void A_sheet_turned_with_its_map_keeps_its_picture_where_the_turn_takes_it()
    {
        var s = Sheet("a", 3, 4, 6, 2, 30);
        var group = new List<SheetDef> { s, Sheet("b", 3, 7, 6, 4, 30) };
        foreach (var deg in new[] { 90.0, -90, 180 })
        {
            var move = MapTurn.Turned(group, new List<PieceInstance>(), Layers.Umpire, deg).Items.OfType<MoveSheet>().First(m => m.Id == "a");
            var turned = Sheet("a", move.Pos.X, move.Pos.Z, 6, 2, move.RotDeg);
            var probe = new PieceInstance { Id = "p", Pos = Ground.FromUv(s, 0.1, 0.2) };
            var expected = Assert.Single(MapTurn.Turned(group, new List<PieceInstance> { probe }, Layers.Umpire, deg).Items.OfType<MovePiece>()).Pos;
            var actual = Ground.FromUv(turned, 0.1, 0.2);
            Assert.Equal(expected.X, actual.X, 0.001);
            Assert.Equal(expected.Z, actual.Z, 0.001);
        }
    }
}
