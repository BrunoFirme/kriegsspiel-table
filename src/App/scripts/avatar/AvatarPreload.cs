using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class AvatarPreload(TableContext c, Walker walk, AvatarFigures figures, ThrownDice dice)
{
    const double LookEvery = 1, PicturesWithinMs = 10000;

    readonly HashSet<string> _packsSeen = new();
    readonly Queue<IEnumerator<bool>> _work = new();
    readonly List<Resource> _kept = new();
    readonly OutOfSight _stage = new(c.Scene);
    bool _walkersHere, _common;
    double _lookIn;

    public bool Busy => _work.Count > 0;

    public bool Started => _common;

    public bool Holds(PieceDef body) => Gait.Warmed(body, c);

    public void Tick(double delta)
    {
        _stage.Tick();
        if ((_lookIn -= delta) <= 0)
        {
            _lookIn = LookEvery;
            Look();
        }
        while (_work.Count > 0)
        {
            if (_work.Peek().MoveNext()) return;
            _work.Dequeue().Dispose();
        }
    }

    void Look()
    {
        foreach (var id in c.S.View.Packs)
        {
            if (_packsSeen.Contains(id) || c.S.Store.Pack(id) is not { } pack) continue;
            _packsSeen.Add(id);
            foreach (var def in pack.Pieces.Where(d => d.Walk is { Count: > 0 }))
            {
                _walkersHere = true;
                _work.Enqueue(Gait.Warming(def, c).GetEnumerator());
            }
        }
        if (_common || !(_walkersHere || walk.Walking || figures.Count > 0)) return;
        _common = true;
        _work.Enqueue(Common().GetEnumerator());
    }

    IEnumerable<bool> Common()
    {
        _kept.AddRange(FlightFx.Warmed());
        yield return true;
        Show(figures.Sample());
        yield return true;
        foreach (int sides in ThrownDice.Throwable)
        {
            Show(dice.Sample(sides));
            yield return true;
        }
        ulong began = Time.GetTicksMsec();
        while (!ThrownDice.Throwable.All(dice.Ready) && Time.GetTicksMsec() - began < PicturesWithinMs) yield return true;
        foreach (int sides in ThrownDice.Throwable)
        {
            Show(dice.Sample(sides));
            yield return true;
        }
    }

    void Show(Node3D? sample)
    {
        if (sample != null) _stage.Hold(sample);
    }
}
