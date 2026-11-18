using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class CrowdLayer
{
    public List<(int Town, int Person)> Group(string id)
    {
        if (_c.S.View.Crowds?.Orders?.FirstOrDefault(o => o.Id == id) is { } o) return Members(o);
        int ti = TownOf(id);
        return ti < 0 ? [] : Enumerable.Range(0, _towns[ti].Shown.Length).Where(i => _towns[ti].Shown[i]).Select(i => (ti, i)).ToList();
    }

    public (string Shape, double WidthM)? Drilled(string command, IReadOnlyCollection<(int Town, int Person)> people) =>
        command == CrowdCommands.FireAt && people.Count > 1 && people.All(p => _towns[p.Town].Roster.Residents[p.Person].Entry.Calling == CrowdBaseline.Soldier)
            ? (CrowdShapes.Line, CrowdOrders.TwoRanksM(people.Count))
            : null;

    public Dictionary<string, List<int>> MembersOf(IReadOnlyCollection<(int Town, int Person)> people)
    {
        var members = people.GroupBy(p => p.Town).ToDictionary(g => _towns[g.Key].Roster.Thing, g => g.Select(p => p.Person).ToList());
        var keys = people.Select(p => CrowdPiece.Key(_towns[p.Town].Roster.Thing, p.Person)).ToHashSet();
        foreach (var key in _c.S.View.Crowds?.HeldBy(keys) ?? [])
        {
            if (CrowdPiece.Parse(key) is not { } held) continue;
            if (!members.TryGetValue(held.Thing, out var ids)) members[held.Thing] = ids = new();
            if (!ids.Contains(held.Person)) ids.Add(held.Person);
        }
        foreach (var ids in members.Values) ids.Sort();
        return members;
    }

    public string? TownNear(Vec2 at, double reach)
    {
        var near = _towns.Where(t => !t.Roster.Made).Select(t => (t.Roster.Thing, Apart: t.Roster.Places.Min(p => (p.At - at).Length))).Where(t => t.Apart <= reach);
        return near.OrderBy(t => t.Apart).Select(t => t.Thing).FirstOrDefault();
    }

    public List<Command> Settling(IReadOnlyCollection<string> made, string town)
    {
        var view = _c.S.View;
        long now = view.Clock.Seconds;
        var items = new List<Command>();
        foreach (var m in view.Crowds?.Made?.Where(m => made.Contains(m.Id)) ?? [])
        {
            var settled = m.Clone();
            settled.Settled = town;
            items.Add(new SetMadeCrowd { Crowd = settled });
        }
        foreach (var o in view.Crowds?.Orders?.Where(o => o.Members.Keys.All(made.Contains) && (o.Until is not { } u || u > now)) ?? [])
        {
            var ended = o.Clone();
            ended.Until = Math.Max(ended.Start, now);
            items.Add(new SetCrowdOrder { Order = ended });
        }
        return items;
    }

    const float FollowSlack = 1e-3f;
    (string Thing, int Person)? _followed;
    Vector3? _followSet;

    public bool Following => _followed != null;

    public void Follow(int ti, int i)
    {
        (_followed, _followSet) = ((_towns[ti].Roster.Thing, i), null);
        _c.Say($"Following {_towns[ti].Roster.Residents[i].Entry.Name} · a pan or Esc stops");
    }

    public void Unfollow() => _followed = null;

    void Follow()
    {
        if (_followed is not { } f) return;
        var rig = _c.Rig;
        if (_followSet is { } set && (rig.Target - set).Length() > FollowSlack)
        {
            _followed = null;
            return;
        }
        int ti = TownOf(f.Thing), k = _seen.FindIndex(s => s.Town == ti && s.Person == f.Person);
        if (k < 0) return;
        var at = _seen[k].At;
        rig.PanWorld(new Vector3(at.X - rig.Target.X, 0, at.Z - rig.Target.Z));
        _followSet = rig.Target;
    }

    public Vec2 FallenAt(int ti, int i) => Fallen(_c.S.View, ti, i, (float)(_c.S.View.Calibration.UnitsPerMeter ?? 1)).At;

    public (int Shown, int Out) Counts(int ti) => (_towns[ti].Shown.Count(s => s), _seen.Count(s => s.Town == ti));

    public bool Arrived(CrowdOrder o, long now, out int count)
    {
        var members = Members(o);
        count = members.Count;
        return members.Count > 0 && members.All(m => _tracks.TryGetValue(m, out var tr) && tr.Order == o.Id && (tr.Run.Steps.Count == 0 || tr.Run.Steps[0].Path == null || now >= tr.Run.Steps[0].End));
    }

    public string? Happened(string? id)
    {
        var view = _c.S.View;
        long now = view.Clock.Seconds;
        if (view.Layers.Values.SelectMany(l => l).FirstOrDefault(p => p.Id == id) is { } near)
        {
            double reach = Earshot.Reach(Earshot.Say, view.Calibration);
            int heard = _seen.Count(s => (Coords.FromWorld(s.At) - near.Pos).Length <= reach);
            return heard > 0 ? $"{heard} crowd people within earshot of {id}" : null;
        }
        if (view.Crowds?.Orders?.FirstOrDefault(o => o.Id == id) is not { } o) return $"No order {id}";
        if (o.Until is { } until && now >= until) return $"{o.Name}: the order ended";
        return Arrived(o, now, out int count) ? $"{o.Name}: all {count} are there" : null;
    }

    public (CrowdTown Town, int Person)? PersonAt(Vec2 at, double reach)
    {
        (int Town, int Person, double Apart)? best = null;
        foreach (var (ti, i, spot, _) in _seen)
        {
            double apart = (Coords.FromWorld(spot) - at).Length;
            if (apart <= reach && (best == null || apart < best.Value.Apart)) best = (ti, i, apart);
        }
        return best is { } b ? (_towns[b.Town].Roster, b.Person) : null;
    }
}
