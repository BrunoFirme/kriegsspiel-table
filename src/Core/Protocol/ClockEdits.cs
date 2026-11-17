using KTable.Core.Model;

namespace KTable.Core.Protocol;

static class ClockEdits
{
    public const int MostApart = 200, MostPlaceLength = 64;
    const string NoSuchPlace = "that place keeps the table's time", TooFar = "the clock cannot go that far";

    public static string? Apply(GameState g, Command c, ref Command? inverse) => c switch
    {
        SetClock sc => Set(g.Clock, sc, ref inverse),
        AdvanceClock ac => Advance(g.Clock, ac, ref inverse),
        StepClock st => Step(g.Clock, st, ref inverse),
        RunClock rc => Run(g.Clock, rc),
        SetTimer st => Timer(g.Clock, st, ref inverse),
        ClearTimer ct => Clear(g.Clock, ct, ref inverse),
        SetClockSteps ss => Steps(g.Clock, ss, ref inverse),
        SetCalendar sc => Calendar(g.Clock, sc, ref inverse),
        SetClockApart sa => Apart(g.Clock, sa, ref inverse),
        _ => "not a clock edit",
    };

    static ClockTime? Of(GameClock clock, string? place) =>
        place == null ? clock : clock.Apart != null && clock.Apart.TryGetValue(place, out var apart) ? apart : null;

    static string? Set(GameClock clock, SetClock sc, ref Command? inverse)
    {
        if (Of(clock, sc.Place) is not { } time) return NoSuchPlace;
        if (sc.Turn < 0 || sc.TurnSeconds is < 0 or > ClockTime.MostTurnSeconds) return "that turn or turn length cannot be set";
        if (Math.Abs(sc.Seconds) > CalendarDef.MostSeconds) return TooFar;
        if (sc.Name is { Length: > GameClock.MostNameLength }) return "that name is too long";
        inverse = new SetClock { Turn = clock.Turn, Seconds = time.Now, TurnSeconds = time.TurnSeconds, Name = clock.Name, Place = sc.Place };
        time.Put(sc.Seconds);
        time.TurnSeconds = sc.TurnSeconds;
        if (sc.Place != null) return null;
        clock.Turn = sc.Turn;
        clock.Name = string.IsNullOrWhiteSpace(sc.Name) ? null : sc.Name.Trim();
        return null;
    }

    static string? Advance(GameClock clock, AdvanceClock ac, ref Command? inverse)
    {
        int turns = ac.Back ? -1 : 1;
        if (clock.Turn + turns < 0) return "there is no turn before this one";
        if (!clock.CanAdvance(turns)) return TooFar;
        if (ac.Name is { Length: > GameClock.MostNameLength }) return "that name is too long";
        inverse = new AdvanceClock { Back = !ac.Back, Name = clock.Name };
        clock.Advance(turns);
        clock.Name = ac.Back && !string.IsNullOrWhiteSpace(ac.Name) ? ac.Name : null;
        return null;
    }

    static string? Step(GameClock clock, StepClock st, ref Command? inverse)
    {
        if (Of(clock, st.Place) is not { } time) return NoSuchPlace;
        if (st.Seconds == 0 || Math.Abs(st.Seconds) > CalendarDef.MostSeconds || !time.CanMove(st.Seconds)) return TooFar;
        inverse = new StepClock { Seconds = -st.Seconds, Place = st.Place };
        time.Move(st.Seconds);
        return null;
    }

    static string? Run(GameClock clock, RunClock rc)
    {
        if (Of(clock, rc.Place) is not { } time) return NoSuchPlace;
        if (!double.IsFinite(rc.Rate) || rc.Rate < 0 || rc.Rate > ClockTime.MostRate) return "that rate cannot be set";
        if (rc.Seconds is { } seconds && Math.Abs(seconds) > CalendarDef.MostSeconds) return TooFar;
        if (rc.Seconds is { } now) time.Put(now);
        time.Run(rc.Rate);
        return null;
    }

    static string? Timer(GameClock clock, SetTimer st, ref Command? inverse)
    {
        if (Of(clock, st.Place) is not { } time) return NoSuchPlace;
        string name = st.Name.Trim();
        if (name.Length is 0 or > GameTimer.MostNameLength) return "a timer needs a name of up to 80 letters";
        if (Math.Abs(st.Due) > CalendarDef.MostSeconds) return TooFar;
        var timers = time.Timers ??= new();
        var old = timers.Find(t => t.Name == name);
        if (old == null && timers.Count >= GameTimer.MostOnAClock) return "too many timers";
        inverse = old != null ? new SetTimer { Name = old.Name, Due = old.Due, Everyone = old.Everyone, Place = st.Place } : new ClearTimer { Name = name, Place = st.Place };
        if (old != null) timers.Remove(old);
        timers.Add(new GameTimer { Name = name, Due = st.Due, Everyone = st.Everyone });
        st.Name = name;
        return null;
    }

    static string? Clear(GameClock clock, ClearTimer ct, ref Command? inverse)
    {
        inverse = new ClearTimer { Name = ct.Name, Place = ct.Place };
        if (Of(clock, ct.Place) is not { Timers: { } timers } time || timers.Find(t => t.Name == ct.Name) is not { } old) return null;
        inverse = new SetTimer { Name = old.Name, Due = old.Due, Everyone = old.Everyone, Place = ct.Place };
        timers.Remove(old);
        if (timers.Count == 0) time.Timers = null;
        return null;
    }

    static string? Steps(GameClock clock, SetClockSteps ss, ref Command? inverse)
    {
        if (ss.Steps is { } steps && (steps.Count > GameClock.MostSteps || steps.Any(s => s is < 1 or > ClockTime.MostTurnSeconds))) return "those steps cannot be set";
        inverse = new SetClockSteps { Steps = clock.Steps?.ToList() };
        clock.Steps = ss.Steps is { Count: > 0 } own ? own.ToList() : null;
        return null;
    }

    static string? Calendar(GameClock clock, SetCalendar sc, ref Command? inverse)
    {
        if (sc.Calendar is { Sound: false }) return "that calendar cannot be used";
        inverse = new SetCalendar { Calendar = clock.Calendar?.Clone() };
        clock.Calendar = sc.Calendar == null || sc.Calendar.Id == CalendarDef.GregorianId ? null : sc.Calendar.Clone();
        return null;
    }

    static string? Apart(GameClock clock, SetClockApart sa, ref Command? inverse)
    {
        if (sa.Place.Length is 0 or > MostPlaceLength) return "no such place";
        ClockTime? old = null;
        clock.Apart?.TryGetValue(sa.Place, out old);
        if (sa.Clock is { } set)
        {
            if (set.TurnSeconds is < 0 or > ClockTime.MostTurnSeconds || Math.Abs(set.Seconds) > CalendarDef.MostSeconds) return TooFar;
            if (set.Timers is { Count: > GameTimer.MostOnAClock }) return "too many timers";
            if (old == null && clock.Apart is { Count: >= MostApart }) return "too many places keep their own time";
            (clock.Apart ??= new())[sa.Place] = set.CopyTime();
        }
        else if (old != null)
        {
            clock.Apart!.Remove(sa.Place);
            if (clock.Apart.Count == 0) clock.Apart = null;
        }
        inverse = new SetClockApart { Place = sa.Place, Clock = old?.CopyTime() };
        return null;
    }
}
