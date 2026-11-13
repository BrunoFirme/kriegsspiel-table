using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

sealed class Gizmo : IKnobDrag
{
    readonly TableContext _c;
    readonly SheetLayer _sheets;
    readonly ToolOverlay _overlay;
    readonly Selection _selection;
    readonly PendingPoses _poses;
    public Gizmo(TableContext c, SheetLayer sheets, ToolOverlay overlay, Selection selection, PendingPoses poses)
    {
        _c = c;
        _sheets = sheets;
        _overlay = overlay;
        _selection = selection;
        _poses = poses;
    }

    SelectTool _selectTool = null!;
    public void Connect(SelectTool selectTool) => _selectTool = selectTool;

    public enum Handle { None, Rotate, Scale }
    public Handle Held { get; private set; }
    public bool Dragging => Held != Handle.None;
    Vector3 _center;
    float _radius;
    float _startAngle, _startDist;
    readonly Dictionary<string, (Vec2 pos, double rot, double scale)> _origin = new();
    SheetNode? _sheet;
    (double rot, Size2 size) _sheetOrigin;
    double _sheetPreviewRot, _sheetPreviewK = 1;
    public SheetNode? Sheet => _sheets.Selected is { } s && !s.Def.Locked ? s : null;

    CameraRig Rig => _c.Rig;

