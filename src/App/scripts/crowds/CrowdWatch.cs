using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class CrowdWatch(TableContext c, CrowdLayer layer) : ICrowdWatch
{
    readonly List<(string Thing, int Person, Node3D Bubble, float Tall)> _speaking = new();
    readonly Dictionary<string, double> _due = new();
    readonly Random _dice = new();

    float Upm => (float)(c.S.View.Calibration.UnitsPerMeter ?? 1);

    public IReadOnlyList<CrowdOwner> Owners()
    {
        var view = c.S.View;
        var owners = new List<CrowdOwner>();
        for (int ti = 0; ti < layer.Towns; ti++)
        {
            var roster = layer.Roster(ti);
            string name = CrowdScript.NameOf(view, roster.Thing);
            var (shown, seen) = layer.Counts(ti);
            owners.Add(new CrowdOwner(roster.Thing, name, roster.Made, shown, seen));
        }
        return owners;
    }

    public IReadOnlyList<CrowdSeen> OutNow(string? owner)
    {
        var seen = new List<CrowdSeen>();
        for (int ti = 0; ti < layer.Towns; ti++)
        {
            if (owner != null && layer.Roster(ti).Thing != owner) continue;
            for (int i = 0; i < layer.Roster(ti).Residents.Count; i++)
                if (layer.Observed(ti, i) is { } one) seen.Add(one);
        }
        return seen;
    }

    public CrowdSeen? Find(string id) =>
        CrowdPiece.Parse(id) is { } who && layer.TownOf(who.Thing) is >= 0 and var ti && who.Person < layer.Roster(ti).Residents.Count ? layer.Observed(ti, who.Person) : null;

    public bool Speak(string key, string text)
    {
        if (CrowdPiece.Parse(key) is not { } who || layer.TownOf(who.Thing) is not (>= 0 and var ti) || who.Person >= layer.Roster(ti).Residents.Count) return false;
        float tall = (float)layer.Roster(ti).Residents[who.Person].Entry.Look!.HeightM * Upm;
        if (new SpeechBubbles(c).Show(text, Here(ti, who.Person), tall) is { } bubble) _speaking.Add((who.Thing, who.Person, bubble, tall));
        return true;
    }

    Vector3 Here(int ti, int i)
    {
        var view = c.S.View;
        var (at, _) = layer.Where(view, ti, i, view.Clock.Moment, Upm);
        return Coords.ToWorld(at, layer.HeightAt(ti, at));
    }

    public void Tick()
    {
        Follow();
        if (c.S.IsHost) Chatter();
    }

    void Chatter()
    {
        var view = c.S.View;
        if (view.Crowds?.Towns is not { Count: > 0 } towns) return;
        double now = view.Clock.Moment;
        foreach (var (owner, set) in towns)
            if (set.Talk is { Lines.Count: > 0 } talk && Due(owner, talk, now) && Speaker(owner) is { } who)
                c.S.Do(new Speak { Layer = CrowdPiece.Layer, Id = who, Text = talk.Lines[_dice.Next(talk.Lines.Count)], Loud = talk.Loud });
    }

    string? Speaker(string owner)
    {
        var seen = OutNow(owner);
        var still = seen.Where(p => p.Staying).ToList();
        var pool = still.Count > 0 ? still : seen;
        return pool.Count > 0 ? pool[_dice.Next(pool.Count)].Id : null;
    }

    bool Due(string owner, CrowdTalk talk, double now)
    {
        double every = talk.EveryMin * 60;
        if (!_due.TryGetValue(owner, out double due) || due > now + every)
        {
            _due[owner] = now + every * _dice.NextDouble();
            return false;
        }
        if (now < due) return false;
        _due[owner] = now + every * (0.5 + _dice.NextDouble());
        return true;
    }

    void Follow()
    {
        for (int k = _speaking.Count - 1; k >= 0; k--)
        {
            var (thing, person, bubble, tall) = _speaking[k];
            if (!GodotObject.IsInstanceValid(bubble) || bubble.IsQueuedForDeletion() || layer.TownOf(thing) is not (>= 0 and var ti))
            {
                _speaking.RemoveAt(k);
                continue;
            }
            bubble.Position = Here(ti, person) + Vector3.Up * (tall * SpeechBubbles.Above);
        }
    }
}
