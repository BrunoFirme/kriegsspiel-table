using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class ClockTests
{
    static readonly CalendarDef Real = CalendarDef.Gregorian;

    [Theory]
    [InlineData("0001-01-01T00:00")]
    [InlineData("1800-01-01T06:00")]
    [InlineData("1812-09-07T06:30")]
    [InlineData("1900-02-28T23:59")]
    [InlineData("2000-02-29T12:00")]
    [InlineData("9999-12-31T23:59")]
    public void The_real_calendar_reads_every_date_the_old_text_clock_held_as_the_same_moment(string text)
    {
        var then = DateTime.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        long seconds = Real.Read(text)!.Value;
        Assert.Equal(then.Ticks / TimeSpan.TicksPerSecond, seconds);
        Assert.Equal(text, Real.Text(seconds));
        Assert.Equal(then.DayOfYear, Real.DayOfYear(seconds));
        Assert.Equal(then.DayOfWeek.ToString(), Real.WeekDay(seconds));
    }

    [Fact]
    public void A_year_before_1_is_set_stepped_and_written_with_its_era()
    {
        long ides = Real.Read("-0044-03-15T11:00")!.Value;
        Assert.True(ides < 0);
        Assert.Equal("-0044-03-15T11:00", Real.Text(ides));
        Assert.Equal("15 Mar 44 BC", Real.Date(ides)[4..]);
        Assert.Equal(ides, Real.Read("15 March 44 BC 11:00"));
        long newYear = Real.Read("-0001-12-31T23:59:59")!.Value + 1;
        Assert.Equal("0001-01-01T00:00", Real.Text(newYear));
        Assert.Null(Real.Read("0000-01-01T00:00"));
        Assert.Null(Real.Read("1900-02-29T00:00"));
        Assert.NotNull(Real.Read("-0001-02-29T00:00"));
    }

    [Fact]
    public void The_julian_calendar_names_the_same_moment_in_its_own_words()
    {
        long moment = Real.Read("1582-10-15T00:00")!.Value;
        Assert.Equal("1582-10-05T00:00", CalendarDef.Julian.Text(moment));
        Assert.Equal(Real.WeekDay(moment), CalendarDef.Julian.WeekDay(moment));
        Assert.Equal(moment, CalendarDef.Julian.Read("1582-10-05T00:00"));
        Assert.Equal("1700-02-29T00:00", CalendarDef.Julian.Text(CalendarDef.Julian.Read("1700-02-29T00:00")!.Value));
    }

    [Fact]
    public void A_calendar_of_a_world_of_its_own_counts_its_months_weeks_leap_days_and_moons()
    {
        var world = new CalendarDef
        {
            Id = "w", Name = "Reckoning", Months = { new MonthDef { Name = "Thaw", Days = 40 }, new MonthDef { Name = "Blaze", Days = 40 }, new MonthDef { Name = "Fall", Days = 20 } },
            WeekDays = { "One", "Two", "Three", "Four", "Five" }, Leap = new LeapRule { Month = 2, Every = 2 },
            Moons = { new MoonDef { Name = "Pale", CycleDays = 10, PhaseAtStart = 0.5 } },
        };
        Assert.True(world.Sound);
        Assert.Equal(100, world.YearDays(0));
        Assert.Equal(101, world.YearDays(1));
        long leapDay = world.Read("0002-03-21T00:00")!.Value;
        Assert.Equal(100 + 100, leapDay / CalendarDef.SecondsInDay);
        Assert.Equal("0003-01-01T00:00", world.Text(leapDay + CalendarDef.SecondsInDay));
        Assert.Equal("0000-03-21T00:00", world.Text(-CalendarDef.SecondsInDay));
        Assert.Equal("One", world.WeekDay(0));
        Assert.Equal("Five", world.WeekDay(-CalendarDef.SecondsInDay));
        Assert.Equal(0.5, world.MoonPhase(world.Moons[0], 0), 6);
        Assert.Equal(0.0, world.MoonPhase(world.Moons[0], 5 * CalendarDef.SecondsInDay), 6);
        Assert.Equal(world.Text(leapDay), world.Clone().Text(leapDay));
        Assert.Equal("Pale full, 100% lit", MoonPhases.Line(world, 0));
        Assert.Equal("Pale first quarter, 50% lit", MoonPhases.Line(world, (long)(7.5 * CalendarDef.SecondsInDay)));
    }

    [Fact]
    public void The_moon_s_phase_is_named_in_eighths_and_the_real_calendar_has_the_real_moon()
    {
        Assert.Equal(new[] { "new", "waxing crescent", "first quarter", "waxing gibbous", "full", "waning gibbous", "last quarter", "waning crescent", "new" },
            new[] { 0, 0.12, 0.25, 0.4, 0.5, 0.6, 0.75, 0.85, 0.97 }.Select(MoonPhases.Name));
        Assert.Equal("Moon full, 100% lit", MoonPhases.Line(CalendarDef.Gregorian, CalendarDef.Gregorian.Read("2024-01-25T18:00")!.Value));
        Assert.Equal(CalendarDef.Gregorian.Read("2024-01-24T12:00"), CalendarDef.Julian.Read("2024-01-11T12:00"));
        Assert.Equal("Moon new, 0% lit", MoonPhases.Line(CalendarDef.Julian, CalendarDef.Julian.Read("2023-12-29T12:00")!.Value));
        Assert.Null(MoonPhases.Line(new CalendarDef { Months = { new MonthDef { Name = "Only" } } }, 0));
    }

    [Fact]
    public void A_save_of_the_text_clock_opens_at_the_same_date_and_turn_length()
    {
        var old = Json.Deserialize<GameClock>("""{"turn":4,"gameTime":"1812-09-07T06:30","incrementMinutes":20}""");
        Assert.Equal((4, "1812-09-07T06:30", 1200), (old.Turn, old.GameTime, old.TurnSeconds));
        var words = Json.Deserialize<GameClock>("""{"turn":2,"gameTime":"Morning of the third day","incrementMinutes":60}""");
        Assert.Equal(("Morning of the third day", GameClock.UsualStart, 3600), (words.Name, words.Seconds, words.TurnSeconds));
        var again = Json.Deserialize<GameClock>(Json.Serialize(old));
        Assert.Equal((4, "1812-09-07T06:30", 1200), (again.Turn, again.GameTime, again.TurnSeconds));
        Assert.DoesNotContain("gameTime", Json.Serialize(old));
    }

    [Fact]
    public void A_step_moves_the_time_and_ends_no_turn_and_its_undo_steps_back()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00", turn: 3) };
        var step = new StepClock { Seconds = 6 };
        Assert.False(SaveFile.EndsTurn(g, step));
        for (int i = 0; i < 10; i++) Assert.Null(StateMachine.Apply(g, new StepClock { Seconds = 6 }, out _));
        Assert.Equal((3, "1757-12-05T13:01"), (g.Clock.Turn, g.Clock.GameTime));
        Assert.Null(StateMachine.Apply(g, step, out var back));
        Assert.Equal("1757-12-05T13:01:06", g.Clock.GameTime);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal("1757-12-05T13:01", g.Clock.GameTime);
        Assert.NotNull(StateMachine.Apply(g, new StepClock(), out _));
        Assert.NotNull(StateMachine.Apply(g, new StepClock { Seconds = long.MaxValue }, out _));
    }

    [Fact]
    public void Next_turn_moves_the_time_by_the_turn_length_drops_the_turn_s_name_and_its_undo_brings_both_back()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00", turn: 3, turnSeconds: 1800) };
        Assert.Null(StateMachine.Apply(g, new SetClock { Turn = 3, Seconds = g.Clock.Seconds, TurnSeconds = 1800, Name = " The Tavern " }, out _));
        Assert.Equal("The Tavern", g.Clock.Name);
        Assert.True(SaveFile.EndsTurn(g, new AdvanceClock()));
        Assert.Equal("The Tavern", SaveFile.RecordOf(g, DateTime.UtcNow).Name);
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out var back));
        Assert.Equal((4, "1757-12-05T13:30", null), (g.Clock.Turn, g.Clock.GameTime, g.Clock.Name));
        Assert.False(SaveFile.EndsTurn(g, back!));
        Assert.Null(StateMachine.Apply(g, back!, out var again));
        Assert.Equal((3, "1757-12-05T13:00", "The Tavern"), (g.Clock.Turn, g.Clock.GameTime, g.Clock.Name));
        Assert.Null(StateMachine.Apply(g, again!, out _));
        Assert.Equal(4, g.Clock.Turn);
    }

    [Fact]
    public void A_turn_length_of_none_keeps_the_time_and_only_counts_the_turn()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00", turnSeconds: 0) };
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out _));
        Assert.Equal((1, "1757-12-05T13:00"), (g.Clock.Turn, g.Clock.GameTime));
        Assert.NotNull(StateMachine.Apply(new GameState(), new AdvanceClock { Back = true }, out _));
    }

    [Fact]
    public void A_timer_is_set_changed_and_cleared_by_its_name_and_each_undo_puts_back_what_was()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00") };
        long two = g.Clock.Cal.Read("1757-12-05T14:00")!.Value;
        Assert.Null(StateMachine.Apply(g, new SetTimer { Name = " Reinforcements ", Due = two }, out var unset));
        Assert.IsType<ClearTimer>(unset);
        Assert.Null(StateMachine.Apply(g, new SetTimer { Name = "Reinforcements", Due = two + 60, Everyone = true }, out var before));
        Assert.Equal((1, two + 60, true), (g.Clock.Timers!.Count, g.Clock.Timers[0].Due, g.Clock.Timers[0].Everyone));
        Assert.Null(StateMachine.Apply(g, before!, out _));
        Assert.Equal((two, false), (g.Clock.Timer("Reinforcements")!.Due, g.Clock.Timer("Reinforcements")!.Everyone));
        Assert.Null(StateMachine.Apply(g, new ClearTimer { Name = "Reinforcements" }, out var set));
        Assert.Null(g.Clock.Timers);
        Assert.Null(StateMachine.Apply(g, new ClearTimer { Name = "Reinforcements" }, out _));
        Assert.Null(StateMachine.Apply(g, set!, out _));
        Assert.Equal(two, g.Clock.Timer("Reinforcements")!.Due);
        Assert.NotNull(StateMachine.Apply(g, new SetTimer { Name = " ", Due = two }, out _));
    }

    [Fact]
    public void A_timer_is_due_once_the_clock_passes_it_by_a_step_a_jump_or_a_turn_and_pending_again_when_the_clock_goes_back()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00", turnSeconds: 3600) };
        long two = g.Clock.Cal.Read("1757-12-05T14:00")!.Value;
        StateMachine.Apply(g, new SetTimer { Name = "Reinforcements", Due = two }, out _);
        Assert.Equal("Reinforcements", g.Clock.NextDue()!.Name);
        long before = g.Clock.Now;
        StateMachine.Apply(g, new StepClock { Seconds = 1800 }, out _);
        Assert.Empty(g.Clock.Due(before, g.Clock.Now));
        StateMachine.Apply(g, new StepClock { Seconds = 1800 }, out var back);
        Assert.Single(g.Clock.Due(before, g.Clock.Now));
        Assert.Null(g.Clock.NextDue());
        StateMachine.Apply(g, back!, out _);
        Assert.NotNull(g.Clock.NextDue());
        StateMachine.Apply(g, new AdvanceClock(), out _);
        Assert.Null(g.Clock.NextDue());
        StateMachine.Apply(g, new SetClock { Turn = 1, Seconds = before, TurnSeconds = 3600 }, out _);
        Assert.NotNull(g.Clock.NextDue());
    }

    [Fact]
    public void A_running_clock_is_worked_out_from_when_it_was_set_and_its_rate_and_a_pause_keeps_the_time_reached()
    {
        double real = 100;
        Ticker.Source = () => real;
        try
        {
            var g = new GameState { Clock = GameClock.At("1757-12-05T13:00") };
            Assert.Null(StateMachine.Apply(g, new RunClock { Rate = 60 }, out _));
            real += 30;
            Assert.Equal("1757-12-05T13:30", g.Clock.GameTime);
            var copy = Json.Deserialize<GameClock>(Json.Serialize(g.Clock));
            var peer = g.Clock.Copy();
            real += 30;
            Assert.Equal("1757-12-05T14:00", g.Clock.GameTime);
            Assert.Equal("1757-12-05T14:00", copy.GameTime);
            Assert.Equal("1757-12-05T14:00", peer.GameTime);
            Assert.Null(StateMachine.Apply(g, new StepClock { Seconds = 600 }, out _));
            Assert.Equal("1757-12-05T14:10", g.Clock.GameTime);
            Assert.Null(StateMachine.Apply(g, new RunClock { Rate = 0, Seconds = g.Clock.Now }, out _));
            real += 500;
            Assert.Equal("1757-12-05T14:10", g.Clock.GameTime);
            Assert.NotNull(StateMachine.Apply(g, new RunClock { Rate = double.NaN }, out _));
            Assert.NotNull(StateMachine.Apply(g, new RunClock { Rate = -1 }, out _));
        }
        finally
        {
            Ticker.Source = null;
        }
    }

    [Fact]
    public void A_place_s_clock_apart_stays_put_on_next_turn_moves_by_its_own_turn_length_when_set_and_joins_back()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00", turnSeconds: CalendarDef.SecondsInDay) };
        var dungeon = new ClockTime { Seconds = g.Clock.Now };
        Assert.Null(StateMachine.Apply(g, new SetClockApart { Place = "sheet-2", Clock = dungeon }, out var join));
        Assert.Null(StateMachine.Apply(g, new StepClock { Seconds = 600, Place = "sheet-2" }, out _));
        Assert.Null(StateMachine.Apply(g, new SetTimer { Name = "Torch", Due = dungeon.Seconds + 3600, Place = "sheet-2" }, out _));
        Assert.Equal(("1757-12-05T13:00", "1757-12-05T13:10"), (g.Clock.GameTime, g.Clock.Cal.Text(g.Clock.Of("sheet-2").Now)));
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out var back));
        Assert.Equal(("1757-12-06T13:00", "1757-12-05T13:10"), (g.Clock.GameTime, g.Clock.Cal.Text(g.Clock.Of("sheet-2").Now)));
        Assert.Null(StateMachine.Apply(g, new SetClock { Seconds = g.Clock.Of("sheet-2").Now, TurnSeconds = 900, Place = "sheet-2" }, out _));
        Assert.Null(StateMachine.Apply(g, new AdvanceClock(), out _));
        Assert.Equal((2, "1757-12-05T13:25"), (g.Clock.Turn, g.Clock.Cal.Text(g.Clock.Of("sheet-2").Now)));
        Assert.Same(g.Clock, g.Clock.Of("the road"));
        Assert.NotNull(StateMachine.Apply(g, new StepClock { Seconds = 60, Place = "the road" }, out _));
        Assert.Null(StateMachine.Apply(g, new SetClockApart { Place = "sheet-2" }, out var apart));
        Assert.Null(g.Clock.Apart);
        Assert.Null(StateMachine.Apply(g, apart!, out _));
        Assert.Equal("Torch", g.Clock.Of("sheet-2").Timers![0].Name);
        Assert.NotNull(back);
        Assert.NotNull(join);
    }

    [Fact]
    public void Players_are_given_only_the_timers_shown_to_everyone()
    {
        var clock = GameClock.At("1757-12-05T13:00");
        clock.Timers = new() { new GameTimer { Name = "Ambush", Due = clock.Seconds + 60 }, new GameTimer { Name = "Dusk", Due = clock.Seconds + 90, Everyone = true } };
        clock.Apart = new() { ["s"] = new ClockTime { Timers = new() { new GameTimer { Name = "Trap", Due = 5 } } } };
        var shown = clock.Shown();
        Assert.Equal(new[] { "Dusk" }, shown.Timers!.Select(t => t.Name));
        Assert.Null(shown.Apart!["s"].Timers);
        Assert.Equal(2, clock.Timers.Count);
    }

    [Fact]
    public void The_steps_and_the_calendar_are_set_and_put_back_and_the_calendar_keeps_the_moment()
    {
        var g = new GameState { Clock = GameClock.At("1582-10-15T00:00") };
        long moment = g.Clock.Now;
        Assert.Equal(GameClock.UsualSteps, g.Clock.StepList);
        Assert.Null(StateMachine.Apply(g, new SetClockSteps { Steps = new() { 6, 600 } }, out var steps));
        Assert.Equal(new[] { 6, 600 }, g.Clock.StepList);
        Assert.Null(StateMachine.Apply(g, steps!, out _));
        Assert.Null(g.Clock.Steps);
        Assert.NotNull(StateMachine.Apply(g, new SetClockSteps { Steps = new() { 0 } }, out _));
        Assert.Null(StateMachine.Apply(g, new SetCalendar { Calendar = CalendarDef.Julian }, out var real));
        Assert.Equal((moment, "1582-10-05T00:00"), (g.Clock.Now, g.Clock.GameTime));
        Assert.Null(StateMachine.Apply(g, real!, out _));
        Assert.Equal("1582-10-15T00:00", g.Clock.GameTime);
        Assert.NotNull(StateMachine.Apply(g, new SetCalendar { Calendar = new CalendarDef { Id = "empty" } }, out _));
    }

    [Fact]
    public void Turn_lengths_are_written_in_the_largest_unit_that_fits()
    {
        Assert.Equal(new[] { "none", "6 s", "1′ 30″", "15′", "1 h", "1 h 30′", "1 day", "3 days", "25 h" },
            new[] { 0, 6, 90, 900, 3600, 5400, 86400, 259200, 90000 }.Select(GameClock.StepText));
    }

    [Fact]
    public void The_clock_of_a_spot_is_that_of_the_innermost_place_round_it_that_keeps_one_and_else_the_table_s()
    {
        var g = new GameState { Clock = GameClock.At("1757-12-05T13:00") };
        g.Sheets.Add(new SheetDef { Id = "s1", Name = "Dungeon", Pos = new Vec2(0, 0), Size = new Size2(100, 100) });
        g.Layers[Layers.Umpire] = new() { new PieceInstance { Id = "inn", Def = "house", Pos = new Vec2(10, 10) } };
        PieceDef? Def(string id) => id == "house" ? new PieceDef { Id = "house", Name = "Inn", Tags = { Doors.BuildingTag }, Size = new Size2(6, 6) } : null;
        Assert.Null(PlaceClocks.At(g, Def, new Vec2(10, 10)));
        StateMachine.Apply(g, new SetClockApart { Place = "s1", Clock = new ClockTime { Seconds = 5 } }, out _);
        Assert.Equal("s1", PlaceClocks.At(g, Def, new Vec2(10, 10)));
        StateMachine.Apply(g, new SetClockApart { Place = "inn", Clock = new ClockTime { Seconds = 9 } }, out _);
        Assert.Equal("inn", PlaceClocks.At(g, Def, new Vec2(10, 10)));
        Assert.Equal("s1", PlaceClocks.At(g, Def, new Vec2(40, 40)));
        Assert.Null(PlaceClocks.At(g, Def, new Vec2(900, 900)));
        Assert.Equal((9, 5), (g.Clock.AsOf("inn").Now, g.Clock.AsOf("s1").Now));
        Assert.Same(g.Clock, g.Clock.AsOf(null));
        Assert.Same(g.Clock, g.Clock.AsOf("the road"));
        Assert.Equal(g.Clock.Turn, g.Clock.AsOf("inn").Turn);
    }
}