    public void Draw()
    {
        if (_c.Tool != Tool.Transform || _selectTool.BoxSelecting) return;
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        if (nodes.Count == 0 && sheet == null) return;
        Measure(nodes, sheet);
        var mesh = _overlay.Mesh;
        var mat =
            new StandardMaterial3D { AlbedoColor = new Color(1, 0.93f, 0.22f, 1), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        float k = KnobSize();
        var ring = new List<Vec2>(RingSegments + 1);
        for (int i = 0; i <= RingSegments; i++)
        {
            var a = Mathf.Tau * i / RingSegments;
            ring.Add(Coords.FromWorld(_center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * _radius));
        }
        Ribbon.Draw(mesh, mat, ring, k * 0.44f, _c.SheetsTop + Lift, _c.S.Ground);
        var rk = RotateKnobWorld();
        var sk = ScaleKnobWorld();
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        for (int i = 0; i < 20; i++)
        {
            var a0 = Mathf.Tau * i / 20;
            var a1 = Mathf.Tau * (i + 1) / 20;
            mesh.SurfaceAddVertex(rk);
            mesh.SurfaceAddVertex(rk + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * k);
            mesh.SurfaceAddVertex(rk + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * k);
        }
        foreach (var (dx, dz) in new[] { (-1, -1), (1, -1), (1, 1), (-1, -1), (1, 1), (-1, 1) })
            mesh.SurfaceAddVertex(sk + new Vector3(dx * k * 0.85f, 0, dz * k * 0.85f));
        mesh.SurfaceEnd();
    }

    const float Lift = 0.4f;
    const int RingSegments = 72;

    Vector3 Seated(Vector3 w) => w with { Y = _c.SheetsTop + Lift + (float)_c.S.Ground.At(Coords.FromWorld(w)) };

    public float KnobSize() => Mathf.Max(0.05f, 8f / Mathf.Max(0.01f, Rig.PixelsPerUnit()));

    public void Measure(List<PieceNode> nodes, SheetNode? sheet)
    {
        if (sheet != null) MeasureSheet(sheet);
        else MeasurePieces(nodes);
    }

    void MeasurePieces(List<PieceNode> nodes)
    {
        if (Held != Handle.None) return;
        static Vector3 Flat(PieceNode n) => n.Position with { Y = 0 };
        var c = nodes.Aggregate(Vector3.Zero, (a, n) => a + Flat(n)) / nodes.Count;
        float r = nodes.Max(n => (Flat(n) - c).Length() + (n.FootprintCentre.Length() + n.Footprint.Length() / 2) * n.Scale.X);
        _center = c with { Y = 0 };
        _radius = Mathf.Max(r * 1.15f, 18f / Mathf.Max(0.01f, Rig.PixelsPerUnit()));
    }

    void MeasureSheet(SheetNode s)
    {
        if (Held != Handle.None) return;
        _center = s.Position with { Y = 0 };
        float r = new Vector2((float)s.Def.Size.W, (float)s.Def.Size.H).Length() / 2 * s.Scale.X;
        _radius = Mathf.Max(r * 1.06f, 18f / Mathf.Max(0.01f, Rig.PixelsPerUnit()));
    }

    public Vector3 RotateKnobWorld() => Seated(_center + new Vector3(0, 0, -_radius).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));
    public Vector3 ScaleKnobWorld() => Seated(_center + new Vector3(_radius * 0.7071f, 0, _radius * 0.7071f).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));

    public Handle At(Vector2 screen)
    {
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        if (nodes.Count == 0 && sheet == null) return Handle.None;
        Measure(nodes, sheet);
        if ((Rig.Cam.UnprojectPosition(RotateKnobWorld()) - screen).Length() < 16) return Handle.Rotate;
        if ((Rig.Cam.UnprojectPosition(ScaleKnobWorld()) - screen).Length() < 16) return Handle.Scale;
        return Handle.None;
    }

    public void Begin(Handle h, Vector2 screen)
    {
        Held = h;
        _sheet = _selection.Nodes.Any() ? null : Sheet;
        if (_sheet != null)
        {
            _sheetOrigin = (_sheet.Def.RotDeg, _sheet.Def.Size);
            _sheetPreviewRot = _sheetOrigin.rot;
            _sheetPreviewK = 1;
        }
        _origin.Clear();
        foreach (var n in _selection.Nodes) if (!n.Piece.Locked) _origin[n.Piece.Id] = _poses.Current(n);
        var g = Rig.GroundAt(screen) ?? _center;
        var d = g - _center;
        _startAngle = Mathf.Atan2(d.Z, d.X);
        _startDist = Mathf.Max(0.001f, new Vector2(d.X, d.Z).Length());
    }

    public void Drag(Vector2 screen, bool snap)
    {
        var g = Rig.GroundAt(screen);
        if (g == null) return;
        var d = g.Value - _center;
        if (_sheet != null) { DragSheet(_sheet, d, snap); return; }
        if (_origin.Count == 0) { _c.Say("Locked pieces are pinned down · right-click → Locked to let them go."); return; }
        var center = Coords.FromWorld(_center);
        if (Held == Handle.Rotate)
        {
            double delta = Mathf.RadToDeg(Mathf.Wrap(Mathf.Atan2(d.Z, d.X) - _startAngle, -Mathf.Pi, Mathf.Pi));
            if (snap) delta = Math.Round(delta / 15) * 15;
            var rad = delta * Math.PI / 180;
            foreach (var n in _selection.Nodes)
            {
                if (!_origin.TryGetValue(n.Piece.Id, out var o)) continue;
                var off = o.pos - center;
                var rot = new Vec2(off.X * Math.Cos(rad) + off.Z * Math.Sin(rad), -off.X * Math.Sin(rad) + off.Z * Math.Cos(rad));
                _poses.Set(n, center + rot, o.rot + delta, o.scale, 1e9);
            }
            _c.Say($"Rotate {delta:+0;-0;0}° — Shift snaps to 15°");
        }
        else
        {
            double k = Math.Clamp(new Vector2(d.X, d.Z).Length() / _startDist, 0.05, 20);
            foreach (var n in _selection.Nodes)
            {
                if (!_origin.TryGetValue(n.Piece.Id, out var o)) continue;
                var sc = o.scale * k;
                if (snap) sc = Math.Max(0.1, Math.Round(sc * 10) / 10);
                _poses.Set(n, o.pos, o.rot, sc, 1e9);
            }
            _c.Say($"Scale ×{k:0.00} — Shift snaps to 0.1");
        }
    }

    void DragSheet(SheetNode sheet, Vector3 d, bool snap)
    {
        if (Held == Handle.Rotate)
        {
            double delta = Mathf.RadToDeg(Mathf.Wrap(Mathf.Atan2(d.Z, d.X) - _startAngle, -Mathf.Pi, Mathf.Pi));
            if (snap) delta = Math.Round(delta / 15) * 15;
            _sheetPreviewRot = _sheetOrigin.rot + delta;
            sheet.Rotation = new Vector3(0, Coords.Yaw(_sheetPreviewRot), 0);
            _c.Say($"Turn the sheet {delta:+0;-0;0}° — Shift snaps to 15°");
        }
        else
        {
            double k = Math.Clamp(new Vector2(d.X, d.Z).Length() / _startDist, 0.05, 20);
            if (snap) k = Math.Max(0.1, Math.Round(k * 10) / 10);
            _sheetPreviewK = k;
            sheet.Scale = new Vector3((float)k, 1, (float)k);
            _c.Say($"Resize the sheet ×{k:0.00}: {_sheetOrigin.size.W * k:0.#} × {_sheetOrigin.size.H * k:0.#} u — Shift snaps to 0.1");
        }
    }

    public void End()
    {
        Held = Handle.None;
        if (_sheet != null)
        {
            var s = _sheet;
            _sheet = null;
            s.Scale = Vector3.One;
            bool resized = Math.Abs(_sheetPreviewK - 1) > 1e-6;
            _sheets.Move(s.Def, s.Def.Pos, Table.Norm360(_sheetPreviewRot), resized ? new Size2(_sheetOrigin.size.W * _sheetPreviewK, _sheetOrigin.size.H * _sheetPreviewK) : null);
            _c.NotifySelectionChanged();
            return;
        }
        _poses.Flush();
        _c.NotifySelectionChanged();
    }
}
