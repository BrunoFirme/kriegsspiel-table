using KTable.Core.Model;
using KTable.Core.Protocol;
using MoonSharp.Interpreter;
using static KTable.Server.Scripting.LuaArgs;

namespace KTable.Server.Scripting;

internal sealed class ScriptClock(ScriptHost host)
{
    readonly ClockWatch _watch = Watching(host.Server.State.Clock);

    static ClockWatch Watching(GameClock clock)
    {
        var watch = new ClockWatch();
        watch.Rung(clock);
        return watch;
    }

    GameClock Clock => host.Server.State.Clock;

    string? Place => Clock.Apart != null && host._current is { Id.Length: > 0 } running && host.Server.State.Find(Layers.Umpire, running.Id) is { } piece
        ? PlaceClocks.At(host.Server.State, id => host.Server.Store?.Def(id), piece.Pos)
        : null;

    public GameClock Seen => Clock.AsOf(Place);

    DynValue T() => DynValue.NewTable(host.Vm);

    public bool Due => _watch.AnyDue(Clock);

    public void Forget()
    {
        _watch.Forget();
        _watch.Rung(Clock);
    }

    public static DynValue? Field(GameClock clock, string key) => key switch
    {
        "seconds" => N(clock.Now),
        "rate" => N(clock.Rate),
        "turn_name" => clock.Name is { } name ? S(name) : DynValue.Nil,
        "turn_length" => N(clock.TurnSeconds),
        _ => null,
    };

    public void Install(Table game)
    {
        game["step"] = Fn(a => Do(new StepClock { Seconds = Whole(Arg(a, 0)), Place = Place }));
        game["jump"] = Fn(a => Do(new SetClock { Turn = Clock.Turn, Seconds = Moment(Arg(a, 0)), TurnSeconds = Seen.TurnSeconds, Name = Clock.Name, Place = Place }));
        game["run"] = Fn(a => Do(new RunClock { Rate = Arg(a, 0).CastToNumber() ?? 1, Place = Place }));
        game["pause"] = Fn(_ => Do(new RunClock { Rate = 0, Place = Place }));
        game["timer"] =
            Fn(a => Do(new SetTimer { Name = Arg(a, 0).CastToString() ?? "", Due = Due_(Arg(a, 1)), Everyone = Arg(a, 2).Type == DataType.Table && Arg(a, 2).Table.Get("everyone").CastToBool(), Place = Place }));
        game["clear_timer"] = Fn(a => Do(new ClearTimer { Name = Arg(a, 0).CastToString() ?? "", Place = Place }));
        game["timers"] = Fn(_ => Timers());
    }

    DynValue Do(Command c)
    {
        host.Emit(c);
        return DynValue.Nil;
    }

    static long Whole(DynValue v) => v.CastToNumber() is { } n && double.IsFinite(n) ? (long)Math.Round(Math.Clamp(n, -CalendarDef.MostSeconds, CalendarDef.MostSeconds)) : 0;

    long Moment(DynValue v)
    {
        if (v.Type == DataType.Number) return Whole(v);
        return Clock.Cal.Read(v.CastToString()) ?? throw new ScriptRuntimeException("not a time in the game's calendar");
    }

    long Due_(DynValue v) => v.Type == DataType.Number ? Seen.Now + Whole(v) : Moment(v);

    DynValue Timers()
    {
        var list = T();
        var seen = Seen;
        foreach (var timer in (seen.Timers ?? new()).OrderBy(t => t.Due))
        {
            var t = T();
            t.Table["name"] = timer.Name;
            t.Table["due"] = Clock.Cal.Text(timer.Due);
            t.Table["seconds_left"] = timer.Due - seen.Now;
            t.Table["everyone"] = timer.Everyone;
            list.Table.Append(t);
        }
        return list;
    }

    void Tell(string hook, params DynValue[] args)
    {
        if (host.Global is { } global) host.Call(global, hook, args);
        foreach (var ps in host.PieceScripts.Values.ToList()) host.Call(ps, hook, args);
    }

    public void After(Command c)
    {
        if (c is Speak said)
        {
            ScriptSpeech.Heard(host, said);
            return;
        }
        if (c is AdvanceClock or SetClock { Place: null }) Tell("on_turn", N(Clock.Turn));
        Moved();
    }

    public void Moved()
    {
        long? before = _watch.Seen(null);
        var rung = _watch.Rung(Clock);
        if (before is { } was && was != Clock.Now) Tell("on_clock", N(was), N(Clock.Now));
        foreach (var (_, timer) in rung)
        {
            host.Server.Happened($"{timer.Name} is due", umpiresOnly: !timer.Everyone);
            Tell("on_timer", S(timer.Name));
        }
    }
}
