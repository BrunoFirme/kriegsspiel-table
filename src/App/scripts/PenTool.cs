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
    public PenTool(TableContext c, ToolOverlay overlay, DrawingLayer drawings) { _c = c; _overlay = overlay; _drawings = drawings; }

    public string ShapeKind = "line";
    public Color PenColor = new(0.1f, 0.1f, 0.1f);
    public float PenPixels = 3f;
    public float PenOpacity = 1f;

    List<Vec2>? _stroke;
    Vec2? _shapeA, _shapeB;
    Vec2? _rubbedAt;

    public bool Stroking => _stroke != null;
    public int? StrokePoints => _stroke?.Count;
    public bool Shaping => _shapeA != null;
    public Vec2? ShapeEnd => _shapeB;

    ToolOverlay Overlay => _overlay;

    public void PressDraw(Vec2 g) => _stroke = new() { g };
    public void PressShape(Vec2 g) { _shapeA = g; _shapeB = g; }
    public void PressErase(Vec2 g) { _rubbedAt = null; RubAt(g); }

    public void MoveDraw(Vec2 g) { if (_stroke != null) _stroke.Add(g); }
    public void MoveShape(Vec2 g) { if (_shapeA != null) _shapeB = g; }

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
            Draw One(List<Vec2> pts) => new() { Layer = Layers.Umpire, Stroke = new Stroke { Points = pts, Color = StrokeHex(), Width = PenWidth(), Board = _drawings.DrawBoard() } };
            var body = One(Polyline.ShapePoints(ShapeKind, sa, sb));
            var head = Polyline.ShapeHead(ShapeKind, sa, sb);
            _c.S.Do(head == null ? body : new Batch { Items = new List<Command> { body, One(head) } });
        }
        _shapeA = _shapeB = null;
    }

    public void Drop() { _stroke = null; _shapeA = _shapeB = null; _rubbedAt = null; }

    public void DrawInHand()
    {
        var mesh = Overlay.Mesh;
        if (_stroke != null && _stroke.Count >= 2)
            Ribbon.Draw(mesh, PenMaterial(), _stroke, PenWidth(), _c.SheetsTop + PreviewLift);
        if (_shapeA is { } pa && _shapeB is { } pb && (pb - pa).Length > 0.01)
        {
            Ribbon.Draw(mesh, PenMaterial(), Polyline.ShapePoints(ShapeKind, pa, pb), PenWidth(), _c.SheetsTop + PreviewLift);
            if (Polyline.ShapeHead(ShapeKind, pa, pb) is { } hd) Ribbon.Draw(mesh, PenMaterial(), hd, PenWidth(), _c.SheetsTop + PreviewLift);
        }
        DrawTip();
    }

    StandardMaterial3D PenMaterial() => new() { AlbedoColor = StrokeColor(), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };

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
                if (!drawings.OnDrawBoard(st.Board) || !Polyline.Rubs(st.Points, p, r)) continue;
                cmds.Add(new EraseStroke { Layer = layer, StrokeId = st.Id });
                foreach (var piece in Polyline.Cut(st.Points, p, r))
                    cmds.Add(new Draw { Layer = layer, Stroke = new Stroke { Points = piece, Color = st.Color, Width = st.Width, Board = st.Board } });
            }
        }
        if (cmds.Count == 0)
        {
            var other = view.Drawings.Values.SelectMany(l => l)
                .FirstOrDefault(st => st.Board.Length > 0 && st.Board != drawings.DrawBoard() && drawings.BoardShown(st.Board) && Polyline.Rubs(st.Points, p, r));
            if (other != null)
                _c.Say($"That drawing is on {view.Boards.FirstOrDefault(b => b.Id == other.Board)?.Name ?? "another layer"} · pick that layer to rub it out.");
            return;
        }
        _c.S.Do(cmds.Count == 1 ? cmds[0] : new Batch { Items = cmds });
    }
}
