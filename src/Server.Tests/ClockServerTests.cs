using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class ClockServerTests
{
    static IEnumerable<Command> Flat(IEnumerable<Command> log) => log.SelectMany(c => c is Batch b ? Flat(b.Items) : new[] { c });

    static List<string> Said(Table t) => Flat(t.Server.Log).OfType<Say>().Select(s => s.Text).ToList();

    static void Ok(List<Outgoing> res) => Assert.DoesNotContain(res, o => o.Message is ErrorMessage);

    static string Refused(List<Outgoing> res) => Assert.IsType<ErrorMessage>(Assert.Single(res).Message).Text;

    static IEnumerable<Command> SentTo(List<Outgoing> res, long peer) =>
        Flat(res.Where(o => o.PeerId == peer).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command));

    [Fact]
    public void A_script_steps_the_clock_ten_times_by_six_seconds_and_a_minute_has_passed_with_no_turn_ended()
    {
        using var t = new Table();
        var clock = t.Server.State.Clock;
        long start = clock.Now;
        Ok(t.Server.Handle(Table.Lead, new SetGlobalScript
        {
            Text = "function round() game.step(6) end function on_clock(before, now) game.say('moved ' .. (now - before)) end function on_timer(name) game.say('rang ' .. name) end",
        }));
        Ok(t.Server.Handle(Table.Host, new SetTimer { Name = "Torch", Due = start + 30 }));
        int turns = t.Server.Turns.Count;
        for (int i = 0; i < 10; i++) t.Server.Request(Table.Host, new RunLua { Target = "", Code = "round()" });
        Assert.Equal(start + 60, clock.Now);
        Assert.Equal((0, turns), (clock.Turn, t.Server.Turns.Count));
        Assert.Equal(10, Said(t).Count(s => s == "moved 6"));
        Assert.Single(Said(t), s => s == "rang Torch");
        Ok(t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log[^1].Seq }));
        Assert.Equal(start + 54, clock.Now);
    }

    [Fact]
    public void A_script_jumps_runs_pauses_and_keeps_timers_and_reads_the_time_as_a_count()
    {
        using var t = new Table();
        var clock = t.Server.State.Clock;
        Ok(t.Server.Handle(Table.Lead, new SetGlobalScript
        {
            Text = """
                function go()
                  game.jump("1812-09-07T06:00")
                  game.timer("Dawn attack", 1800, { everyone = true })
                  game.timer("Guns", "1812-09-07T07:30")
                  game.clear_timer("Guns")
                  local due = game.timers()[1]
                  game.say(due.name .. " " .. due.due .. " " .. due.seconds_left .. " " .. tostring(due.everyone) .. " " .. game.rate .. " " .. game.turn_length)
                  game.run(60)
                  game.pause()
                end
                """,
        }));
        t.Server.Request(Table.Host, new RunLua { Target = "", Code = "go()" });
        Assert.Contains("Dawn attack 1812-09-07T06:30 1800 true 0 900", Said(t));
        Assert.Equal(("1812-09-07T06:00", 0d), (clock.GameTime, clock.Rate));
        Assert.Equal(new[] { "Dawn attack" }, clock.Timers!.Select(x => x.Name));
        var lines = t.Server.Request(Table.Host, new RunLua { Target = "", Code = "game.jump('not a time')" });
        Assert.Contains(lines.Select(o => o.Message).OfType<ScriptConsole>().SelectMany(c => c.Lines), l => l.Text.Contains("not a time in the game's calendar"));
    }

    [Fact]
    public void A_watcher_is_sent_only_the_timers_shown_to_everyone_and_loses_one_that_is_hidden_again()
    {
        using var t = new Table();
        long now = t.Server.State.Clock.Now;
        Assert.Empty(SentTo(t.Server.Handle(Table.Host, new SetTimer { Name = "Ambush", Due = now + 60 }), Table.Obs));
        Assert.Single(SentTo(t.Server.Handle(Table.Host, new SetTimer { Name = "Ambush", Due = now + 60 }), Table.Ump3));
        Assert.Empty(SentTo(t.Server.Handle(Table.Host, new ClearTimer { Name = "Ambush" }), Table.Obs));
        Assert.IsType<SetTimer>(Assert.Single(SentTo(t.Server.Handle(Table.Host, new SetTimer { Name = "Dusk", Due = now + 90, Everyone = true }), Table.Obs)));
        Ok(t.Server.Handle(Table.Host, new SetTimer { Name = "Ambush", Due = now + 60 }));
        var seen = Visibility.Filter(t.Server.State, t.Server.Peers.First(p => p.PeerId == Table.Obs));
        Assert.Equal(new[] { "Dusk" }, seen.Clock.Timers!.Select(x => x.Name));
        Assert.IsType<ClearTimer>(Assert.Single(SentTo(t.Server.Handle(Table.Host, new SetTimer { Name = "Dusk", Due = now + 90 }), Table.Obs)));
        Ok(t.Server.Handle(Table.Host, new SetTimer { Name = "Dusk", Due = now + 90, Everyone = true }));
        Assert.IsType<ClearTimer>(Assert.Single(SentTo(t.Server.Handle(Table.Host, new ClearTimer { Name = "Dusk" }), Table.Obs)));
        Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Obs, new SetTimer { Name = "Mine", Due = now })));
    }

    [Fact]
    public void Leads_set_the_clock_running_any_umpire_pauses_it_and_the_server_says_what_time_it_is()
    {
        double real = 500;
        Ticker.Source = () => real;
        try
        {
            using var t = new Table();
            var clock = t.Server.State.Clock;
            long start = clock.Now;
            Assert.Contains("not allowed", Refused(t.Server.Handle(Table.Ump3, new RunClock { Rate = 60 })));
            var sent = t.Server.Handle(Table.Lead, new RunClock { Rate = 60, Seconds = start + 99999 });
            Assert.Equal(start, Assert.IsType<RunClock>(SentTo(sent, Table.Obs).Single()).Seconds);
            real += 10;
            Assert.Equal(start + 600, clock.Now);
            var paused = t.Server.Handle(Table.Ump3, new RunClock { Rate = 0 });
            Assert.Equal(start + 600, Assert.IsType<RunClock>(SentTo(paused, Table.Obs).Single()).Seconds);
            real += 10;
            Assert.Equal((start + 600, false), (clock.Now, clock.Runs));
            Refused(t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log[^1].Seq }));
        }
        finally
        {
            Ticker.Source = null;
        }
    }

    [Fact]
    public void A_running_clock_rings_its_timers_for_the_scripts_as_the_time_flows_past_them()
    {
        double real = 900;
        Ticker.Source = () => real;
        try
        {
            using var t = new Table();
            long start = t.Server.State.Clock.Now;
            Ok(t.Server.Handle(Table.Lead, new SetGlobalScript { Text = "function on_timer(name) game.say('rang ' .. name) end" }));
            Ok(t.Server.Handle(Table.Host, new SetTimer { Name = "Reinforcements", Due = start + 300 }));
            Ok(t.Server.Handle(Table.Lead, new RunClock { Rate = 60 }));
            real += 4;
            t.Server.Scripts.Tick(1);
            Assert.DoesNotContain("rang Reinforcements", Said(t));
            real += 2;
            t.Server.Scripts.Tick(2);
            t.Server.Scripts.Tick(3);
            Assert.Single(Said(t), s => s == "rang Reinforcements");
        }
        finally
        {
            Ticker.Source = null;
        }
    }

    [Fact]
    public void A_game_saved_while_its_clock_ran_opens_paused_at_the_time_it_had_reached()
    {
        double real = 1300;
        Ticker.Source = () => real;
        try
        {
            using var t = new Table();
            long start = t.Server.State.Clock.Now;
            Ok(t.Server.Handle(Table.Lead, new RunClock { Rate = 10 }));
            real += 6;
            var save = Path.Combine(t.Root, "running" + SaveStore.Ext);
            t.Server.SaveTo(save);
            real += 100;
            var opened = SaveLoader.Load(save, t.Store);
            Assert.Equal((start + 60, false), (opened.State.Clock.Now, opened.State.Clock.Runs));
        }
        finally
        {
            Ticker.Source = null;
        }
    }

    [Fact]
    public void A_turn_s_name_is_kept_with_the_turn_in_the_history()
    {
        using var t = new Table();
        var clock = t.Server.State.Clock;
        Ok(t.Server.Handle(Table.Host, new SetClock { Turn = clock.Turn, Seconds = clock.Now, TurnSeconds = 0, Name = "The Tavern" }));
        Ok(t.Server.Handle(Table.Host, new AdvanceClock()));
        Assert.Equal(("The Tavern", null), (t.Server.Turns[^1].Name, clock.Name));
        Ok(t.Server.Handle(Table.Host, new Undo { TargetSeq = t.Server.Log[^1].Seq }));
        Assert.Equal(("The Tavern", 0), (clock.Name, clock.Turn));
    }

    [Fact]
    public void A_script_on_a_piece_drives_and_reads_the_clock_of_the_place_it_stands_in_and_the_table_keeps_its_own()
    {
        using var t = new Table();
        var clock = t.Server.State.Clock;
        long start = clock.Now;
        Ok(t.Server.Handle(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(2, 2) }));
        Ok(t.Server.Handle(Table.Lead, new SetClockApart { Place = "s-000001", Clock = new ClockTime { Seconds = start } }));
        Ok(t.Server.Handle(Table.Host, new SetScript
        {
            Layer = Layers.Umpire, Id = "p-000002", Text = "function round() game.step(6) game.timer('Torch', 60) game.say('at ' .. (game.seconds - " + start + ")) end",
        }));
        t.Server.Request(Table.Host, new RunLua { Target = "p-000002", Code = "round()" });
        Assert.Equal((start, start + 6), (clock.Now, clock.Of("s-000001").Now));
        Assert.Equal(("Torch", start + 66), (clock.Of("s-000001").Timers![0].Name, clock.Of("s-000001").Timers![0].Due));
        Assert.Null(clock.Timers);
        Assert.Contains("at 6", Said(t));
        Ok(t.Server.Handle(Table.Host, new MovePiece { Layer = Layers.Umpire, Id = "p-000002", Pos = new(900, 900) }));
        t.Server.Request(Table.Host, new RunLua { Target = "p-000002", Code = "round()" });
        Assert.Equal((start + 6, start + 6), (clock.Now, clock.Of("s-000001").Now));
    }
}
