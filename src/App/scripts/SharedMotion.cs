using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.App;

sealed class SharedMotion
{
    const double LingerSeconds = 0.3;
    const float MovedFarEnough = 0.05f;

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Carry _hand;
    readonly CarryShare _carry = new();
    readonly FlightShare _flight = new();
    readonly MotionBuffer _buffer = new();
    readonly Dictionary<PieceNode, double> _moving = new();
    readonly Dictionary<long, List<CarriedPiece>> _layouts = new();
    readonly HashSet<PieceKey> _taken = new();

    public SharedMotion(TableContext c, PieceLayer pieces, Carry hand)
    {
        _c = c; _pieces = pieces; _hand = hand;
    }

    public event Action<IReadOnlyList<CarriedPiece>, HandAt, double>? LayoutShared { add => _carry.SendLayout += value; remove => _carry.SendLayout -= value; }
    public event Action<HandAt, double>? PlaceShared { add => _carry.SendPlace += value; remove => _carry.SendPlace -= value; }
    public event Action<double, IReadOnlyList<MotionSample>>? FlightShared { add => _flight.Send += value; remove => _flight.Send -= value; }

    static double Now => Time.GetTicksMsec() / 1000.0;

    public void Moving(PieceNode n) => _moving[n] = Now + LingerSeconds;

    public bool Carrying(long carrier) => _layouts.ContainsKey(carrier);

    public void LayoutArrived(long carrier, IReadOnlyList<CarriedPiece> pieces, HandAt at, double t)
    {
        if (pieces.Count == 0) { _layouts.Remove(carrier); return; }
        _layouts[carrier] = pieces.ToList();
        HandArrived(carrier, at, t);
    }

    public void HandArrived(long carrier, HandAt at, double t)
    {
        if (!_layouts.TryGetValue(carrier, out var layout)) return;
        var samples = new List<MotionSample>();
        foreach (var p in layout)
            if (_pieces.NodeOf(p.Layer, p.Id) is { } n && GodotObject.IsInstanceValid(n))
                samples.Add(new MotionSample(new PieceKey(p.Layer, p.Id), n.HandPose(new Vec2(at.X + p.Dx, at.Z + p.Dz), p.RotDeg, at.Y + p.Lift)));
        _buffer.Add(carrier, t, samples, Now);
    }

    public void FlightArrived(long carrier, double t, IReadOnlyList<MotionSample> samples) => _buffer.Add(carrier, t, samples, Now);

    public void Tick()
    {
        var now = Now;
        _carry.Tick(now, _hand.Holding ? _hand.Shared() : [], _hand.At);
        ShareFlights(now);
        foreach (var (key, pose) in _buffer.Poses(now))
        {
            if (Replayable(key) is not { } n) continue;
            if (_taken.Add(key)) Take(n);
            n.ShowPose(pose);
        }
        var landed = new List<(PieceNode n, bool onPiece, double after)>();
        foreach (var (key, from, to) in _buffer.Finished(now, StillHeld))
        {
            if (!_taken.Remove(key) || Node(key) is not { } n) continue;
            Give(n);
            if (System.Numerics.Vector3.Distance(from, to) > MovedFarEnough && !n.IsDie) landed.Add((n, n.Piece.Y > 0.01, 0));
        }
        if (landed.Count > 0) _c.Sound.LandMany(landed, 0.6f);
    }

    void ShareFlights(double now)
    {
        foreach (var gone in _moving.Where(m => m.Value < now || !GodotObject.IsInstanceValid(m.Key)).Select(m => m.Key).ToList()) _moving.Remove(gone);
        var moving = _moving.Keys.Where(n => !_hand.Has(n)).Select(n => new MotionSample(new PieceKey(n.Layer, n.Piece.Id), n.ShownPose)).ToList();
        _flight.Tick(now, moving);
    }

    bool StillHeld(long carrier, PieceKey key) =>
        _layouts.TryGetValue(carrier, out var layout) && layout.Exists(p => p.Layer == key.Layer && p.Id == key.Id);

    PieceNode? Node(PieceKey key) =>
        _pieces.NodeOf(key.Layer, key.Id) is { } n && GodotObject.IsInstanceValid(n) ? n : null;

    PieceNode? Replayable(PieceKey key)
    {
        if (Node(key) is not { } n || _hand.Has(n) || _moving.ContainsKey(n)) return null;
        if (_taken.Contains(key)) return n;
        return n.Placed || n.Rolling ? null : n;
    }

    static void Take(PieceNode n)
    {
        n.Held = true;
        if (n.IsDie) n.BeginRoll();
    }

    static void Give(PieceNode n)
    {
        if (n.IsDie) { n.EndRoll(); n.TakeFace(n.Piece.State); }
        n.Held = false;
        n.Refresh(n.Piece);
    }
}
