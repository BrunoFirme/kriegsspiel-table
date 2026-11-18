using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class ClockBeat : HudPart
{
    const long LightEvery = 60;
    const ulong LookEveryMs = 200;

    readonly TopBar _top;
    readonly ClockWatch _watch = new();
    readonly OrderWatch _arrivals = new();
    long _shown = long.MinValue, _lit = long.MinValue, _seq = long.MinValue;
    ulong _looked;
    List<MapPlace> _apart = new();
    string _keys = "";

    public Action<string?, string?>? PlaceChanged;

    Vec2 Standing()
    {
        if (!S.IsUmpire && T.Avatars.Bodies.FirstOrDefault(b => b.Name == S.Me.Name).Piece is { } worn && S.View.Find(Layers.Umpire, worn) is { } body) return body.Pos;
        return Coords.FromWorld(T.Rig.Target);
    }

    void Place()
    {
        var clock = S.View.Clock;
        string keys = clock.Apart == null ? "" : string.Join('|', clock.Apart.Keys);
        if (keys != _keys || keys.Length > 0 && S.View.LastSeq != _seq)
        {
            (_keys, _seq) = (keys, S.View.LastSeq);
            _apart = PlaceClocks.Apart(S.View, S.Def);
        }
        var here = _apart.Count == 0 ? null : PlaceClocks.At(_apart, Standing());
        if (here?.Id == _top.ClockPlace) return;
        (_top.ClockPlace, _top.ClockPlaceName, T.Stage.ClockPlace) = (here?.Id, here?.Name, here?.Id);
        _shown = long.MinValue;
        _lit = long.MinValue;
        PlaceChanged?.Invoke(here?.Id, here?.Name);
        if (S.View.Lighting.FromClock) T.Stage.ApplyLighting(T.Stage.LightNow());
    }

    public ClockBeat(HudContext ctx, TopBar top) : base(ctx)
    {
        _top = top;
    }

    public void Tick()
    {
        ulong at = Time.GetTicksMsec();
        if (at - _looked < LookEveryMs) return;
        _looked = at;
        Place();
        var clock = S.View.Clock;
        foreach (var (_, timer) in _watch.Rung(clock))
        {
            Say($"{timer.Name}: due");
            Ctx.LogAs($"[b]{Fmt.Bb(timer.Name)}[/b] is due · {clock.Cal.Words(timer.Due)}", LogKind.World);
        }
        foreach (var p in _arrivals.Arrived(S.View))
            if (S.IsUmpire) Ctx.LogAs($"[b]{Fmt.Bb(p.Label ?? S.Def(p.Def)?.Name ?? p.Id)}[/b] arrived · {clock.Cal.Words((long)System.Math.Ceiling(p.Timed!.Arrives))}", LogKind.World);
        long now = clock.Of(_top.ClockPlace).Now;
        if (now == _shown) return;
        _shown = now;
        _top.ShowClock();
        if (!clock.Of(_top.ClockPlace).Runs || !S.View.Lighting.FromClock || System.Math.Abs(now - _lit) < LightEvery) return;
        _lit = now;
        T.Stage.ApplyLighting(T.Stage.LightNow());
    }
}
