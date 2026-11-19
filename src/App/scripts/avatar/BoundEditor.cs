using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class BoundEditor
{
    public const float StoreyMetres = 3f;
    const float CloseNearPx = 12f, GrabNearPx = 10f, SnapMetres = 0.4f, ShoulderMetres = 1.6f, UnderGroundMetres = 0.5f, TracePx = 12f, TraceMetres = 0.5f;
    const int GroundSamples = 24;

    readonly TableContext _c;
    readonly Func<Vector2, (Vector3 At, PieceNode? On)?> _standOn;
    readonly Func<string, PieceNode?> _node;
    readonly HashSet<string> _hidden = new();
    readonly List<Vector3> _corners = new();
    readonly BoundMarks _marks;
    PieceNode? _drawingOn;
    Vector3? _cursor;
    int _dragging = -1;
    bool _open, _tracing, _chain;

    public BoundEditor(TableContext c, Func<Vector2, (Vector3 At, PieceNode? On)?> standOn, Func<string, PieceNode?> node)
    {
        (_c, _standOn, _node) = (c, standOn, node);
        _marks = new BoundMarks { Table = c, Edit = this, MouseFilter = Control.MouseFilterEnum.Ignore };
        _marks.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var layer = new CanvasLayer { Layer = 0 };
        layer.AddChild(_marks);
        c.Scene.AddChild(layer);
    }

    public event Action? Changed;

    public string? Layer { get; private set; }
    public string? Selected { get; private set; }
    public bool Drawing => _corners.Count > 0;
    public IReadOnlyList<Vector3> Corners => _corners;
    public Vector3? Cursor => _cursor;

    float Metre => AvatarPose.UnitsPerMetre(_c.S.View);

    public bool Open
    {
        get => _open;
        set
        {
            _open = value;
            if (!value) Stop();
            Changed?.Invoke();
        }
    }

    bool Active => _open && _c.S.IsUmpire && Layer != null && _c.S.View.BoundLayers?.Any(l => l.Id == Layer) == true;

    public bool Shown(string layer) => !_hidden.Contains(layer);

    public void Show(string layer, bool on)
    {
        if (on) _hidden.Remove(layer);
        else _hidden.Add(layer);
        Changed?.Invoke();
    }

    public void Pick(string? layer)
    {
        Stop();
        (Layer, Selected) = (Layer == layer ? null : layer, null);
        Changed?.Invoke();
    }

    public void Select(string? shape)
    {
        Selected = shape;
        Changed?.Invoke();
    }

    public BoundLayer? Current => _c.S.View.BoundLayers?.FirstOrDefault(l => l.Id == Layer);

    public BoundShape? Shape => Current?.Shapes.FirstOrDefault(s => s.Id == Selected);

    public PieceNode? NodeOf(BoundShape shape) => shape.On is { } id ? _node(id) : null;

    public float Reference(BoundShape shape) =>
        shape.On != null ? 0 : (float)_c.S.Ground.At(Middle(shape.Polygon));

    public float Unit(BoundShape shape) => shape.On != null && NodeOf(shape) is { } n && AvatarSolids.Where(n) is { } f ? Metre / Mathf.Max(f.Basis.Y.Length(), 1e-6f) : Metre;

    public void SetHeights(double floorMetres, double ceilingMetres)
    {
        if (Current is not { } layer || Shape is not { } shape || ceilingMetres <= floorMetres) return;
        float reference = Reference(shape), unit = Unit(shape);
        var changed = layer.Clone();
        var target = changed.Shapes.First(s => s.Id == shape.Id);
        (target.Floor, target.Ceiling) = (reference + floorMetres * unit, reference + ceilingMetres * unit);
        _c.S.Do(new SetBoundLayer { Bound = changed });
    }

    public void DeleteSelected()
    {
        if (Current is not { } layer || Shape is not { } shape) return;
        var changed = layer.Clone();
        changed.Shapes.RemoveAll(s => s.Id == shape.Id);
        _c.S.Do(new SetBoundLayer { Bound = changed });
        Select(null);
    }

    public void Tick() => _marks.Tick(Active);

    public bool Takes(InputEvent e)
    {
        if (!Active) return false;
        switch (e)
        {
            case InputEventMouseMotion mm:
                return Moved(mm.Position);
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } mb:
                return mb.Pressed ? Pressed(mb.Position, mb.ShiftPressed) : Released();
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } when Drawing:
                _corners.RemoveAt(_corners.Count - 1);
                return true;
            case InputEventKey { Pressed: true } key:
                return Key(key.Keycode);
        }
        return false;
    }

    bool Key(Godot.Key key)
    {
        switch (key)
        {
            case Godot.Key.Backspace when Drawing:
                _corners.RemoveAt(_corners.Count - 1);
                return true;
            case Godot.Key.Escape when Drawing:
                Stop();
                return true;
            case Godot.Key.Escape when Selected != null:
                Select(null);
                return true;
            case Godot.Key.Delete when Selected != null && !Drawing:
                DeleteSelected();
                return true;
        }
        return false;
    }

    bool Moved(Vector2 screen)
    {
        _cursor = Snapped(screen);
        if (_tracing && _cursor is { } at && _c.Rig.Screen(_corners[^1]).DistanceTo(screen) >= Ui.Pxf(TracePx)) _corners.Add(at);
        return _tracing || _dragging >= 0;
    }

    bool Pressed(Vector2 screen, bool shift)
    {
        if (Drawing && _chain)
        {
            if (_corners.Count >= 3 && _c.Rig.Screen(_corners[0]).DistanceTo(screen) < Ui.Pxf(CloseNearPx))
            {
                Finish();
                return true;
            }
            if (Snapped(screen) is { } next) _corners.Add(next);
            if (!shift && _corners.Count >= 3) Finish();
            return true;
        }
        if (!Drawing && Grabbed(screen) is int corner)
        {
            _dragging = corner;
            return true;
        }
        var at = Snapped(screen);
        if (at == null) return true;
        if (!Drawing && !shift && Picked(at.Value) is { } shape)
        {
            Select(shape);
            return true;
        }
        _drawingOn = _standOn(screen)?.On;
        Select(null);
        _corners.Clear();
        _corners.Add(at.Value);
        (_chain, _tracing) = (shift, !shift);
        return true;
    }

    bool Released()
    {
        if (_tracing)
        {
            _tracing = false;
            var traced = Straightened(_corners, TraceMetres * Metre);
            if (traced.Count > 3 && new Vector2(traced[^1].X - traced[0].X, traced[^1].Z - traced[0].Z).Length() <= 2 * TraceMetres * Metre) traced.RemoveAt(traced.Count - 1);
            _corners.Clear();
            _corners.AddRange(traced);
            if (_corners.Count >= 3) Finish();
            else Stop();
            return true;
        }
        if (_dragging < 0) return false;
        int corner = _dragging;
        _dragging = -1;
        if (Current is { } layer && Shape is { } shape && _cursor is { } at) Move(layer, shape, corner, at);
        return true;
    }

    void Move(BoundLayer layer, BoundShape shape, int corner, Vector3 at)
    {
        var changed = layer.Clone();
        var target = changed.Shapes.First(s => s.Id == shape.Id);
        target.Polygon[corner] = Flat(shape.On, at);
        _c.S.Do(new SetBoundLayer { Bound = changed });
    }

    int? Grabbed(Vector2 screen)
    {
        if (Shape is not { } shape) return null;
        var corners = Ring(shape);
        for (int i = 0; i < corners.Count; i++)
            if (_c.Rig.Screen(corners[i]).DistanceTo(screen) < Ui.Pxf(GrabNearPx)) return i;
        return null;
    }

    string? Picked(Vector3 at)
    {
        if (Current is not { } layer) return null;
        foreach (var shape in layer.Shapes)
        {
            var ring = Ring(shape);
            if (ring.Count >= 3 && BoundSpace.InRing(ring.Select(v => new Vector2(v.X, v.Z)).ToArray(), new Vector2(at.X, at.Z))) return shape.Id;
        }
        return null;
    }

    public List<Vector3> Ring(BoundShape shape)
    {
        if (shape.On == null) return shape.Polygon.Select(p => Coords.ToWorld(p, (float)shape.Floor)).ToList();
        if (NodeOf(shape) is not { } n || AvatarSolids.Where(n) is not { } frame) return [];
        return shape.Polygon.Select(p => frame * new Vector3((float)p.X, (float)shape.Floor, (float)p.Z)).ToList();
    }

    Vec2 Flat(string? on, Vector3 at)
    {
        if (on == null || _node(on) is not { } n || AvatarSolids.Where(n) is not { } frame) return Coords.FromWorld(at);
        var local = frame.AffineInverse() * at;
        return new Vec2(local.X, local.Z);
    }

    Vector3? Snapped(Vector2 screen)
    {
        if (_dragging >= 0 && Shape is { } shape) return Dragged(screen, shape);
        if (_standOn(screen) is not { } hit) return null;
        var on = Drawing ? _drawingOn : hit.On;
        if (on == null || AvatarSolids.Where(on) is not { } frame) return hit.At;
        var local = frame.AffineInverse() * hit.At;
        float height = Drawing ? (frame.AffineInverse() * _corners[0]).Y : local.Y;
        float unit = Metre / Mathf.Max(frame.Basis.Y.Length(), 1e-6f);
        var flat = WallSlice.Snap(on, height + ShoulderMetres * unit, new Vector2(local.X, local.Z), SnapMetres * unit) ?? new Vector2(local.X, local.Z);
        return frame * new Vector3(flat.X, height, flat.Y);
    }

    Vector3? Dragged(Vector2 screen, BoundShape shape)
    {
        var ring = Ring(shape);
        if (ring.Count == 0) return null;
        float y = ring[0].Y;
        Vector3 from = WorldShift.ToTable(_c.Rig.Cam.ProjectRayOrigin(screen)), along = _c.Rig.Cam.ProjectRayNormal(screen);
        if (Mathf.Abs(along.Y) < 1e-5f) return null;
        float t = (y - from.Y) / along.Y;
        if (t <= 0) return null;
        var at = from + along * t;
        if (NodeOf(shape) is not { } on || AvatarSolids.Where(on) is not { } frame) return at;
        var local = frame.AffineInverse() * at;
        float unit = Metre / Mathf.Max(frame.Basis.Y.Length(), 1e-6f);
        var flat = WallSlice.Snap(on, local.Y + ShoulderMetres * unit, new Vector2(local.X, local.Z), SnapMetres * unit) ?? new Vector2(local.X, local.Z);
        return frame * new Vector3(flat.X, local.Y, flat.Y);
    }

    void Finish()
    {
        if (Current is not { } layer || _corners.Count < 3) return;
        var shape = _drawingOn is { } on && AvatarSolids.Where(on) is { } frame ? OnBuilding(on, frame) : OnMap();
        var changed = layer.Clone();
        changed.Shapes.Add(shape);
        Stop();
        _c.S.Do(new SetBoundLayer { Bound = changed });
        if (_c.S.View.BoundLayers?.FirstOrDefault(l => l.Id == layer.Id) is { } made && made.Shapes.Count > 0) Select(made.Shapes[^1].Id);
    }

    BoundShape OnBuilding(PieceNode on, Transform3D frame)
    {
        var local = _corners.Select(c => frame.AffineInverse() * c).ToList();
        float floor = local.Min(v => v.Y), unit = Metre / Mathf.Max(frame.Basis.Y.Length(), 1e-6f);
        return new BoundShape { Polygon = local.Select(v => new Vec2(v.X, v.Z)).ToList(), Floor = floor, Ceiling = floor + StoreyMetres * unit, On = on.Piece.Id };
    }

    BoundShape OnMap()
    {
        var polygon = _corners.Select(Coords.FromWorld).ToList();
        var heights = Heights(polygon).ToList();
        return new BoundShape
        {
            Polygon = polygon, Floor = heights.Min() - UnderGroundMetres * Metre, Ceiling = heights.Max() + StoreyMetres * Metre,
        };
    }

    IEnumerable<float> Heights(List<Vec2> polygon)
    {
        foreach (var c in _corners) yield return c.Y;
        double x0 = polygon.Min(p => p.X), x1 = polygon.Max(p => p.X), z0 = polygon.Min(p => p.Z), z1 = polygon.Max(p => p.Z);
        var ring = polygon.Select(p => new Vector2((float)p.X, (float)p.Z)).ToArray();
        for (int j = 0; j <= GroundSamples; j++)
            for (int i = 0; i <= GroundSamples; i++)
            {
                var p = new Vec2(x0 + (x1 - x0) * i / GroundSamples, z0 + (z1 - z0) * j / GroundSamples);
                if (BoundSpace.InRing(ring, new Vector2((float)p.X, (float)p.Z))) yield return (float)_c.S.Ground.At(p);
            }
    }

    static Vec2 Middle(List<Vec2> polygon) => new(polygon.Average(p => p.X), polygon.Average(p => p.Z));

    void Stop()
    {
        _corners.Clear();
        _drawingOn = null;
        _dragging = -1;
        _tracing = _chain = false;
    }

    internal static List<Vector3> Straightened(List<Vector3> points, float tolerance)
    {
        if (points.Count < 3) return points.ToList();
        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        void Split(int from, int to)
        {
            float worst = 0;
            int at = -1;
            for (int i = from + 1; i < to; i++)
            {
                float off = OffLine(points[i], points[from], points[to]);
                if (off > worst) (worst, at) = (off, i);
            }
            if (at < 0 || worst <= tolerance) return;
            keep[at] = true;
            Split(from, at);
            Split(at, to);
        }
        Split(0, points.Count - 1);
        return points.Where((_, i) => keep[i]).ToList();
    }

    static float OffLine(Vector3 p, Vector3 a, Vector3 b)
    {
        var along = new Vector2(b.X - a.X, b.Z - a.Z);
        var to = new Vector2(p.X - a.X, p.Z - a.Z);
        float length = along.Length();
        return length < 1e-6f ? to.Length() : Mathf.Abs(along.Cross(to)) / length;
    }
}
