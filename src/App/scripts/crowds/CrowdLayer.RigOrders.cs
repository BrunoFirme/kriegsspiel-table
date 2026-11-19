using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed partial class CrowdLayer
{
    const int RigBase = 1 << 30, RigDays = 1000, RigStride = 100_000;

    public readonly HashSet<(int Town, int Rig)> PickedRigs = new();
    readonly Dictionary<(int, int), Track> _rigTracks = new();
    readonly Dictionary<(int Town, long Day), (List<TrafficTrip> Trips, OnWay?[] Ways)> _pastPlans = new();

    public IReadOnlyList<(Vector3 At, int Town, int Rig)> RigsSeen => _rigsSeen.Select(r => (r.At, r.Ti, r.Member)).ToList();

    static int RigCount(CrowdOrder o) => o.Members.Values.Sum(ids => ids.Count(id => id >= RigBase));

    public void ClearPicks()
    {
        Picked.Clear();
        PickedRigs.Clear();
    }

    static int Key(long day) => (int)((day % RigDays + RigDays) % RigDays);

    static int RigMember(long day, int k) => RigBase + Key(day) * RigStride + k;

    (long Day, int K)? RigOf(int member) =>
        member < RigBase ? null : (_day - (Key(_day) - (member - RigBase) / RigStride + RigDays) % RigDays, (member - RigBase) % RigStride);

    public void AddRigs(Dictionary<string, List<int>> members)
    {
        foreach (var (ti, member) in PickedRigs.Where(r => r.Town < _towns.Count)) Join(members, ti, member);
    }

    void Join(Dictionary<string, List<int>> members, int ti, int member)
    {
        string thing = _towns[ti].Roster.Thing;
        if (!members.TryGetValue(thing, out var ids)) members[thing] = ids = new();
        if (!ids.Contains(member)) ids.Add(member);
        ids.Sort();
    }

    public int AddRigsNear(Dictionary<string, List<int>> members, Vec2 at, double reach)
    {
        var centre = Coords.ToWorld(at, 0);
        var near = _rigsSeen.Where(r => new Vector2(r.At.X - centre.X, r.At.Z - centre.Z).Length() <= reach).DistinctBy(r => (r.Ti, r.Member)).ToList();
        foreach (var r in near) Join(members, r.Ti, r.Member);
        return near.Count;
    }

    (List<TrafficTrip> Trips, OnWay?[] Ways)? PlanOf(int ti, long day)
    {
        var t = _towns[ti];
        if (t.Roster.Made || t.Stands != null || t.Roster.People.Traffic is not { Count: > 0 }) return null;
        if (day == _day)
        {
            t.Trips ??= CrowdTraffic.Plan(t.Roster, t.Kind, _day, Riders(t));
            t.OnWays ??= new OnWay?[t.Trips.Count];
            return (t.Trips, t.OnWays);
        }
        if (_pastPlans.TryGetValue((ti, day), out var kept)) return kept;
        var trips = CrowdTraffic.Plan(t.Roster, DayOf(t.Roster, day), day, Riders(t));
        return _pastPlans[(ti, day)] = (trips, new OnWay?[trips.Count]);
    }

    List<(int Town, int Member, long Day, int K)> RigMembers(CrowdOrder o, float upm, ref bool waiting)
    {
        var rigs = new List<(int, int, long, int)>();
        foreach (var (thing, ids) in o.Members)
        {
            int ti = TownOf(thing);
            if (ti < 0) continue;
            foreach (int id in ids)
                if (RigOf(id) is { } at && Ready(ti, at.Day, at.K, upm, ref waiting)) rigs.Add((ti, id, at.Day, at.K));
        }
        return rigs;
    }

    bool Ready(int ti, long day, int k, float upm, ref bool waiting)
    {
        if (PlanOf(ti, day) is not { } plan || k >= plan.Trips.Count) return false;
        if (plan.Ways[k] is { } made) return made.Path != null;
        var t = _towns[ti];
        var trip = plan.Trips[k];
        var rig = t.Roster.People.Traffic![trip.Rig];
        if (!t.Routes.Way(trip.From, trip.To, TravellerOf(t.Routes, rig), out var path))
        {
            waiting = true;
            return false;
        }
        return (plan.Ways[k] = Rig(t, trip, rig, path, upm, k, _c.S.View.Clock.Seconds)).Path != null;
    }

    (Vec2 At, double Heading) RigStart(int ti, long day, int k, long when, float upm)
    {
        var plan = PlanOf(ti, day)!.Value;
        var trip = plan.Trips[k];
        var made = plan.Ways[k]!;
        var rig = _towns[ti].Roster.People.Traffic![trip.Rig];
        double second = when - day * CrowdDay.Day;
        double gone = Math.Clamp((second - trip.Depart) * rig.SpeedM, 0, made.LengthM);
        return made.Path!.At(Math.Max(gone - made.FrontM, 0) * upm);
    }

    void CompileRigs(CrowdOrder o, List<(int Town, int Member, long Day, int K)> rigs, int first, int ranks, Vec2 target, WayTree? tree, float upm)
    {
        for (int j = 0; j < rigs.Count; j++)
        {
            var (ti, member, day, k) = rigs[j];
            var (from, heading) = RigStart(ti, day, k, o.Start, upm);
            var run = CrowdOrders.Compile(o, first + j, ranks, from, heading, upm, target, (a, b) => _towns[ti].Routes.Between(tree, a, b));
            _rigTracks[(ti, member)] = new Track { Run = run, Order = o.Id, From = from };
        }
    }

    void ForgetPastPlans()
    {
        foreach (var key in _pastPlans.Keys.Where(p => !_rigTracks.Keys.Any(r => r.Item1 == p.Town && RigOf(r.Item2)?.Day == p.Day)).ToList()) _pastPlans.Remove(key);
    }

    void HeldFromPastDays(int ti, double now, float upm, Vector3 eye, bool flat, float figures)
    {
        foreach (var ((town, member), held) in _rigTracks)
        {
            if (town != ti || RigOf(member) is not { } at || at.Day == _day || now < held.Run.Begins) continue;
            if (PlanOf(ti, at.Day) is not { } plan || at.K >= plan.Trips.Count || plan.Ways[at.K] is not { Path: not null } made) continue;
            var trip = plan.Trips[at.K];
            Rigs++;
            Held(_towns[ti], ti, member, trip.To, _towns[ti].Roster.People.Traffic![trip.Rig], made, held, now, upm, eye, flat, figures);
        }
    }

    void Held(Town t, int ti, int member, int to, TrafficRig rig, OnWay made, Track held, double now, float upm, Vector3 eye, bool flat, float figures)
    {
        var run = held.Run;
        if (!run.At(Math.Clamp(now, run.Begins, run.Ends - 1), upm, out var w, out _)) return;
        bool moving = run.Covers(now) && w.Moving;
        double sin = Math.Sin(w.Heading), cos = Math.Cos(w.Heading);
        double went = w.Stride / Math.Max(upm, 1e-6);
        for (int j = 0; j < rig.Parts.Count; j++)
        {
            var p = rig.Parts[j];
            double d = (p.Z - p.Lag) * upm;
            var point = new Vec2(w.At.X + sin * d, w.At.Z + cos * d);
            Part(t, ti, member, rig, made, j, point, w.Heading, t.Routes.Height(point), went + p.Z - p.Lag, moving, to, upm, eye, flat, figures);
        }
    }
}
