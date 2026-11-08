using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class PenTool
{
    const float PreviewLift = 0.05f;
    readonly TableContext _c;
    readonly ToolOverlay _overlay;
    readonly DrawingLayer _drawings;
    public PenTool(TableContext c, ToolOverlay overlay, DrawingLayer drawings)
    {
        _c = c;
        _overlay = overlay;
        _drawings = drawings;
    }

    public string ShapeKind = "line";
    public Color PenColor = new(0.1f, 0.1f, 0.1f);
    public float PenPixels = 3f;
    public float PenOpacity = 1f;

    List<Vec2>? _stroke;
    Vec2? _shapeA, _shapeB;
    Vec2 _across = new(1, 0);
    Vec2? _rubbedAt;

    List<Vec2>? _knots;
    string _knotsKind = "";
    const double ClosePixels = 10, SamePixels = 3, LeastBend = 0.05;

    public bool Stroking => _stroke != null;
    public int? StrokePoints => _stroke?.Count;
    public bool Shaping => _shapeA != null || _knots != null;
    public Vec2? ShapeEnd => _shapeB;
    public int Knots => _knots?.Count ?? 0;

    ToolOverlay Overlay => _overlay;

    public void PressDraw(Vec2 g) => _stroke = new() { g };
    public void PressShape(Vec2 g, bool twice = false)
    {
        if (Polyline.IsPath(ShapeKind)) { PressPath(g, twice); return; }
        _shapeA = g;
        _shapeB = g;
        _across = ScreenAcross(_c.Rig.Cam);
    }

    void PressPath(Vec2 g, bool twice)
    {
        if (_knots != null && _knotsKind != ShapeKind) _knots = null;
        if (_knots == null)
        {
            _knots = new() { g };
            _knotsKind = ShapeKind;
            return;
        }
        double perPixel = 1 / Math.Max(0.01, _c.Rig.PixelsPerUnit()), close = ClosePixels * perPixel;
        if (ShapeKind == Polyline.Area && _knots.Count >= 3 && (g - _knots[0]).Length <= close) { FinishPath(); return; }
        if ((g - _knots[^1]).Length > SamePixels * perPixel) _knots.Add(g);
        if (twice) FinishPath();
    }

    public bool PathKey(InputEventKey k)
    {
        if (_knots == null || !k.Pressed) return false;
        if (k.Keycode is Key.Enter or Key.KpEnter) { FinishPath(); return true; }
        if (k.Keycode != Key.Backspace) return false;
        _knots.RemoveAt(_knots.Count - 1);
        if (_knots.Count == 0) _knots = null;
        return true;
    }

    public void FinishPath()
    {
        var knots = _knots;
        _knots = null;
        if (knots == null || Polyline.Length(knots) < LeastBend) return;
        bool area = _knotsKind == Polyline.Area;
        if (knots.Count < (area ? 3 : 2)) return;
        var body = Polyline.Smooth(knots, area);
        var head = _knotsKind == Polyline.CurvedArrow ? Polyline.PathHead(body) : null;
        _c.S.Do(head == null ? Laid(body, area) : new Batch { Items = new List<Command> { Laid(body), Laid(head) } });
    }

    Draw Laid(List<Vec2> pts, bool fill = false) =>
        new() { Layer = Layers.Umpire, Stroke = new Stroke { Points = pts, Color = StrokeHex(), Width = PenWidth(), Board = _drawings.DrawBoard(), Fill = fill } };

    List<Vec2>? PathInHand()
    {
        if (_knots == null || _c.Tool != Tool.Shape || _knotsKind != ShapeKind) return null;
        var pts = _knots.ToList();
        if (_c.Ground(Pointer.In(_c.Scene.GetViewport())) is { } at && (at - pts[^1]).Length > 1e-6) pts.Add(at);
        return pts;
    }

    public static Vec2 ScreenAcross(Camera3D cam)
    {
        var across = Coords.FromWorld(cam.GlobalPosition + cam.GlobalBasis.X) - Coords.FromWorld(cam.GlobalPosition);
        return across.Length > 1e-6 ? across : new Vec2(1, 0);
    }
    public void PressErase(Vec2 g) { _rubbedAt = null; RubAt(g); }

    public void MoveDraw(Vec2 g) { if (_stroke != null) _stroke.Add(g); }
    public void MoveShape(Vec2 g)
    {
        if (_shapeA != null) _shapeB = g;
        else if (_knots is { Count: 1 }) _knots.Add(g);
        else if (_knots != null) _knots[^1] = g;
    }

    public void ReleaseDraw()
    {
        if (_stroke != null && _stroke.Count >= 2)
            _c.S.Do(new Draw { Layer = Layers.Umpire, Stroke = new Stroke { Points = Polyline.Simplify(_stroke), Color = StrokeHex(), Width = PenWidth(), Board = _drawings.DrawBoard() } });
        _stroke = null;
    }

    public void ReleaseShape()
    {
        if (_shapeA is { } sa && _shapeB is { } sb && (sb - sa).Length > 0.05)
        {
            var body = Laid(Polyline.ShapePoints(ShapeKind, sa, sb, _across));
            var head = Polyline.ShapeHead(ShapeKind, sa, sb);
            _c.S.Do(head == null ? body : new Batch { Items = new List<Command> { body, Laid(head) } });
        }
        _shapeA = _shapeB = null;
    }

    public void Drop()
    {
        _stroke = null;
        _shapeA = _shapeB = null;
        _knots = null;
        _rubbedAt = null;
    }

    public void DrawInHand()
    {
        var mesh = Overlay.Mesh;
        if (_stroke != null && _stroke.Count >= 2)
            Ribbon.Draw(mesh, PenMaterial(), _stroke, PenWidth(), _c.SheetsTop + PreviewLift);
        if (_shapeA is { } pa && _shapeB is { } pb && (pb - pa).Length > 0.01)
        {
            Ribbon.Draw(mesh, PenMaterial(), Polyline.ShapePoints(ShapeKind, pa, pb, _across), PenWidth(), _c.SheetsTop + PreviewLift);
            if (Polyline.ShapeHead(ShapeKind, pa, pb) is { } hd) Ribbon.Draw(mesh, PenMaterial(), hd, PenWidth(), _c.SheetsTop + PreviewLift);
        }
        if (PathInHand() is { Count: >= 2 } path)
        {
            bool area = _knotsKind == Polyline.Area;
            var body = Polyline.Smooth(path, area && path.Count >= 3);
            if (area && path.Count >= 3)
            {
                var wash = DrawingLayer.Ink(DrawingLayer.Washed(StrokeColor()));
                wash.NoDepthTest = true;
                Ribbon.Fill(mesh, wash, body, _c.SheetsTop + PreviewLift);
            }
            Ribbon.Draw(mesh, PenMaterial(), body, PenWidth(), _c.SheetsTop + PreviewLift);
            if (_knotsKind == Polyline.CurvedArrow && Polyline.PathHead(body) is { } ph) Ribbon.Draw(mesh, PenMaterial(), ph, PenWidth(), _c.SheetsTop + PreviewLift);
        }
        DrawTip();
    }

    StandardMaterial3D PenMaterial() =>
        new() { AlbedoColor = StrokeColor(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };

    void DrawTip()
    {
        if (_c.Tool is not (Tool.Draw or Tool.Shape or Tool.Erase)) return;
        if (_c.Ground(Pointer.In(_c.Scene.GetViewport())) is not { } at) return;
        bool rub = _c.Tool == Tool.Erase;
        double r = Math.Max(rub ? RubRadius() : PenWidth() / 2, 2.5 / Math.Max(0.01, _c.Rig.PixelsPerUnit()));
        var mat = new StandardMaterial3D
        {
            AlbedoColor = rub ? new Color(1, 1, 1, 0.85f) : new Color(StrokeColor(), 0.9f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        };
        var ring = new List<Vec2>();
        for (int i = 0; i <= 36; i++)
        {
            double t = i * Math.Tau / 36;
            ring.Add(new Vec2(at.X + Math.Cos(t) * r, at.Z + Math.Sin(t) * r));
        }
        Overlay.RibbonPx(Overlay.Mesh, mat, ring, 0.06f, 2f);
    }

    Color StrokeColor() => new(PenColor, PenOpacity);

    string StrokeHex() => "#" + StrokeColor().ToHtml(PenOpacity < 1);

    double PenWidth() => Math.Max(0.04, PenPixels / Math.Max(0.01, _c.Rig.PixelsPerUnit()));

    public double RubRadius() => Math.Max(15.0, PenPixels * 2.1) / Math.Max(0.01, _c.Rig.PixelsPerUnit());

    public void RubAt(Vec2 p)
    {
        double r = RubRadius();
        if (_rubbedAt is { } was && (p - was).Length < r * 0.33) return;
        _rubbedAt = p;
        var drawings = _drawings;
        var view = _c.S.View;
        var cmds = new List<Command>();
        foreach (var (layer, strokes) in view.Drawings)
        {
            if (!_c.S.CanEdit(layer) || !_c.LayerShown(layer)) continue;
            foreach (var st in strokes.ToList())
            {
                if (!drawings.OnDrawBoard(st.Board) || !(Polyline.Near(st.Points, p, r) || st.Fill && Polyline.Inside(st.Points, p))) continue;
                cmds.Add(new EraseStroke { Layer = layer, StrokeId = st.Id });
                if (st.Text != null || st.Fill) continue;
                foreach (var piece in Polyline.Cut(st.Points, p, r))
                    cmds.Add(new Draw { Layer = layer, Stroke = new Stroke { Points = piece, Color = st.Color, Width = st.Width, Board = st.Board, Round = st.Round } });
            }
        }
        if (cmds.Count == 0)
        {
            var other = view.Drawings.Values.SelectMany(l => l)
                .FirstOrDefault(st => st.Board.Length > 0 && st.Board != drawings.DrawBoard() && drawings.BoardShown(st.Board) && Polyline.Near(st.Points, p, r));
            if (other != null)
                _c.Say($"That drawing is on {view.Boards.FirstOrDefault(b => b.Id == other.Board)?.Name ?? "another layer"} · pick that layer to rub it out.");
            return;
        }
        _c.S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
    }
}
