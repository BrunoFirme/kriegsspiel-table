using System.Text.Json;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Agent;

public sealed class ClockTools
{
    readonly TableTools _t;

    public ClockTools(TableTools t) => _t = t;

    public IEnumerable<AgentTool> Tools()
    {
        yield return new AgentTool("end_turn", "Ends the turn, as Next turn does.", Args.Schema(), EndTurn);
        yield return new AgentTool("set_clock",
            "Sets the game clock: the turn and its name, the time, how long a turn lasts and the rate the clock runs at; step moves the time and ends no turn.",
            Args.Schema(new Arg("turn", "number", "turn number"), new Arg("turn_name", "string", "names the turn; empty removes the name"),
                new Arg("time", "string", "yyyy-mm-ddThh:mm, a minus before a year before 1"), new Arg("minutes_per_turn", "number", "60 an hour, 1440 a day, 0 none"),
                new Arg("step", "number", "seconds to move the time, negative to go back"), new Arg("rate", "number", "game seconds a second; 0 pauses")), SetClockTool);
        yield return new AgentTool("set_timer",
            "Sets, moves or removes a timer on the game clock; the Chat log says when it is due.",
            Args.Schema(new Arg("name", "string", "its name", true), new Arg("when", "string", "14:00, yyyy-mm-ddThh:mm, or from now: 2 h, 90 min"),
                new Arg("everyone", "boolean", "players see it"), new Arg("remove", "boolean", "removes it")), SetTimerTool);
    }

    async Task<ToolResult> EndTurn(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        int turn = s.View.Clock.Turn;
        s.Do(new AdvanceClock());
        if (!await _t.Until(() => s.View.Clock.Turn != turn, 5)) return ToolResult.Fail("The turn did not end (the clock may not be allowed to go on).");
        _t.Log($"ended turn {turn}");
        return new ToolResult($"Turn {s.View.Clock.Turn} · {s.View.Clock.GameTime}.");
    }

    async Task<ToolResult> SetClockTool(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var c = s.View.Clock;
        if ((a.Str("time") is { } time ? c.Cal.Read(time) : c.Now) is not { } seconds) return ToolResult.Fail("time is yyyy-mm-ddThh:mm in the game's calendar.");
        const int mostMinutes = ClockTime.MostTurnSeconds / CalendarDef.SecondsInMinute;
        if (a.Num("minutes_per_turn") is < 0 or > mostMinutes) return ToolResult.Fail($"minutes_per_turn is 0 to {mostMinutes}.");
        if (a.Num("rate") is < 0 or > ClockTime.MostRate) return ToolResult.Fail($"rate is 0 to {ClockTime.MostRate:0}.");
        int turnSeconds = a.Num("minutes_per_turn") is { } minutes ? (int)minutes * CalendarDef.SecondsInMinute : c.TurnSeconds;
        var name = a.Str("turn_name") ?? c.Name;
        if (a.Num("turn") != null || a.Str("time") != null || a.Num("minutes_per_turn") != null || a.Str("turn_name") != null)
            s.Do(new SetClock { Turn = (int)(a.Num("turn") ?? c.Turn), Seconds = seconds, TurnSeconds = turnSeconds, Name = name });
        if (a.Num("step") is { } step && Math.Round(step) != 0) s.Do(new StepClock { Seconds = (long)Math.Round(step) });
        if (a.Num("rate") is { } rate) s.Do(new RunClock { Rate = rate });
        await _t.Landed(s);
        _t.Log("set the clock");
        return new ToolResult(Told(s.View.Clock));
    }

    async Task<ToolResult> SetTimerTool(JsonElement a)
    {
        if (_t.Open(out var why) is not { } s) return why!;
        if (!_t.CanChange(s, out why)) return why!;
        var c = s.View.Clock;
        if (a.Str("name")?.Trim() is not { Length: > 0 and <= GameTimer.MostNameLength } name) return ToolResult.Fail($"name is 1 to {GameTimer.MostNameLength} letters.");
        if (a.Flag("remove") == true)
        {
            if (c.Timer(name) == null) return ToolResult.Fail("There is no timer of that name.");
            s.Do(new ClearTimer { Name = name });
        }
        else
        {
            if (ClockWords.When(c.Cal, c.Now, a.Str("when")) is not { } due) return ToolResult.Fail("when is a time of day (14:00), a date and time (yyyy-mm-ddThh:mm) or a span from now (2 h, 90 min).");
            s.Do(new SetTimer { Name = name, Due = due, Everyone = a.Flag("everyone") == true });
        }
        await _t.Landed(s);
        _t.Log("set a timer");
        return new ToolResult(Told(s.View.Clock));
    }

    static string Told(GameClock c) => c.Name != null || c.Timers != null ? Hearsay.Marked(Hearsay.Held(ClockLine(c))) : ClockLine(c);

    internal static string ClockLine(GameClock c)
    {
        string turn = c.Name is { } named ? $"Turn {c.Turn} ({named})" : $"Turn {c.Turn}";
        string timers = c.Timers is { Count: > 0 } set ? " Timers: " + string.Join(", ", set.OrderBy(t => t.Due).Select(t => $"{t.Name} {c.Cal.Text(t.Due)}{(t.Due <= c.Now ? " (due)" : "")}")) + "." : "";
        return $"{turn} · {c.GameTime} · {GameClock.StepText(c.TurnSeconds)} a turn · {(c.Runs ? "running " + ClockWords.Rate(c.Rate) : "paused")}.{timers}";
    }
}
