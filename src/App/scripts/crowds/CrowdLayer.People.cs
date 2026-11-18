using System;
using System.Collections.Generic;
using System.Linq;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed partial class CrowdLayer
{
    const double LetGoAfter = 2, DimShare = 0.4;

    readonly HashSet<string> _asPieces = new();
    readonly Dictionary<string, double> _unworn = new();
    readonly Dictionary<(int, int), CrowdWhere> _fallen = new();
    bool _fated;

    public Func<string, bool> Worn { get; set; } = _ => false;

    void Pieced(GameState view, double every)
    {
        _asPieces.Clear();
        _fated = view.Crowds?.Fates is { Count: > 0 };
        if (!_fated) _fallen.Clear();
        foreach (var p in view.Layer(Layers.Umpire).Where(p => p.Crowd != null).ToList())
        {
            _asPieces.Add(p.Crowd!);
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

    string? Fate(GameState view, string thing, int person, long now) => view.Crowds?.FateOf(CrowdPiece.Key(thing, person), now);

    CrowdWhere Fallen(GameState view, int ti, int i, float upm)
    {
        if (_fallen.TryGetValue((ti, i), out var known)) return known;
        var key = CrowdPiece.Key(_towns[ti].Roster.Thing, i);
        long at = view.Crowds?.Fates?.GetValueOrDefault(key)?.At ?? view.Clock.Seconds;
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
        var look = CrowdDress.Look(t.Roster.Residents[i].Entry, t.Roster.People, t.Roster.Coat);
        var (at, heading) = Where(view, ti, i, view.Clock.Seconds, upm);
        return (look, at, heading);
    }
}
