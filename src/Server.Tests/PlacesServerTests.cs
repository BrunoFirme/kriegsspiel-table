using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class PlacesServerTests
{
    const long Player = 40;

    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static IEnumerable<Command> SentTo(List<Outgoing> res, long peer) =>
        Flat(res.Where(o => o.PeerId == peer).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command));

    [Fact]
    public void A_watcher_is_never_sent_a_pin_neither_as_it_is_made_changed_forgotten_nor_in_the_table_it_joins()
    {
        using var t = new Table();
        t.LetIn(Player, "Ann", Role.Player);
        var made = t.Ok(Table.Ump3, new SetPlace { Place = new PlacePin { Id = "pl-999999", Name = "The ambush", Pos = new Vec2(2, 2) } });
        var pin = Assert.Single(t.Server.State.Places!);
        Assert.NotEqual("pl-999999", pin.Id);
        Assert.StartsWith(PlacePin.IdPrefix + "-", pin.Id);
        Assert.Single(SentTo(made, Table.Ump2).OfType<SetPlace>());
        Assert.Empty(SentTo(made, Table.Obs));
        Assert.Empty(SentTo(made, Player));
        var moved = t.Ok(Table.Lead, new SetPlace { Place = new PlacePin { Id = pin.Id, Name = "The real ambush", Pos = new Vec2(3, 3) } });
        Assert.Equal(pin.Id, t.Server.State.Places![0].Id);
        Assert.Empty(SentTo(moved, Table.Obs).Concat(SentTo(moved, Player)));
        foreach (var watcher in new[] { Table.Obs, Player })
        {
            var view = Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(watcher)).Message).View;
            Assert.Null(view.Places);
        }
        Assert.Null(t.LetIn(41, "Bob", Role.Observer).View!.Places);
        Assert.Equal("The real ambush", Assert.IsType<Snapshot>(Assert.Single(t.Server.Resync(Table.Ump3)).Message).View.Places![0].Name);
        var forgot = t.Ok(Table.Ump3, new RemovePlace { Id = pin.Id });
        Assert.Empty(SentTo(forgot, Table.Obs).Concat(SentTo(forgot, Player)));
        Assert.Null(t.Server.State.Places);
        t.Ok(Table.Ump3, new Undo { TargetSeq = t.Server.Log[^1].Seq });
        Assert.Equal((pin.Id, "The real ambush"), (t.Server.State.Places![0].Id, t.Server.State.Places[0].Name));
        Assert.Contains("not allowed", t.Error(Table.Obs, new SetPlace { Place = new PlacePin { Name = "Mine" } }));
        Assert.Contains("not allowed", t.Error(Player, new RemovePlace { Id = pin.Id }));
    }
}
