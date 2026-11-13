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

    public void TickKeys(double delta, bool typing)
    {
        var move = Vector3.Zero;
        if (!typing)
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

    const float LoneBoardSpan = 6, EmptyTableSpan = 60;

    public void FrameAll()
    {
        var view = _c.S.View;
        var pts = new List<Vector3>();
        if (view.Sheets.Count > 0)
        {
            var g = Rig.Terrain();
            float top = g.Any ? Math.Max(0, (float)g.Highest) : 0;
            foreach (var sh in view.Sheets)
                foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    var corner = Coords.ToWorld(sh.Pos + SheetGeometry.Offset(dx * sh.Size.W / 2, dz * sh.Size.H / 2, sh.RotDeg));
                    pts.Add(corner);
                    if (top > 0) pts.Add(corner + Vector3.Up * top);
                }
        }
        else
        {
            var board = view.Layer(Layers.Umpire);
            var all = board.Count > 0 ? board : view.Layers.Values.SelectMany(l => l).ToList();
            if (all.Count == 0) { Rig.Frame(Vector3.Zero, EmptyTableSpan); return; }
            double x0 = all.Min(p => p.Pos.X), x1 = all.Max(p => p.Pos.X), z0 = all.Min(p => p.Pos.Z), z1 = all.Max(p => p.Pos.Z);
            double cx = (x0 + x1) / 2, cz = (z0 + z1) / 2;
            double hx = Math.Max(LoneBoardSpan, x1 - x0) / 2, hz = Math.Max(LoneBoardSpan, z1 - z0) / 2;
            foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                pts.Add(Coords.ToWorld(new Vec2(cx + dx * hx, cz + dz * hz)));
        }
        Rig.Fit(pts);
    }

    public void FrameSheet(SheetDef sheet) =>
        Rig.Fit(new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }
            .Select(k => Coords.ToWorld(sheet.Pos + SheetGeometry.Offset(k.Item1 * sheet.Size.W / 2, k.Item2 * sheet.Size.H / 2, sheet.RotDeg))).ToList());

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

    public const int ViewSlots = 9;

    public bool KeepsViews => _c.Settings != null && ScenarioKey() != null;

    public IReadOnlyDictionary<int, float[]> Views =>
        _c.Settings != null && ScenarioKey() is { } key && _c.Settings.CameraViews.TryGetValue(key, out var v) ? v : new Dictionary<int, float[]>();

    public bool SaveView(int slot)
    {
        if (_c.Settings is not { } settings || ScenarioKey() is not { } key || slot is < 1 or > ViewSlots || Rig.Eye != null) return false;
        if (!settings.CameraViews.TryGetValue(key, out var views)) settings.CameraViews[key] = views = new();
        views[slot] = Snapshot();
        settings.Save();
        _c.Say($"View {slot} saved");
        return true;
    }

    public bool GoToView(int slot)
    {
        if (!Views.TryGetValue(slot, out var a) || a.Length < 6) return false;
        if (Rig.Eye != null) Rig.LeaveEye();
        Rig.Show(new CameraRig.View(new Vector3(a[0], 0, a[1]), a[2], a[3], a[4], a[5] > 0.5f));
        return true;
    }

    public void ForgetView(int slot)
    {
        if (_c.Settings is not { } settings || ScenarioKey() is not { } key || !settings.CameraViews.TryGetValue(key, out var views) || !views.Remove(slot)) return;
        if (views.Count == 0) settings.CameraViews.Remove(key);
        settings.Save();
    }

    public void KeepNow()
    {
        if (_c.Settings is { } settings && Rig.Eye == null && ScenarioKey() is { } key) settings.CameraAt[key] = Snapshot();
    }

    float[] Snapshot() => new[] { Rig.Target.X, Rig.Target.Z, Rig.Distance, Rig.Yaw, Rig.Pitch, Rig.TopDown ? 1f : 0f };

    string? ScenarioKey() => _c.S.View.ScenarioId is { Length: > 0 } id ? id : null;

    public void TickMemory(double delta)
    {
        var settings = _c.Settings;
        if (_c.S == null || settings == null || Rig.Eye != null || ScenarioKey() is not { } key) return;
        var now = Snapshot();
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
