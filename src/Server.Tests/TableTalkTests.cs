using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class TableTalkTests
{
    const long Ann = 40, Bob = 41;

    static Table Seated()
    {
        var t = new Table();
        t.LetIn(Ann, "Ann", Role.Player);
        t.LetIn(Bob, "Bob", Role.Player);
        return t;
    }

    static List<long> Readers(List<Outgoing> res) => res.Where(o => o.Message is CommandMessage { Command: Say }).Select(o => o.PeerId).OrderBy(id => id).ToList();

    static string Refused(List<Outgoing> res) => Assert.IsType<ErrorMessage>(Assert.Single(res).Message).Text;

    [Fact]
    public void A_player_writes_to_everyone_to_the_umpires_or_to_one_person_and_only_those_addressed_and_the_umpires_read_it()
    {
        using var t = Seated();
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Table.Obs, Ann, Bob }, Readers(t.Server.Handle(Ann, new Say { Text = "Where is the bridge?" })));
        Assert.Equal("one line at a time", Refused(t.Server.Handle(Ann, new Say { Text = "Too soon" })));
        t.Pass(GameServer.LineEveryMs);
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann }, Readers(t.Server.Handle(Ann, new Say { Text = "A word, umpire", To = Say.ToUmpires })));
        t.Pass(GameServer.LineEveryMs);
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann, Bob }, Readers(t.Server.Handle(Ann, new Say { Text = "Meet me at the mill", To = $"p:{Bob}" })));
        Assert.Equal(new[] { Table.Ump, Table.Ump2, Table.Ump3, Ann }, Readers(t.Server.Handle(Table.Ump3, new Say { Text = "Granted", To = $"p:{Ann}" })));
    }

    [Fact]
    public void A_player_without_the_right_a_watcher_a_line_too_long_a_colour_or_a_reader_who_is_not_there_is_refused()
    {
        using var t = Seated();
        Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Obs, new Say { Text = "hello" })));
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Say { Text = new string('x', Say.MostLength + 1) })));
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Say { Text = "red", Color = "#ff0000" })));
        t.Pass(GameServer.LineEveryMs);
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Say { Text = "anyone?", To = "p:999" })));
        t.Ok(Table.Host, new SetPlayerRights { Rights = new() { ["Ann"] = new PlayerRights { Write = false } } });
        Assert.Contains("not allowed", Refused(t.Server.Handle(Ann, new Say { Text = "hello" })));
        Assert.NotEmpty(Readers(t.Server.Handle(Bob, new Say { Text = "hello" })));
    }
}
