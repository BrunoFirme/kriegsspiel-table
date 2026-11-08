using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

internal sealed partial class ModelProfileEdit : Control
{
    ShapeSpec _spec = new();
    int _drag = -1;
    float _scale = 1;
    Vector2 _origin;
    public int Chosen { get; private set; }
    public Action<ShapeSpec>? Changed;
    public Action? Picked;

    const float PointRadius = 5, PickReach = 9, Pad = 14;
    const int GridLines = 10;
    static readonly Color Line = new(1, 1, 1, 0.7f), Axis = new(0.8f, 0.64f, 0.3f, 0.7f), Grid = new(1, 1, 1, 0.07f), Dot = new(0.95f, 0.72f, 0.3f);

    public ShapeSpec Spec
    {
        get => _spec;
        set
        {
            _spec = value.Copy();
            Chosen = Math.Clamp(Chosen, 0, Math.Max(0, _spec.Count - 1));
            QueueRedraw();
        }
    }

    public ModelProfileEdit()
    {
        CustomMinimumSize = new Vector2(0, 170);
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        TooltipText = "Drag a point · double-click a line to add one · right-click a point to take it out";
    }

    Vector2 At(int i) => new(_spec.Points[i * _spec.Stride], _spec.Points[i * _spec.Stride + 1]);

    Vector2 ToScreen(Vector2 p) => _origin + new Vector2(p.X, -p.Y) * _scale;
    Vector2 ToShape(Vector2 s) => new((s.X - _origin.X) / _scale, -(s.Y - _origin.Y) / _scale);

    void Fit()
    {
        var pts = Enumerable.Range(0, _spec.Count).Select(At).ToList();
        if (_spec.Kind == ShapeMaker.Lathe) pts.Add(new Vector2(0, pts.Count > 0 ? pts[0].Y : 0));
        if (pts.Count == 0) pts.Add(Vector2.Zero);
        var lo = pts.Aggregate((a, b) => a.Min(b));
        var hi = pts.Aggregate((a, b) => a.Max(b));
        var span = (hi - lo).Max(new Vector2(0.1f, 0.1f)) * 1.25f;
        var room = Size - new Vector2(Pad * 2, Pad * 2);
        _scale = Mathf.Max(1e-3f, Mathf.Min(room.X / span.X, room.Y / span.Y));
        var mid = (lo + hi) / 2;
        _origin = Size / 2 - new Vector2(mid.X, -mid.Y) * _scale;
    }

    public override void _Draw()
    {
        if (_drag < 0) Fit();
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.18f));
        float step = Mathf.Pow(10, Mathf.Floor(Mathf.Log(Mathf.Max(1e-3f, Size.X / _scale / GridLines)) / Mathf.Log(10)));
        var a = ToShape(Vector2.Zero);
        var b = ToShape(Size);
        for (float x = Mathf.Floor(a.X / step) * step; x <= b.X; x += step) DrawLine(ToScreen(new Vector2(x, 0)) with { Y = 0 }, ToScreen(new Vector2(x, 0)) with { Y = Size.Y }, Grid);
        for (float y = Mathf.Floor(b.Y / step) * step; y <= a.Y; y += step) DrawLine(ToScreen(new Vector2(0, y)) with { X = 0 }, ToScreen(new Vector2(0, y)) with { X = Size.X }, Grid);
        if (_spec.Kind == ShapeMaker.Lathe) DrawLine(ToScreen(Vector2.Zero) with { Y = 0 }, ToScreen(Vector2.Zero) with { Y = Size.Y }, Axis, 1);
        int n = _spec.Count;
        if (n == 0) return;
        var pts = Enumerable.Range(0, n).Select(i => ToScreen(At(i))).ToArray();
        for (int i = 0; i + 1 < n; i++) DrawLine(pts[i], pts[i + 1], Line, 2, true);
        if (_spec.Kind == ShapeMaker.Extrude && n > 2) DrawLine(pts[^1], pts[0], Line, 2, true);
        if (_spec.Kind == ShapeMaker.Sweep)
            for (int i = 0; i < n; i++) DrawArc(pts[i], _spec.Points[i * 4 + 3] * _scale, 0, Mathf.Tau, 32, Grid with { A = 0.35f }, 1, true);
        for (int i = 0; i < n; i++)
        {
            DrawCircle(pts[i], PointRadius + 1.5f, new Color(0, 0, 0, 0.6f));
            DrawCircle(pts[i], PointRadius, i == Chosen ? Dot : Colors.White);
        }
    }

    int Near(Vector2 s)
    {
        int best = -1;
        float bestD = PickReach;
        for (int i = 0; i < _spec.Count; i++)
        {
            float d = ToScreen(At(i)).DistanceTo(s);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public override void _GuiInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } mb:
                if (mb.DoubleClick && Insert(mb.Position)) { AcceptEvent(); return; }
                _drag = Near(mb.Position);
                if (_drag >= 0) { Chosen = _drag; Picked?.Invoke(); }
                QueueRedraw();
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }:
                if (_drag >= 0) { _drag = -1; Changed?.Invoke(_spec.Copy()); }
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true } rb:
                if (Near(rb.Position) is >= 0 and var gone && _spec.Count > (_spec.Kind == ShapeMaker.Extrude ? 3 : 2))
                {
                    var l = _spec.Points.ToList();
                    l.RemoveRange(gone * _spec.Stride, _spec.Stride);
                    _spec.Points = l.ToArray();
                    Chosen = Math.Clamp(Chosen, 0, _spec.Count - 1);
                    Changed?.Invoke(_spec.Copy());
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion mm when _drag >= 0:
                var p = ToShape(mm.Position);
                if (_spec.Kind == ShapeMaker.Lathe) p.X = Mathf.Max(0, p.X);
                _spec.Points[_drag * _spec.Stride] = Mathf.Snapped(p.X, 0.001f);
                _spec.Points[_drag * _spec.Stride + 1] = Mathf.Snapped(p.Y, 0.001f);
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    bool Insert(Vector2 s)
    {
        int n = _spec.Count, best = -1;
        float bestD = PickReach;
        int segs = _spec.Kind == ShapeMaker.Extrude ? n : n - 1;
        for (int i = 0; i < segs; i++)
        {
            var a = ToScreen(At(i));
            var b = ToScreen(At((i + 1) % n));
            float d = s.DistanceTo(Geometry2D.GetClosestPointToSegment(s, a, b));
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best < 0) return false;
        var q = ToShape(s);
        var add = new List<float> { Mathf.Snapped(q.X, 0.001f), Mathf.Snapped(q.Y, 0.001f) };
        if (_spec.Stride == 4)
        {
            int a = best * 4, b = (best + 1) % n * 4;
            add.Add((_spec.Points[a + 2] + _spec.Points[b + 2]) / 2);
            add.Add((_spec.Points[a + 3] + _spec.Points[b + 3]) / 2);
        }
        var l = _spec.Points.ToList();
        l.InsertRange((best + 1) * _spec.Stride, add);
        _spec.Points = l.ToArray();
        Chosen = best + 1;
        Changed?.Invoke(_spec.Copy());
        return true;
    }
}
