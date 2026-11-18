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

    public IReadOnlyList<(Vector3 At, int Town, int Rig)> RigsSeen => _rigsSeen.Select(r => (r.At, r.Ti, r.K)).ToList();

    int RigCount(CrowdOrder o) => o.Members.Values.Sum(ids => ids.Count(id => RigOf(id) != null));

    public void ClearPicks()
    {
        Picked.Clear();
        PickedRigs.Clear();
    }

    int DayKey => (int)((_day % RigDays + RigDays) % RigDays);

    int RigMember(int k) => RigBase + DayKey * RigStride + k;

    int? RigOf(int member) => member >= RigBase && (member - RigBase) / RigStride == DayKey ? (member - RigBase) % RigStride : null;

    public void AddRigs(Dictionary<string, List<int>> members)
    {
        foreach (var (ti, k) in PickedRigs.Where(r => r.Town < _towns.Count))
        {
            string thing = _towns[ti].Roster.Thing;
            if (!members.TryGetValue(thing, out var ids)) members[thing] = ids = new();
            ids.Add(RigMember(k));
            ids.Sort();
        }
    }

    public int AddRigsNear(Dictionary<string, List<int>> members, Vec2 at, double reach)
    {
        var centre = Coords.ToWorld(at, 0);
        int added = 0;
        foreach (var r in _rigsSeen.Where(r => new Vector2(r.At.X - centre.X, r.At.Z - centre.Z).Length() <= reach).DistinctBy(r => (r.Ti, r.K)))
        {
            string thing = _towns[r.Ti].Roster.Thing;
            if (!members.TryGetValue(thing, out var ids)) members[thing] = ids = new();
            ids.Add(RigMember(r.K));
            ids.Sort();
            added++;
        }
        return added;
    }

    List<(int Town, int Rig)> RigMembers(CrowdOrder o, float upm, ref bool waiting)
    {
        var rigs = new List<(int, int)>();
        foreach (var (thing, ids) in o.Members)
        {
            int ti = TownOf(thing);
            if (ti < 0) continue;
            foreach (int id in ids)
                if (RigOf(id) is { } k && Ready(ti, k, upm, ref waiting)) rigs.Add((ti, k));
        }
        return rigs;
    }

    bool Ready(int ti, int k, float upm, ref bool waiting)
    {
        var t = _towns[ti];
        if (t.Roster.Made || t.Stands != null || t.Roster.People.Traffic is not { Count: > 0 } rigs) return false;
        t.Trips ??= CrowdTraffic.Plan(t.Roster, t.Kind, _day, t.Shown.Count(s => s));
        t.OnWays ??= new OnWay?[t.Trips.Count];
        if (k >= t.Trips.Count) return false;
        if (t.OnWays[k] is { } made) return made.Path != null;
        var trip = t.Trips[k];
        if (!t.Routes.Way(trip.From, trip.To, TravellerOf(t.Routes, rigs[trip.Rig]), out var path))
        {
            waiting = true;
            return false;
        }
        return (t.OnWays[k] = Rig(t, trip, rigs[trip.Rig], path, upm, k, _c.S.View.Clock.Seconds)).Path != null;
    }

    (Vec2 At, double Heading) RigStart(int ti, int k, long when, float upm)
    {
        var t = _towns[ti];
        var trip = t.Trips![k];
        var made = t.OnWays![k]!;
        var rig = t.Roster.People.Traffic![trip.Rig];
        int second = (int)(when - _day * CrowdDay.Day);
        double gone = Math.Clamp((second - trip.Depart) * rig.SpeedM, 0, made.LengthM);
        return made.Path!.At(Math.Max(gone - made.FrontM, 0) * upm);
    }

    void CompileRigs(CrowdOrder o, List<(int Town, int Rig)> rigs, int first, int ranks, Vec2 target, WayTree? tree, float upm)
    {
        for (int j = 0; j < rigs.Count; j++)
        {
            var (ti, k) = rigs[j];
            var (from, heading) = RigStart(ti, k, o.Start, upm);
            var run = CrowdOrders.Compile(o, first + j, ranks, from, heading, upm, target, (a, b) => _towns[ti].Routes.Between(tree, a, b));
            _rigTracks[(ti, k)] = new Track { Run = run, Order = o.Id, From = from };
        }
    }

    void Held(Town t, int ti, int k, TrafficRig rig, OnWay made, Track held, long now, float upm, Vector3 eye, bool flat, float figures)
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
            Part(t, ti, k, rig, made, j, point, w.Heading, t.Routes.Height(point), went + p.Z - p.Lag, moving, t.Trips![k].To, upm, eye, flat, figures);
        }
    }
}
