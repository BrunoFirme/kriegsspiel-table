using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    public bool IsDie => Def is { Kind: "model" } d && PieceInstance.FacesOf(d, Piece).Count > 1;
    List<DieFace> Faces => PieceInstance.FacesOf(Def, Piece);

    Vector3[]? _hull;
    bool _meshReady;
    public bool Rolling { get; private set; }
    public int? PendingState { get; private set; }
    Vec2 _lastPos;
    double _lastRot, _lastY;
    bool _spinning, _landing;
    double _spinT;
    int _spinTouches;
    Vector3 _spinAxis;
    float _spinSpeed;
    Basis _spinFrom;
    Vector3 _spinCenter;
    int _animGen;
    static readonly Random Rng = new();
    public static bool DebugDice;
    void Dbg(string what) { if (DebugDice) GD.Print($"[die {Def?.Name} {Piece.Id}] {what}"); }

    public Vector3[]? DiceHull => _hull;
    public Basis ModelWorldBasis => _model != null ? _model.GlobalBasis.Orthonormalized() : Basis.Identity;
    public Vector3 ModelWorldCenter => _model?.GlobalPosition ?? GlobalPosition;

    Quaternion FaceQuat(int state)
    {
        var faces = Faces;
        if (faces.Count == 0) return Quaternion.Identity;
        var up = faces[Math.Clamp(state, 0, faces.Count - 1)].Up;
        if (up is not { Length: 3 }) return Quaternion.Identity;
        return ArcTo(new Vector3((float)up[0], (float)up[1], (float)up[2]).Normalized(), Vector3.Up);
    }

    public int FaceUp(Basis rest)
    {
        int best = Piece.ShownFace;
        float most = float.MinValue;
        var faces = Faces;
        for (int i = 0; i < faces.Count; i++)
            if (faces[i].Up is { Length: 3 } up)
            {
                float d = (rest * new Vector3((float)up[0], (float)up[1], (float)up[2]).Normalized()).Dot(Vector3.Up);
                if (d > most) { most = d; best = i; }
            }
        return best;
    }

    (float lo, float hi) ExtentY(Basis b)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in _hull!)
        {
            var y = (b * p).Y;
            lo = Mathf.Min(lo, y);
            hi = Mathf.Max(hi, y);
        }
        return (lo, hi);
    }

    void ApplyFace(int state)
    {
        if (_model == null || _hull == null) return;
        var q = FaceQuat(state);
        var (lo, hi) = ExtentY(new Basis(q));
        Dbg($"face {state + 1} applied");
        _model.Quaternion = q;
        _model.Position = new Vector3(0, -lo, 0);
        _modelRest = q;
        _modelRestPos = _model.Position;
        _thickness = hi - lo;
        _shownState = state;
    }

    void RefreshDie(PieceInstance p)
    {
        Piece = p;
        if (p.TintOverride != _shownTint)
        {
            _shownTint = p.TintOverride;
            if (_modelMat != null) _modelMat.AlbedoColor = Tint();
        }
        if (Rolling)
        {
            PendingState = p.ShownFace;
            Dbg($"rolling: pending face {p.ShownFace + 1}");
            return;
        }
        if (Placed)
        {
            _lastPos = p.Pos;
            _lastRot = p.RotDeg;
            _lastY = p.Y;
            if (_meshReady && p.ShownFace != _shownState) ApplyFace(p.ShownFace);
            return;
        }
        bool moved = (p.Pos - _lastPos).Length > 1e-6 || Math.Abs(p.RotDeg - _lastRot) > 1e-6 || Math.Abs(p.Y - _lastY) > 1e-6;
        bool faceChanged = _meshReady && p.ShownFace != _shownState;
        Dbg($"refresh: face {p.ShownFace + 1} shown {_shownState + 1} meshReady {_meshReady} moved {moved} spinning {_spinning} landing {_landing}");
        if (_spinning || _landing) { if (moved) LandSpin(); return; }
        if (faceChanged) { BeginSpin(); return; }
        _lastPos = p.Pos;
        _lastRot = p.RotDeg;
        _lastY = p.Y;
        Scale = Vector3.One;
        Position = Coords.ToWorld(p.Pos, BaseY(p));
        Rotation = new Vector3(0, Coords.Yaw(p.RotDeg), 0);
        _lettering.Layout(new());
    }

    public int RollGen { get; private set; }

    public void BeginRoll()
    {
        _animGen++;
        _spinning = false;
        _landing = false;
        SetProcess(false);
        Rolling = true;
        PendingState = null;
        RollGen++;
    }

    public bool InRoll(int gen) => Rolling && RollGen == gen;

    public void EndRoll() { Rolling = false; PendingState = null; }

    void StopDieAnim()
    {
        if (Rolling)
        {
            int face = PendingState ?? Piece.ShownFace;
            EndRoll();
            TakeFace(face);
            return;
        }
        if (!_spinning && !_landing) return;
        _animGen++;
        _spinning = false;
        _landing = false;
        SetProcess(false);
        TakeFace(Piece.ShownFace);
    }

    public void TakeFace(int state)
    {
        _animGen++;
        _spinning = false;
        _landing = false;
        SetProcess(false);
        _lastPos = Piece.Pos;
        _lastRot = Piece.RotDeg;
        _lastY = Piece.Y;
        if (_meshReady) ApplyFace(state);
    }

    public Quaternion TurnInHand => _model?.Quaternion ?? Quaternion.Identity;

    public void TurnInHandTo(Quaternion q)
    {
        if (_model == null || _hull == null) return;
        var (lo, _) = ExtentY(new Basis(q));
        _model.Quaternion = q;
        _model.Position = new Vector3(0, -lo, 0);
    }

    public static Quaternion AnyTurn(Random rng)
    {
        double u = rng.NextDouble(), v = rng.NextDouble() * Math.Tau, w = rng.NextDouble() * Math.Tau;
        double a = Math.Sqrt(1 - u), b = Math.Sqrt(u);
        return new Quaternion((float)(a * Math.Sin(v)), (float)(a * Math.Cos(v)), (float)(b * Math.Sin(w)), (float)(b * Math.Cos(w))).Normalized();
    }

    public void ShowTumble(Basis worldBasis, Vector3 worldCenter)
    {
        if (_model == null) return;
        _model.GlobalTransform = new Transform3D(worldBasis * Basis.FromScale(_model.Scale), worldCenter);
    }

    public (double rotDeg, double y) SettleFromPhysics(int state, Basis rest, Vector3 center, Func<Vec2, double, double, double> stored)
    {
        if (_hull == null) return (Piece.RotDeg, Piece.Y);
        var R = new Basis(FaceQuat(state));
        var v = (rest * R.Inverse()) * Vector3.Right;
        float theta = Mathf.Atan2(-v.Z, v.X);
        var (loR, _) = ExtentY(rest);
        var pos = Coords.FromWorld(center);
        double rotDeg = ((180 - Mathf.RadToDeg(theta)) % 360 + 360) % 360;
        double y = stored(pos, rotDeg, Math.Max(0, center.Y + loR - 0.03));
        _animGen++;
        FinishAt(state, pos, rotDeg, y);
        return (rotDeg, y);
    }

    public void RollArrived() { if (!_spinning && !_landing && _meshReady) BeginSpin(); }

    void BeginSpin()
    {
        if (_model == null || !_meshReady) return;
        _animGen++;
        _spinning = true;
        _spinT = 0;
        _spinTouches = 0;
        Sounds?.Tumble(this, 0.8f);
        _spinFrom = ModelWorldBasis;
        _spinCenter = ModelWorldCenter;
        Dbg("spin begins");
        var a = Mathf.Tau * (float)Rng.NextDouble();
        _spinAxis = new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)).Normalized();
        _spinSpeed = 11 + (float)Rng.NextDouble() * 4;
        SetProcess(true);
    }

    public override void _Process(double delta)
    {
        bool gliding = StepGlide(delta);
        if (!_spinning)
        {
            if (!gliding) SetProcess(false);
            return;
        }
        _spinT += delta;
        float t = (float)_spinT;
        float hop = 0.55f * Mathf.Abs(Mathf.Sin(t * Mathf.Pi / 0.5f)) * Mathf.Exp(-t * 0.7f);
        ShowTumble(new Basis(_spinAxis, _spinSpeed * t) * _spinFrom, _spinCenter + Vector3.Up * hop);
        if ((int)(t / 0.5f) > _spinTouches) { _spinTouches = (int)(t / 0.5f); Sounds?.Tumble(this, 0.9f * Mathf.Exp(-t * 0.7f)); }
        if (_spinT > 2.5) LandSpin();
    }

    void LandSpin()
    {
        _spinning = false;
        _landing = true;
        SetProcess(false);
        var p = Piece;
        _lastPos = p.Pos;
        _lastRot = p.RotDeg;
        _lastY = p.Y;
        Dbg($"landing at {p.Pos.X:0.0},{p.Pos.Z:0.0} face {p.ShownFace + 1}");
        _ = GlideTo(p.ShownFace, p.Pos, p.RotDeg, p.Y, ModelWorldBasis, ModelWorldCenter);
    }

    async Task GlideTo(int state, Vec2 pos, double rotDeg, double y, Basis fromB, Vector3 fromC)
    {
        int gen = ++_animGen;
        var T = new Basis(Vector3.Up, Coords.Yaw(rotDeg)) * new Basis(FaceQuat(state));
        var (loT, _) = ExtentY(T);
        var toC = Coords.ToWorld(pos, BaseAt(pos, rotDeg, y, 0) - loT);
        await Animate(fromB, fromC, T, toC, 0.3, 0.25f, gen);
        if (gen != _animGen) return;
        Sounds?.Tumble(this, 0.2f);
        FinishAt(state, pos, rotDeg, y);
    }

    async Task Animate(Basis fromB, Vector3 fromC, Basis toB, Vector3 toC, double seconds, float hop, int gen)
    {
        var qf = fromB.GetRotationQuaternion();
        var qt = toB.GetRotationQuaternion();
        double t = 0;
        while (t < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!IsInstanceValid(this) || gen != _animGen) return;
            t += GetProcessDeltaTime();
            float k = Mathf.Clamp((float)(t / seconds), 0, 1);
            k = k * k * (3 - 2 * k);
            ShowTumble(new Basis(qf.Slerp(qt, k)), fromC.Lerp(toC, k) + Vector3.Up * (hop * 4 * k * (1 - k)));
        }
    }

    void FinishAt(int state, Vec2 pos, double rotDeg, double y)
    {
        _landing = false;
        _lastPos = pos;
        _lastRot = rotDeg;
        _lastY = y;
        Scale = Vector3.One;
        Position = Coords.ToWorld(pos, BaseAt(pos, rotDeg, y, 0));
        Rotation = new Vector3(0, Coords.Yaw(rotDeg), 0);
        ApplyFace(state);
        if (!Rolling && Piece.ShownFace != state) BeginSpin();
    }
}
