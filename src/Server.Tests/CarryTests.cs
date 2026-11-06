using KTable.Core.Model;

namespace KTable.Server.Tests;

public class CarryTests
{
    static CarriedPiece Piece(string id) => new(Layers.Umpire, id, 1, 2, 90, 0);

    static Dictionary<long, List<string>> Seen(List<(long To, List<CarriedPiece> Pieces)> sent) =>
        sent.ToDictionary(s => s.To, s => s.Pieces.Select(p => p.Id).ToList());

    [Fact]
    public void A_carried_piece_is_shown_to_everyone_who_sees_its_move_but_the_carrier()
    {
        using var t = new Table();
        var seen = Seen(t.Server.CarryLayout(Table.Ump3, [Piece("p-000002")]));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Obs }, seen.Keys.Order());
        Assert.All(seen.Values, ids => Assert.Equal(new[] { "p-000002" }, ids));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Obs }, t.Server.CarryWatchers(Table.Ump3).Order());
    }

    [Fact]
    public void Pieces_that_are_not_on_the_table_are_left_out()
    {
        using var t = new Table();
        var seen = Seen(t.Server.CarryLayout(Table.Ump, [Piece("p-000002"), Piece("p-nowhere"), new CarriedPiece("elsewhere", "p-000003", 0, 0, 0, 0)]));
        Assert.All(seen.Values, ids => Assert.Equal(new[] { "p-000002" }, ids));
    }

    [Fact]
    public void An_observer_cannot_show_a_carry()
    {
        using var t = new Table();
        Assert.Empty(t.Server.CarryLayout(Table.Obs, [Piece("p-000002")]));
        Assert.Empty(t.Server.CarryWatchers(Table.Obs));
    }

    [Fact]
    public void Someone_not_at_the_table_cannot_show_a_carry()
    {
        using var t = new Table();
        Assert.Empty(t.Server.CarryLayout(77, [Piece("p-000002")]));
    }

    [Fact]
    public void An_umpire_cannot_show_a_carry_of_a_piece_outside_their_zone()
    {
        using var t = new Table();
        t.Server.State.Zones.Add(new Zone { Id = "z-1", Name = "East", Owner = "Umpire Three", Polygon = { new(5, -5), new(20, -5), new(20, 5), new(5, 5) } });
        Assert.Empty(t.Server.CarryLayout(Table.Ump3, [Piece("p-000002")]));
        t.Server.State.Zones[0].Polygon = [new(-5, -5), new(5, -5), new(5, 5), new(-5, 5)];
        Assert.NotEmpty(t.Server.CarryLayout(Table.Ump3, [Piece("p-000002")]));
    }

    [Fact]
    public void Letting_go_tells_everyone_who_was_watching()
    {
        using var t = new Table();
        t.Server.CarryLayout(Table.Ump, [Piece("p-000002")]);
        var let = t.Server.CarryLayout(Table.Ump, []);
        Assert.Equal(new[] { Table.Ump2, Table.Ump3, Table.Obs }, let.Select(s => s.To).Order());
        Assert.All(let, s => Assert.Empty(s.Pieces));
        Assert.Empty(t.Server.CarryWatchers(Table.Ump));
    }

    [Fact]
    public void Someone_who_leaves_is_no_longer_sent_carries_nor_carries()
    {
        using var t = new Table();
        t.Server.CarryLayout(Table.Ump2, [Piece("p-000002")]);
        t.Server.CarryLayout(Table.Ump3, [Piece("p-000003")]);
        t.Server.Leave(Table.Obs);
        t.Server.Leave(Table.Ump3);
        Assert.DoesNotContain(Table.Obs, t.Server.CarryWatchers(Table.Ump2));
        Assert.Empty(t.Server.CarryWatchers(Table.Ump3));
    }
}
