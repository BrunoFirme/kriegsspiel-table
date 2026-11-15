using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Server.Tests;

public class PlayerDiceTests
{
    const long Bruno = 40, Ana = 41;

    static Table WithPlayers()
    {
        var t = new Table();
        t.LetIn(Bruno, "Bruno", Role.Player);
        t.LetIn(Ana, "Ana", Role.Player);
        return t;
    }

    static RollDice? Seen(List<Outgoing> outs, long peer) =>
        outs.Where(o => o.PeerId == peer).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command).OfType<RollDice>().FirstOrDefault();

    [Fact]
    public void A_player_rolls_in_the_open_and_everyone_sees_the_result()
    {
        using var t = WithPlayers();
        var outs = t.Ok(Bruno, new RollDice { Sides = 20, Count = 2, Open = true });
        var mine = Seen(outs, Bruno);
        Assert.NotNull(mine);
        Assert.Equal("Bruno", mine!.ActorName);
        Assert.Equal(2, mine.Results.Count);
        Assert.All(mine.Results, r => Assert.InRange(r, 1, 20));
        Assert.NotNull(Seen(outs, Ana));
        Assert.NotNull(Seen(outs, Table.Obs));
        Assert.NotNull(Seen(outs, Table.Ump3));
    }

    [Fact]
    public void A_secret_roll_reaches_the_umpires_and_the_roller_only()
    {
        using var t = WithPlayers();
        var outs = t.Ok(Bruno, new RollDice { Sides = 6, Count = 1 });
        Assert.NotNull(Seen(outs, Bruno));
        Assert.NotNull(Seen(outs, Table.Ump3));
        Assert.Null(Seen(outs, Ana));
        Assert.Null(Seen(outs, Table.Obs));
    }

    [Fact]
    public void A_player_the_umpire_took_the_dice_from_cannot_roll_and_nobody_may_throw_board_dice_as_a_player()
    {
        using var t = WithPlayers();
        t.Ok(Table.Host, new SetPlayerRights { Rights = new() { ["Bruno"] = new PlayerRights { Dice = false } } });
        Assert.Contains(t.Server.Handle(Bruno, new RollDice { Sides = 6, Count = 1, Open = true }), o => o.PeerId == Bruno && o.Message is ErrorMessage);
        Assert.Contains(t.Server.Handle(Ana, new RollDice { Sides = 6, Count = 1, Physical = true }), o => o.PeerId == Ana && o.Message is ErrorMessage);
        Assert.Contains(t.Server.Handle(Table.Obs, new RollDice { Sides = 6, Count = 1, Open = true }), o => o.PeerId == Table.Obs && o.Message is ErrorMessage);
    }
}
