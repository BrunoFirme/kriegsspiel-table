using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

sealed class ZoneLayer
{
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    public ZoneLayer(TableContext c, ToolOverlay overlay) { _c = c; _overlay = overlay; }

    public event Action<List<Vec2>>? Drawn;

    Node3D? _root;
    Vec2? _a, _b;
    public bool Show = true;

    public void Sync()
    {
        if (_root == null) { _root = new Node3D { Name = "Zones" }; _c.Scene.AddChild(_root); }
        foreach (var c in _root.GetChildren()) c.QueueFree();
        _c.Labels.RemoveWhere(k => k.StartsWith("zone:"));
        if (!_c.S.IsUmpire || !Show) return;
        foreach (var z in _c.S.View.Zones)
        {
            if (z.Polygon.Count < 3) continue;
            var col = UmpireHue.Of(_c.S, z.Owner) ?? UmpireHue.Unassigned;
            var mine = string.Equals(z.Owner, _c.S.Me.Name, StringComparison.OrdinalIgnoreCase);
            var fill = new StandardMaterial3D { AlbedoColor = col with { A = mine ? 0.16f : 0.09f }, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false };
            var line = new StandardMaterial3D { AlbedoColor = col, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
            var im = new ImmediateMesh();
            var ground = _c.S.Ground;
            im.SurfaceBegin(Mesh.PrimitiveType.Triangles, fill);
            if (ground.Any) Drape(im, z.Polygon, ground);
            else
                for (int i = 1; i + 1 < z.Polygon.Count; i++)
                    foreach (var p in new[] { z.Polygon[0], z.Polygon[i], z.Polygon[i + 1] }) im.SurfaceAddVertex(Coords.ToWorld(p, FillAbove));
            im.SurfaceEnd();
            _overlay.RibbonPx(im, line, z.Polygon.Append(z.Polygon[0]).ToList(), 0.02f, 2f);
            _root.AddChild(new MeshInstance3D { Mesh = im, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            var c = z.Polygon.Aggregate(Vector3.Zero, (a, p) => a + Coords.ToWorld(p)) / z.Polygon.Count;
            c.Y = (float)ground.At(Coords.FromWorld(c));
            _c.Labels.Set("zone:" + z.Id, c + Vector3.Up * 0.3f, $"{z.Name} — {z.Owner}", col.Lightened(0.3f), 13);
        }
    }

    const float FillAbove = 0.015f;
    const int DrapeCells = 64;

    static void Drape(ImmediateMesh im, List<Vec2> poly, Ground ground)
    {
        double x0 = poly.Min(p => p.X), x1 = poly.Max(p => p.X), z0 = poly.Min(p => p.Z), z1 = poly.Max(p => p.Z);
        double sx = (x1 - x0) / DrapeCells, sz = (z1 - z0) / DrapeCells;
        Vector3 At(double x, double z) { var p = new Vec2(x, z); return Coords.ToWorld(p, (float)ground.At(p) + FillAbove); }
        for (int j = 0; j < DrapeCells; j++)
            for (int i = 0; i < DrapeCells; i++)
            {
                double ax = x0 + i * sx, az = z0 + j * sz;
                if (!Inside(poly, new Vec2(ax + sx / 2, az + sz / 2))) continue;
                var a = At(ax, az); var b = At(ax + sx, az); var c = At(ax, az + sz); var d = At(ax + sx, az + sz);
                foreach (var v in new[] { a, b, c, b, d, c }) im.SurfaceAddVertex(v);
            }
    }

    static bool Inside(List<Vec2> poly, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            if ((poly[i].Z > p.Z) != (poly[j].Z > p.Z) && p.X < (poly[j].X - poly[i].X) * (p.Z - poly[i].Z) / (poly[j].Z - poly[i].Z) + poly[i].X) inside = !inside;
        return inside;
    }

    public void Press(Vec2 g) { _a = g; _b = g; }
    public void Motion(Vec2 g) { if (_a != null) _b = g; }
    public void Release()
    {
        if (_a is { } a && _b is { } b && Math.Abs(a.X - b.X) > 0.5 && Math.Abs(a.Z - b.Z) > 0.5)
            Drawn?.Invoke(new List<Vec2> { new(a.X, a.Z), new(b.X, a.Z), new(b.X, b.Z), new(a.X, b.Z) });
        _a = _b = null;
    }

    public void Drop() => _a = _b = null;

    public void DrawDraft()
    {
        if (_a is not { } a || _b is not { } b) return;
        var mat = new StandardMaterial3D { AlbedoColor = new Color(1, 0.75f, 0.2f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true };
        _overlay.RibbonPx(_overlay.Mesh, mat, new List<Vec2> { new(a.X, a.Z), new(b.X, a.Z), new(b.X, b.Z), new(a.X, b.Z), new(a.X, a.Z) }, 0.3f);
    }
}

sealed class UmpirePointers
{
    const double IdleSeconds = 10;
    const float HandPixels = 62;
    const float SmallestSize = 0.05f, LargestSize = 60f;
    const float TipAbove = 0.01f;
    const float AboveCarried = 0.35f;
    const int NameSize = 12;
    static readonly Vector2 NameAbove = new(0, -12);
    readonly TableContext _c;
    readonly Func<Vec2, float> _surface;
    readonly Func<long, bool> _carrying;
    readonly Func<string, Color> _hue;
    public UmpirePointers(TableContext c, Func<Vec2, float> surface, Func<long, bool> carrying, Func<string, Color> hue) { _c = c; _surface = surface; _carrying = carrying; _hue = hue; }

    public event Action<Vec2>? Moved;

    sealed class RemoteCursor { public required PointerHand Hand; public required Color Hue; public string Name = ""; public double Age; public Vec2 Target; }

    readonly Dictionary<long, RemoteCursor> _cursors = new();
    public bool Show = true;
    double _timer;
    Vec2? _last;

    public void Track(Vector2 screen, double delta)
    {
        _timer += delta;
        if (_timer < 1 / 15.0) return;
        var g = _c.Ground(screen);
        if (g == null || (_last is { } lp && (lp - g.Value).Length < 0.05)) return;
        _timer = 0;
        _last = g;
        Moved?.Invoke(g.Value);
    }

    Vector3 TipAt(Vec2 pos, bool carrying = false) => Coords.ToWorld(pos, _surface(pos) + TipAbove + (carrying ? Carry.LiftHeight + AboveCarried : 0));

    public void ShowRemote(long peer, string name, Vec2 pos)
    {
        if (!Show) return;
        var hue = _hue(name);
        if (!_cursors.TryGetValue(peer, out var c))
        {
            var hand = new PointerHand(hue);
            _c.Scene.AddChild(hand);
            hand.Jump(TipAt(pos));
            _cursors[peer] = c = new RemoteCursor { Hand = hand, Hue = hue };
        }
        if (c.Hue != hue) { c.Hue = hue; c.Hand.Recolor(hue); }
        c.Name = name;
        c.Target = pos;
        c.Age = 0;
    }

    public IEnumerable<long> Peers() => _cursors.Keys;

    public void Forget(long peer)
    {
        if (_cursors.Remove(peer, out var c)) { c.Hand.QueueFree(); _c.Labels.Remove("cursor:" + peer); }
    }

    public void Tick(double delta)
    {
        var forward = -_c.Rig.Cam.GlobalBasis.Z;
        var yaw = Mathf.Atan2(-forward.X, -forward.Z);
        foreach (var (peer, c) in _cursors)
        {
            c.Age += delta;
            var carrying = _carrying(peer);
            var seen = Show && (c.Age < IdleSeconds || carrying);
            c.Hand.Visible = seen;
            if (!seen) { _c.Labels.Remove("cursor:" + peer); continue; }
            var tip = TipAt(c.Target, carrying);
            var size = Mathf.Clamp(HandPixels / _c.Rig.PixelsPerUnitAt(tip), SmallestSize, LargestSize);
            c.Hand.Step(tip, yaw, carrying, size, delta);
            _c.Labels.Set("cursor:" + peer, c.Hand.Above, c.Name, c.Hue.Lightened(0.35f), NameSize, NameAbove, centered: true);
        }
    }
}
