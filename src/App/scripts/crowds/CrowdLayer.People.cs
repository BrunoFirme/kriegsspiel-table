using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class CrowdLayer
{
    const double LetGoAfter = 2, DimShare = 0.4;

    readonly HashSet<(int Town, int Person)> _asPieces = new();
    readonly Dictionary<string, double> _unworn = new();
    readonly Dictionary<(int, int), CrowdWhere> _fallen = new();
    readonly HashSet<string> _leaving = new();
    readonly Dictionary<(int, int), CrowdFate> _fates = new();
    bool _fatesDue = true;
    readonly Dictionary<string, CrowdLook> _dressed = new();
    bool _dressDue = true;

    public Func<string, bool> Worn { get; set; } = _ => false;

    void PeopleAsPieces(GameState view)
    {
        _asPieces.Clear();
        foreach (var p in view.Layer(Layers.Umpire))
            if (p.Crowd != null && CrowdPiece.Parse(p.Crowd) is { } piece && TownOf(piece.Thing) is >= 0 and var town) _asPieces.Add((town, piece.Person));
    }

    void Pieced(GameState view, double every)
    {
        PeopleAsPieces(view);
        foreach (var p in view.Layer(Layers.Umpire).Where(p => p.Crowd != null).ToList())
        {
            if (!_c.S.IsHost || view.Crowds?.FateOf(p.Crowd!, view.Clock.Seconds) == CrowdFates.Kept || Worn(p.Id))
            {
                _unworn.Remove(p.Id);
                continue;
            }
            _unworn[p.Id] = _unworn.GetValueOrDefault(p.Id) + every;
            if (_unworn[p.Id] < LetGoAfter || CrowdPiece.Parse(p.Crowd) is not { } who) continue;
            _unworn.Remove(p.Id);
            var back = new CrowdOrder
            {
                Name = "Back to their day", Members = new() { [who.Thing] = [who.Person] }, Do = CrowdCommands.Home, From = p.Pos, Start = view.Clock.Seconds,
            };
            _c.S.Do(new Batch { Items = [new DeletePiece { Layer = Layers.Umpire, Id = p.Id }, new SetCrowdOrder { Order = back }] });
        }
    }

    void Departed(GameState view)
    {
        if (view.Crowds?.Made is not { Count: > 0 } made) return;
        long now = view.Clock.Seconds;
        foreach (var m in made)
        {
            int ti = TownOf(m.Id);
            if (ti < 0 || _leaving.Contains(m.Id)) continue;
            bool Left(int i) => FateAt(ti, i, now) == CrowdFates.Gone || _tracks.TryGetValue((ti, i), out var tr) && tr.Run.Steps.Count > 0 && now >= tr.Run.Steps[0].End
                && view.Crowds.Orders?.FirstOrDefault(o => o.Id == tr.Order)?.Do == CrowdCommands.Leave;
            if (!Enumerable.Range(0, _towns[ti].Roster.Residents.Count).All(Left)) continue;
            _leaving.Add(m.Id);
            _c.S.Do(new Batch { Items = Unmade(view, [m.Id]) });
        }
    }

    public static List<Command> Unmade(GameState view, IReadOnlyCollection<string> ids)
    {
        var items = new List<Command>();
        foreach (var m in view.Crowds?.Made?.Where(m => ids.Contains(m.Id)) ?? []) items.Add(new SetMadeCrowd { Crowd = m.Clone(), Remove = true });
        foreach (var o in view.Crowds?.Orders?.Where(o => o.Members.Keys.All(ids.Contains)) ?? []) items.Add(new SetCrowdOrder { Order = new CrowdOrder { Id = o.Id }, Remove = true });
        var fates = view.Crowds?.Fates?.Keys.Where(k => CrowdPiece.Parse(k) is { } who && ids.Contains(who.Thing)).Take(SetCrowdFates.Most).ToDictionary(k => k, _ => (CrowdFate?)null);
        if (fates is { Count: > 0 }) items.Add(new SetCrowdFates { Fates = fates });
        return items;
    }

    void Fated(GameState view)
    {
        _fatesDue = false;
        _fates.Clear();
        _fallen.Clear();
        foreach (var (key, fate) in view.Crowds?.Fates ?? [])
            if (CrowdPiece.Parse(key) is { } who && TownOf(who.Thing) is >= 0 and var ti && who.Person < _towns[ti].Roster.Residents.Count) _fates[(ti, who.Person)] = fate;
    }

    void Redress(GameState view)
    {
        _dressDue = false;
        _dressed.Clear();
        foreach (var (key, look) in view.Crowds?.Dressed ?? []) _dressed[key] = look.Clone();
        foreach (var t in _towns)
        {
            Array.Clear(t.Drawn);
            Array.Clear(t.LitDrawn);
            Array.Clear(t.LitLooks);
        }
        _near.Clear();
    }

    readonly List<int> _visit = new();
    int _visitStamp;

    List<int> Visit(Town t, int ti, int second, long day, float upm)
    {
        var list = _visit;
        list.Clear();
        int count = t.Roster.Residents.Count;
        if (t.Stands != null)
        {
            for (int i = 0; i < count; i++) list.Add(i);
            return list;
        }
        var hours = t.Hours ??= Hours(t, day, upm);
        var marks = t.Marks ??= new int[count];
        var specials = t.Specials ??= new int[count];
        int stamp = ++_visitStamp;
        foreach (int i in hours[Math.Clamp(second / CalendarDef.SecondsInHour, 0, hours.Length - 1)])
        {
            marks[i] = stamp;
            list.Add(i);
        }
        if (_tracks.Count > 0) Also(_tracks.Keys);
        if (_fates.Count > 0) Also(_fates.Keys);
        return list;

        void Also(IEnumerable<(int Town, int Person)> keys)
        {
            foreach (var (town, i) in keys)
            {
                if (town != ti || i >= count) continue;
                specials[i] = stamp;
                if (marks[i] == stamp) continue;
                marks[i] = stamp;
                list.Add(i);
            }
        }
    }

    static List<int>[] Hours(Town t, long day, float upm)
    {
        var roster = t.Roster;
        var hours = new List<int>[CrowdDay.Day / CalendarDef.SecondsInHour];
        for (int h = 0; h < hours.Length; h++) hours[h] = new List<int>();
        for (int i = 0; i < roster.Residents.Count; i++)
        {
            if (!t.Shown[i]) continue;
            var r = roster.Residents[i];
            int last = -1;
            foreach (var span in t.Spans[i] ??= CrowdDay.Spans(roster, r, t.Plans[i] ??= CrowdDay.Plan(roster, r, t.Kind, day), t.PathOf, upm))
                for (int h = Math.Max(span.Start / CalendarDef.SecondsInHour, last + 1); h <= Math.Min((span.End - 1) / CalendarDef.SecondsInHour, hours.Length - 1); h++)
                {
                    hours[h].Add(i);
                    last = h;
                }
        }
        return hours;
    }

    bool Special(Town t, int i) => t.Stands != null || t.Specials?[i] == _visitStamp;

    string? FateAt(int ti, int i, long now) => _fates.TryGetValue((ti, i), out var f) && f.Holds(now) ? f.State : null;

    bool Dragged(int ti, int i) => _tracks.TryGetValue((ti, i), out var tr) && _fates.TryGetValue((ti, i), out var f) && tr.Run.Begins >= f.At;

    CrowdWhere Fallen(GameState view, int ti, int i, float upm)
    {
        if (_fallen.TryGetValue((ti, i), out var known)) return known;
        long at = _fates.TryGetValue((ti, i), out var fate) ? fate.At : view.Clock.Seconds;
        var (spot, heading) = Where(view, ti, i, at, upm);
        return _fallen[(ti, i)] = new CrowdWhere(spot, heading, 0, false, -1, _towns[ti].Routes.Height(spot));
    }

    static float Dimmed(float packed)
    {
        int v = (int)packed, r = (int)((v >> 16 & 255) * DimShare), g = (int)((v >> 8 & 255) * DimShare), b = (int)((v & 255) * DimShare);
        return r * 65536 + g * 256 + b;
    }

    public float HeightAt(int ti, Vec2 at) => _towns[ti].Routes.Height(at);

    public (CrowdLook Look, Vec2 At, double Heading)? Person(int ti, int i)
    {
        var view = _c.S.View;
        float upm = (float)(view.Calibration.UnitsPerMeter ?? 1);
        var t = _towns[ti];
        var look = LookOf(ti, i);
        var (at, heading) = Where(view, ti, i, view.Clock.Seconds, upm);
        return (look, at, heading);
    }
}
