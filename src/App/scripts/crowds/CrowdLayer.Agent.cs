using System;
using System.Collections.Generic;
using System.Linq;
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
