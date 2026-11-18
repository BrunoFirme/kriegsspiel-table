using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class CrowdLayer
{
    const float MarkM = 2.2f;
    const double ActStride = 0.9;
    static readonly string[] Busy = [RigMotion.Melee, RigMotion.Cheer, RigMotion.Throw, RigMotion.Shove, RigMotion.Work];

    sealed class Track
    {
        public required CrowdTrack Run;
        public required string Order;
        public required Vec2 From;
        public CrowdTrack? Back;
    }

    readonly PieceLayer _pieces;
    readonly ShaderMaterial _markMaterial = new() { Shader = GD.Load<Shader>("res://shaders/crowd_square.gdshader") };
    readonly CrowdBatch _marks;
    readonly float[] _brass = CrowdMeshes.Palette(["#c9a227"]);
    readonly Dictionary<(int, int), Track> _tracks = new();
    readonly Dictionary<(int, int), string> _acts = new();
    long _ordersSigned;

    public readonly HashSet<(int Town, int Person)> Picked = new();
    public CrowdTool Tool { get; }
    public IReadOnlyList<(int Town, int Person, Vector3 At, CrowdWhere W)> Seen => _seen;
    public int Towns => _towns.Count;
    public CrowdTown Roster(int town) => _towns[town].Roster;
    public string? OrderOf(int town, int person) => _tracks.TryGetValue((town, person), out var t) ? t.Order : null;
    public int TownOf(string thing) => _towns.FindIndex(t => t.Roster.Thing == thing);

    const double FollowM = 5;

    void Restamp(GameState view, float upm)
    {
        foreach (var o in view.Crowds?.Orders ?? [])
        {
            if (o.Piece == null || o.Until != null || o.To.Count == 0 || view.Find(Layers.Umpire, o.Piece) is not { } piece) continue;
            if ((piece.Pos - o.To[0]).Length < FollowM * upm) continue;
            var moved = o.Clone();
            (moved.To, moved.Start) = ([piece.Pos], view.Clock.Seconds);
            _c.S.Do(new KTable.Core.Protocol.SetCrowdOrder { Order = moved });
        }
    }

    void Orders(GameState view, float upm)
    {
        _acts.Clear();
        var orders = view.Crowds?.Orders;
        var h = new HashCode();
        foreach (var o in orders ?? [])
        {
            h.Add(o.Id);
            h.Add(o.Start);
            h.Add(o.Until);
            h.Add(o.To.Count > 0 ? o.To[0] : default);
            h.Add(o.Count);
        }
        long signed = h.ToHashCode();
        if (signed == _ordersSigned) return;
        _ordersSigned = signed;
        _tracks.Clear();
        foreach (var o in (orders ?? []).OrderBy(o => o.Start).ThenBy(o => o.Id, StringComparer.Ordinal)) Compile(view, o, upm);
    }

    void Compile(GameState view, CrowdOrder o, float upm)
    {
        var members = Members(o);
        if (members.Count == 0) return;
        var starts = members.Select(m => Where(view, m.Town, m.Person, o.Start, upm)).ToList();
        var target = TargetOf(view, o, starts.Select(s => s.At).ToList(), upm);
        var routes = _towns[members[0].Town].Routes;
        var tree = o.Do is CrowdCommands.Halt or CrowdCommands.Home or CrowdCommands.Yield ? null : routes.TreeTo(target);
        for (int k = 0; k < members.Count; k++)
        {
            var (ti, i) = members[k];
            var aim = o.Do == CrowdCommands.Clear ? _towns[ti].Roster.Places[_towns[ti].Roster.Residents[i].Home].At : target;
            var way = o.Do == CrowdCommands.Clear ? null : tree;
            var run = CrowdOrders.Compile(o, k, members.Count, starts[k].At, starts[k].Heading, upm, aim, (a, b) => _towns[ti].Routes.Between(way, a, b));
            _tracks[(ti, i)] = new Track { Run = run, Order = o.Id, From = starts[k].At };
        }
    }

    public List<(int Town, int Person)> Members(CrowdOrder o)
    {
        var members = new List<(int, int)>();
        foreach (var (thing, ids) in o.Members)
        {
            int ti = TownOf(thing);
            if (ti < 0) continue;
            foreach (int id in ids)
                if (id >= 0 && id < _towns[ti].Roster.Residents.Count && _towns[ti].Shown[id]) members.Add((ti, id));
        }
        return members;
    }

    Vec2 TargetOf(GameState view, CrowdOrder o, List<Vec2> starts, float upm)
    {
        if (o.Piece != null && view.Find(Layers.Umpire, o.Piece) is { } piece) return piece.Pos;
        if (o.Against != null && view.Crowds?.Orders?.FirstOrDefault(x => x.Id == o.Against) is { } other && Members(other) is { Count: > 0 } them)
            return Centre(them.Select(m => Where(view, m.Town, m.Person, o.Start, upm).At).ToList());
        if (o.To.Count > 0) return o.Shape == CrowdShapes.Sides ? Centre(o.To) : o.To[0];
        return Centre(starts);
    }

    static Vec2 Centre(IReadOnlyList<Vec2> points) => points.Count == 0 ? default : new(points.Average(p => p.X), points.Average(p => p.Z));

    public (Vec2 At, double Heading) Where(GameState view, int ti, int i, long t, float upm)
    {
        if (Ordered(ti, i, t, upm, out var w, out _, out _)) return (w.At, w.Heading);
        var town = _towns[ti];
        var r = town.Roster.Residents[i];
        long day = (long)Math.Floor(t / (double)CrowdDay.Day);
        int second = (int)(t - day * CrowdDay.Day);
        var legs = CrowdDay.Plan(town.Roster, r, CrowdDay.KindOf(town.Roster.People, view.Clock.Cal.WeekDay(day * CrowdDay.Day)), day);
        foreach (var span in CrowdDay.Spans(town.Roster, r, legs, town.PathOf, upm))
            if (second >= span.Start && second < span.End && CrowdDay.At(town.Roster, r, span, second, upm, out var at)) return (at.At, at.Heading);
        int place = r.Home;
        foreach (var leg in legs)
            if (leg.Depart <= second) place = leg.To;
        return (town.Roster.Places[place].At, 0);
    }

    bool Ordered(int ti, int i, long t, float upm, out CrowdWhere w, out string? act, out bool seen)
    {
        (w, act, seen) = (default, null, false);
        if (!_tracks.TryGetValue((ti, i), out var tr) || t < tr.Run.Begins) return false;
        if (tr.Run.Covers(t))
        {
            seen = tr.Run.At(t, upm, out w, out act);
            return true;
        }
        tr.Back ??= Back(ti, i, tr, upm);
        if (!tr.Back.Covers(t)) return false;
        seen = tr.Back.At(t, upm, out w, out act);
        return true;
    }

    CrowdTrack Back(int ti, int i, Track tr, float upm)
    {
        long end = tr.Run.Ends;
        var from = tr.Run.Steps.Count > 0 && tr.Run.At(Math.Max(tr.Run.Steps[0].Start, end - 1), upm, out var last, out _) ? last.At : tr.From;
        var town = _towns[ti];
        var r = town.Roster.Residents[i];
        long day = (long)Math.Floor(end / (double)CrowdDay.Day);
        int second = (int)(end - day * CrowdDay.Day), place = r.Home;
        foreach (var leg in DayPlan(town, r, day))
            if (leg.Depart <= second) place = leg.To;
        var path = town.Routes.FromPoint(from, place);
        var back = new CrowdTrack { Begins = end };
        long arrive = end + (long)Math.Ceiling(path.Length / Math.Max(r.SpeedM * upm, 1e-9));
        back.Steps.Add(new CrowdStep(end, arrive, path, town.Roster.Places[place].At, 0, null, r.SpeedM));
        back.Ends = arrive;
        return back;
    }

    List<CrowdLeg> DayPlan(Town town, CrowdResident r, long day) =>
        CrowdDay.Plan(town.Roster, r, CrowdDay.KindOf(town.Roster.People, _c.S.View.Clock.Cal.WeekDay(day * CrowdDay.Day)), day);

    void Acting(int ti, int i, string? act, long t, int seed, float upm, ref CrowdWhere w)
    {
        if (act == null) return;
        _acts[(ti, i)] = act;
        if (!Busy.Contains(act)) return;
        double shake = CrowdOrders.Shake(act, t, seed) * upm;
        w = w with { Moving = true, Stride = t * ActStride * upm, At = new Vec2(w.At.X + shake, w.At.Z) };
    }
}
