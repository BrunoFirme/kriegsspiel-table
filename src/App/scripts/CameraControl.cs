using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class CameraControl
{
    readonly TableContext _c;
    public CameraControl(TableContext c) { _c = c; }

    CameraRig Rig => _c.Rig;

    Vector3 _panVel;
    Vector2 _orbitVel;
    ulong _lastDragMs;

    public void DragBegin()
    {
        Rig.Still();
        _panVel = Vector3.Zero;
        _orbitVel = Vector2.Zero;
        _lastDragMs = Time.GetTicksMsec();
    }

    float DragStep()
    {
        var now = Time.GetTicksMsec();
        float dt = Mathf.Clamp((now - _lastDragMs) / 1000f, 0.004f, 0.1f);
        _lastDragMs = now;
        return dt;
    }

    public void TrackPan(Vector2 from, Vector2 to)
    {
        float dt = DragStep();
        var a = Rig.PlaneAt(from, Rig.Target.Y);
        var b = Rig.PlaneAt(to, Rig.Target.Y);
        Rig.PanScreen(from, to);
        if (a == null || b == null) return;
        var v = (a.Value - b.Value) / dt;
        v.Y = 0;
        _panVel = _panVel.Lerp(v, 0.5f);
    }

    public void TrackOrbit(Vector2 rel)
    {
        float dt = DragStep();
        Rig.Orbit(rel);
        _orbitVel = _orbitVel.Lerp(rel / dt, 0.5f);
    }

    public void LetGo(bool orbit)
    {
        bool moving = Time.GetTicksMsec() - _lastDragMs < 90;
        if (orbit) { if (moving) Rig.FlingOrbit(_orbitVel * 0.005f); _orbitVel = Vector2.Zero; }
        else { if (moving) Rig.Fling(_panVel); _panVel = Vector3.Zero; }
    }

    const float KeySpeed = 0.8f, KeyEase = 9f, KeyStopped = 0.01f;
    Vector3 _keyVel;

    public void TickKeys(double delta)
    {
        var move = Vector3.Zero;
        if (!Table.AnyTextFocus())
        {
            if (Input.IsKeyPressed(Key.W)) move += Vector3.Forward;
            if (Input.IsKeyPressed(Key.S)) move += Vector3.Back;
            if (Input.IsKeyPressed(Key.A)) move += Vector3.Left;
            if (Input.IsKeyPressed(Key.D) && !Input.IsKeyPressed(Key.Ctrl)) move += Vector3.Right;
        }
        float dt = (float)Math.Min(delta, 0.1);
        _keyVel = _keyVel.Lerp(move.LimitLength() * KeySpeed, 1 - Mathf.Exp(-dt * KeyEase));
        if (move == Vector3.Zero && _keyVel.Length() < KeyStopped) { _keyVel = Vector3.Zero; return; }
        Rig.PanWorld(_keyVel.Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi) * Rig.Distance * dt);
    }

    public void FrameAll()
    {
        var view = _c.S.View;
        var pts = view.Sheets.SelectMany(sh => new[] { Coords.ToWorld(sh.Pos) }).ToList();
        if (pts.Count == 0) pts = view.Layers.Values.SelectMany(l => l).Select(p => Coords.ToWorld(p.Pos)).ToList();
        if (pts.Count == 0) { Rig.Frame(Vector3.Zero, 60); return; }
        var c = pts.Aggregate(Vector3.Zero, (a, b) => a + b) / pts.Count;
        float size = view.Sheets.Count > 0 ? (float)view.Sheets.Max(s => Math.Max(s.Size.W, s.Size.H)) : 60;
        if (view.Sheets.Count == 0)
        {
            var board = view.Layer(Layers.Umpire);
            if (board.Count > 0)
            {
                double x0 = board.Min(p => p.Pos.X), x1 = board.Max(p => p.Pos.X);
                double z0 = board.Min(p => p.Pos.Z), z1 = board.Max(p => p.Pos.Z);
                size = (float)Math.Max(6, Math.Max(x1 - x0, z1 - z0));
                c = Coords.ToWorld(new Vec2((x0 + x1) / 2, (z0 + z1) / 2));
            }
        }
        if (view.Sheets.Count > 1)
        {
            var minX = view.Sheets.Min(s => s.Pos.X - s.Size.W / 2);
            var maxX = view.Sheets.Max(s => s.Pos.X + s.Size.W / 2);
            var minZ = view.Sheets.Min(s => s.Pos.Z - s.Size.H / 2);
            var maxZ = view.Sheets.Max(s => s.Pos.Z + s.Size.H / 2);
            size = (float)Math.Max(maxX - minX, maxZ - minZ);
            c = Coords.ToWorld(new Vec2((minX + maxX) / 2, (minZ + maxZ) / 2));
        }
        Rig.Frame(c, size);
    }

    public void FramePieces(IReadOnlyCollection<PieceInstance> pieces)
    {
        const float Room = 1.1f;
        if (pieces.Count == 0) return;
        double x0 = pieces.Min(p => p.Pos.X), x1 = pieces.Max(p => p.Pos.X);
        double z0 = pieces.Min(p => p.Pos.Z), z1 = pieces.Max(p => p.Pos.Z);
        var centre = Coords.ToWorld(new Vec2((x0 + x1) / 2, (z0 + z1) / 2));
        var spread = (float)Math.Max(x1 - x0, z1 - z0);
        if (spread * Room > Rig.Distance) Rig.Frame(centre, spread);
        else Rig.Centre(centre);
    }

    public void FitRange()
    {
        float extent = 60;
        foreach (var s in _c.S.View.Sheets)
            extent = Math.Max(extent, (float)(Math.Max(s.Size.W, s.Size.H) + Math.Max(Math.Abs(s.Pos.X), Math.Abs(s.Pos.Z))));
        Rig.MaxDistance = Math.Max(2500, extent * 2f);
        Rig.Cam.Far = Math.Max(10000, Rig.MaxDistance * 6);
    }

    public bool Restored;
    const double RestSeconds = 1.5;
    const int KeptViews = 40;
    double _idle;
    float[]? _seen;

    public bool Restore()
    {
        if (_c.Settings == null || ScenarioKey() is not { } key || !_c.Settings.CameraAt.TryGetValue(key, out var a) || a.Length < 6) return false;
        Restored = true;
        Rig.Show(new CameraRig.View(new Vector3(a[0], 0, a[1]), a[2], a[3], a[4], a[5] > 0.5f));
        return true;
    }

    string? ScenarioKey() => _c.S.View.ScenarioId is { Length: > 0 } id ? id : null;

    public void TickMemory(double delta)
    {
        var settings = _c.Settings;
        if (_c.S == null || settings == null || ScenarioKey() is not { } key) return;
        var now = new[] { Rig.Target.X, Rig.Target.Z, Rig.Distance, Rig.Yaw, Rig.Pitch, Rig.TopDown ? 1f : 0f };
        if (_seen == null || !Same(_seen, now))
        {
            _seen = now;
            _idle = 0;
            return;
        }
        _idle += delta;
        if (_idle < RestSeconds || settings.CameraAt.TryGetValue(key, out var kept) && Same(kept, now)) return;
        settings.CameraAt[key] = now;
        if (settings.CameraAt.Count > KeptViews) settings.CameraAt.Remove(settings.CameraAt.Keys.First());
        settings.Save();
        static bool Same(float[] a, float[] b) => a.Length == b.Length && a.Zip(b).All(p => Mathf.Abs(p.First - p.Second) < 0.01f);
    }
}
