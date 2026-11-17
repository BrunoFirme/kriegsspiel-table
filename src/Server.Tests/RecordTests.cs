using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class RecordTests
{
    const long Ann = 40, Bob = 41;

    static Table Seated()
    {
        var t = new Table();
        t.LetIn(Ann, "Ann", Role.Player);
        t.LetIn(Bob, "Bob", Role.Player);
        return t;
    }

    static List<string> Texts(Table t, long peer) =>
        t.Server.RecordFor(peer).Select(o => o.Message).OfType<RecordList>().Single().Entries.Where(e => e.Kind != RecordEntry.World).Select(e => $"{e.Who}: {e.Text}").ToList();

    static void Played(Table t)
    {
        t.Ok(Ann, new Say { Text = "Where is the bridge?" });
        t.Pass(GameServer.LineEveryMs);
        t.Ok(Ann, new Say { Text = "A word, umpire", To = Say.ToUmpires });
        t.Pass(GameServer.LineEveryMs);
        t.Ok(Ann, new Say { Text = "Meet me at the mill", To = $"p:{Bob}" });
        t.Ok(Table.Ump3, new Say { Text = "Granted", To = $"p:{Ann}" });
        t.Ok(Table.Host, new RollDice { Sides = 6, Count = 1, Label = "ambush" });
        t.Ok(Table.Host, new RollDice { Sides = 6, Count = 1, Label = "morale", Open = true });
    }

    [Fact]
    public void Each_seat_is_given_back_the_lines_it_was_sent_and_the_umpires_all_of_them()
    {
        using var t = Seated();
        Played(t);
        var umpire = Texts(t, Table.Ump3);
        Assert.Equal(6, umpire.Count);
        Assert.Equal(new[] { "Ann: Where is the bridge?", "Ann: A word, umpire", "Ann: Meet me at the mill", "Umpire Three: Granted" }, umpire.Take(4));
        Assert.StartsWith("Umpire One: rolled 1d6 for ambush: ", umpire[4]);
        var ann = Texts(t, Ann);
        Assert.Equal(new[] { "Ann: Where is the bridge?", "Ann: A word, umpire", "Ann: Meet me at the mill", "Umpire Three: Granted" }, ann.Take(4));
        Assert.StartsWith("Umpire One: rolled 1d6 for morale: ", Assert.Single(ann.Skip(4)));
        var bob = Texts(t, Bob);
        Assert.Equal(new[] { "Ann: Where is the bridge?", "Ann: Meet me at the mill" }, bob.Take(2));
        Assert.Equal(3, bob.Count);
        Assert.Equal(2, Texts(t, Table.Obs).Count);
        Assert.Empty(t.Server.RecordFor(999));
    }

    [Fact]
    public void A_line_keeps_its_writer_s_name_and_its_reader_s_after_both_have_left_and_the_game_time_it_was_said_at()
    {
        using var t = Seated();
        t.Ok(Table.Host, new StepClock { Seconds = 600 });
        t.Ok(Ann, new Say { Text = "Meet me at the mill", To = $"p:{Bob}" });
        t.Server.Leave(Ann);
        t.Server.Leave(Bob);
        var said = t.Server.Record.Single(e => e.Kind == RecordEntry.Talk);
        Assert.Equal(("Ann", "Bob", t.Server.State.Clock.Now, 0), (said.Who, said.ToName, said.Seconds, said.Turn));
        Assert.Contains(t.Server.Record, e => e.Kind == RecordEntry.World && e.Text == "Ann left");
        t.Rejoin(42, t.Tokens[Bob], "Bob");
        Assert.Contains("Ann: Meet me at the mill", Texts(t, 42));
    }

    [Fact]
    public void A_script_s_line_carries_no_name_and_a_turn_and_a_timer_due_are_noted_in_the_world_s_lines()
    {
        using var t = Seated();
        t.Ok(Table.Lead, new SetGlobalScript { Text = "function on_turn(turn) game.say('The sun sets.') end" });
        t.Ok(Table.Host, new SetTimer { Name = "Ambush", Due = t.Server.State.Clock.Now + 60 });
        t.Ok(Table.Host, new SetTimer { Name = "Dusk", Due = t.Server.State.Clock.Now + 90, Everyone = true });
        t.Ok(Table.Host, new AdvanceClock());
        Assert.Contains(t.Server.Record, e => e is { Kind: RecordEntry.Talk, Who: null, Text: "The sun sets." });
        Assert.Contains(t.Server.Record, e => e is { Kind: RecordEntry.World, Text: "Turn 1 began", Turn: 1 });
        var ann = t.Server.RecordFor(Ann).Select(o => o.Message).OfType<RecordList>().Single().Entries.Select(e => e.Text).ToList();
        Assert.Contains("Dusk is due", ann);
        Assert.DoesNotContain("Ambush is due", ann);
        Assert.Contains(t.Server.Record, e => e.Text == "Ambush is due");
    }

    [Fact]
    public void The_record_is_saved_with_the_game_comes_back_when_it_is_opened_and_lines_after_the_last_save_come_back_from_the_log()
    {
        using var t = Seated();
        Played(t);
        var save = Path.Combine(t.Root, "talk" + SaveStore.Ext);
        t.Server.SaveTo(save);
        using (var log = new SaveLog(save, SaveLog.StampOf(t.Server.Created, t.Server.State.ScenarioId)))
        {
            t.Server.Applied = log.Append;
            t.Pass(GameServer.LineEveryMs);
            t.Ok(Ann, new Say { Text = "Said after the save" });
        }
        var opened = SaveLoader.Load(save, t.Store);
        var talk = opened.Record.Where(e => e.Kind == RecordEntry.Talk).Select(e => $"{e.Who}: {e.Text}").ToList();
        Assert.Equal(new[] { "Ann: Where is the bridge?", "Ann: A word, umpire", "Ann: Meet me at the mill", "Umpire Three: Granted", "Ann: Said after the save" }, talk);
        Assert.Equal(2, opened.Record.Count(e => e.Kind == RecordEntry.Dice));
    }

    [Fact]
    public void The_record_keeps_its_newest_lines_when_it_grows_past_its_limit()
    {
        using var t = new Table();
        for (int i = 0; i < RecordEntry.MostKept + RecordEntry.MostKept / 10 + 5; i++) t.Server.Keep(new RecordEntry { Text = i.ToString() });
        Assert.InRange(t.Server.Record.Count, RecordEntry.MostKept, RecordEntry.MostKept + RecordEntry.MostKept / 10);
        Assert.Equal((RecordEntry.MostKept + RecordEntry.MostKept / 10 + 4).ToString(), t.Server.Record[^1].Text);
        var sent = t.Server.RecordFor(Table.Host).Select(o => o.Message).OfType<RecordList>().Single().Entries;
        Assert.Equal(RecordEntry.MostSent, sent.Count);
        Assert.Equal(t.Server.Record[^1].Text, sent[^1].Text);
    }
}
