using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;
using Held = KTable.Client.Hand.HeldPiece<KTable.App.PieceNode>;

namespace KTable.App;

sealed partial class Carry
{
    const double MarchSeconds = 0.32, MarchSettles = 0.28;
    const float MarchLift = 0.5f;

    float? _marchLift;

    float Raised => _marchLift ?? LiftSet;
    Dictionary<Held, (Vec2 from, double fromRot, Vec2 to, double toRot)>? _legs;
    Dictionary<Held, (Vec2 rel, double parentRot, double rot)>? _riding;
    double _marchStart, _landAt;

    public bool Marching => _legs != null;

    static double Now => Time.GetTicksMsec() / 1000.0;

    public void March(IReadOnlyList<(PieceNode Node, Vec2 Pos, double Rot)> to)
    {
        if (to.Count == 0 || Holding && !Marching) return;
        if (!Marching)
        {
            var nodes = to.Select(t => t.Node).Where(GodotObject.IsInstanceValid).ToList();
            if (nodes.Count == 0) return;
            var from = nodes.Select(n => Drawn(n).pos).ToList();
            Take(new Vec2(from.Average(p => p.X), from.Average(p => p.Z)), nodes);
            if (!Holding) return;
            Go();
            _marchLift = MarchLift;
        }
        var aim = to.ToDictionary(t => t.Node, t => (t.Pos, t.Rot));
        _legs = new();
        foreach (var h in _held)
            if (h.Parent == null && aim.TryGetValue(h.Node, out var t))
                _legs[h] = (_cursor + h.Offset, h.Rot, t.Pos, h.Rot + Mathf.Wrap((float)(t.Rot - h.Rot), -180, 180));
        _riding = _held.Where(h => h.Parent != null).ToDictionary(h => h, h => (h.Offset - h.Parent!.Offset, h.Parent!.Rot, h.Rot));
        _marchStart = Now;
        _landAt = _marchStart + MarchSeconds + MarchSettles;
        MarchOn();
    }

    public void Land()
    {
        if (!Marching) return;
        _marchStart = Now - MarchSeconds;
        _landAt = 0;
        MarchOn();
    }

    void MarchOn()
    {
        if (_legs == null || _riding == null) return;
        double now = Now, t = Math.Clamp((now - _marchStart) / MarchSeconds, 0, 1), k = t * t * (3 - 2 * t);
        foreach (var (h, leg) in _legs)
        {
            h.Offset = new Vec2(leg.from.X + (leg.to.X - leg.from.X) * k - _cursor.X, leg.from.Z + (leg.to.Z - leg.from.Z) * k - _cursor.Z);
            h.Rot = leg.fromRot + (leg.toRot - leg.fromRot) * k;
        }
        foreach (var (h, r) in _riding)
        {
            if (h.Parent is not { } p) continue;
            h.Offset = p.Offset + Formation.Turned(r.rel, (p.Rot - r.parentRot) * Math.PI / 180);
            h.Rot = r.rot + p.Rot - r.parentRot;
        }
        _floor = -1;
        Move(_cursor);
        if (now >= _landAt) Drop();
    }

    void EndMarch()
    {
        _legs = null;
        _riding = null;
        _marchLift = null;
    }
}
