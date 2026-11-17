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
        ? PlaceClocks.At(host.Server.State, Def, piece.Pos)
        : null;

    PieceDef? Def(string id) => host.Server.Store?.Def(id);

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
        game["moons"] = Fn(_ => Moons());
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

    DynValue Moons()
    {
        var list = T();
        foreach (var (name, phase, lit) in MoonPhases.Of(Clock.Cal, Seen.Now))
        {
            var t = T();
            t.Table["name"] = name;
            t.Table["phase"] = phase;
            t.Table["lit"] = lit;
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

    void TellIn(string? place, Func<string, string?> placeOf, string hook, params DynValue[] args)
    {
        if (host.Global is { } global) host.Call(global, hook, args);
        foreach (var ps in host.PieceScripts.Values.ToList())
            if (placeOf(ps.Id) == place) host.Call(ps, hook, args);
    }

    Func<string, string?> PlacesOfPieces()
    {
        var apart = Clock.Apart is { Count: > 0 } ? PlaceClocks.Apart(host.Server.State, Def) : new();
        var known = new Dictionary<string, string?>();
        return id =>
        {
            if (apart.Count == 0) return null;
            if (known.TryGetValue(id, out var place)) return place;
            place = host.Server.State.Find(Layers.Umpire, id) is { } piece ? PlaceClocks.At(apart, piece.Pos)?.Id : null;
            known[id] = place;
            return place;
        };
    }

    static DynValue Named(string? place) => place is { } id ? S(id) : DynValue.Nil;

    public void Moved()
    {
        var before = Clock.All().Select(c => (c.Place, Was: _watch.Seen(c.Place))).ToList();
        var rung = _watch.Rung(Clock);
        var moved = before.Where(b => b.Was is { } was && _watch.Seen(b.Place) is { } now && now != was).ToList();
        if (moved.Count == 0 && rung.Count == 0) return;
        var placeOf = PlacesOfPieces();
        foreach (var (place, was) in moved) TellIn(place, placeOf, "on_clock", N(was!.Value), N(_watch.Seen(place)!.Value), Named(place));
        foreach (var (place, timer) in rung)
        {
            host.Server.Happened($"{timer.Name} is due", umpiresOnly: !timer.Everyone);
            TellIn(place, placeOf, "on_timer", S(timer.Name), Named(place));
        }
    }
}
