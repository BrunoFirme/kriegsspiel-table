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

    public enum Handle { None, Rotate, Scale, AlongX, AlongY, Up }
    public Handle Held { get; private set; }
    public bool Dragging => Held != Handle.None;
    Vector3 _center;
    float _radius;
    float _startAngle, _startDist;
    readonly Dictionary<string, (Vec2 pos, double rot, double scale)> _origin = new();
    SheetNode? _sheet;
    (double rot, Size2 size) _sheetOrigin;
    double _sheetPreviewRot, _sheetPreviewK = 1;
    Vec2 _sheetStep, _grabGround;
    float _grabUp;
    Vector3 _liftStand;
    readonly Dictionary<string, (float Foot, double Aloft, double To)> _lifted = new();
    public SheetNode? Sheet => _sheets.Selected is { } s && !s.Def.Locked ? s : null;

    static readonly Color RingInk = new(1, 0.93f, 0.22f, 1);
    const float RingLeast = 64, ArrowHalf = 0.24f, ArrowHead = 1.6f, ArrowBarb = 2.6f, ArrowReach = 9, SteepView = 0.97f;
    const float ArrowClear = 3, ArrowLeast = 4.5f, ArrowMost = 8, GroundShare = 0.7f, GrabFrom = 0.4f, UpGrabFrom = 0.68f, CasingWide = 0.14f, LetterOff = 10;
    const int LetterSize = 11;
    const string LetterKey = "ring-arrow-";
    static readonly Handle[] EveryArrow = { Handle.AlongX, Handle.AlongY, Handle.Up };

    static Vector3 Way(Handle arrow) => arrow switch { Handle.AlongX => Vector3.Right, Handle.AlongY => Vector3.Forward, _ => Vector3.Up };

    static string Letter(Handle arrow) => arrow switch { Handle.AlongX => "X", Handle.AlongY => "Y", _ => "Z" };

    bool CanLift => Sheet == null && _selection.Nodes.Any(PieceLift.Can) && Mathf.Abs(Rig.Cam.GlobalBasis.Z.Y) < SteepView;

    public IEnumerable<Handle> Arrows(bool sheet)
    {
        yield return Handle.AlongX;
        yield return Handle.AlongY;
        if (!sheet && _selection.Nodes.Any() && CanLift) yield return Handle.Up;
    }

    public (Vector3 From, Vector3 Neck, Vector3 Tip) ArrowWorld(Handle arrow)
    {
        float k = KnobSize(), tallest = Mathf.Clamp(_radius - k * ArrowClear, k * ArrowLeast, k * ArrowMost);
        var foot = Seated(_center);
        var way = Way(arrow);
        if (arrow != Handle.Up) return (foot, foot + way * (tallest * GroundShare - k * ArrowHead), foot + way * tallest * GroundShare);
        float onScreen = (Rig.Cam.UnprojectPosition(foot + Vector3.Up * k) - Rig.Cam.UnprojectPosition(foot)).Length() / KnobPixels;
        float stretch = 1 / Mathf.Max(onScreen, LeastUpOnScreen);
        return (foot, foot + way * (tallest - k * ArrowHead) * stretch, foot + way * tallest * stretch);
    }

    public (Vector3 From, Vector3 Tip) Grip(Handle arrow)
    {
        var (from, _, tip) = ArrowWorld(arrow);
        return (from.Lerp(tip, arrow == Handle.Up ? UpGrabFrom : GrabFrom), tip);
    }

    void DrawArrow(ImmediateMesh mesh, Handle arrow)
    {
        var (from, neck, tip) = ArrowWorld(arrow);
        var across = (tip - from).Cross(Rig.Cam.GlobalPosition - from);
        if (across.LengthSquared() < 1e-8f) return;
        float k = KnobSize();
        Vector3 way = (tip - from).Normalized(), side = across.Normalized() * k * ArrowHalf, edge = across.Normalized() * k * CasingWide;
        int over = arrow switch { Handle.Up => 0, Handle.AlongY => 2, _ => 4 };
        Shape(mesh, SelectionAura.Casing, over, from, neck - way * k * CasingWide, tip + way * k * CasingWide * 2, side + edge, side * ArrowBarb + edge * 2);
        Shape(mesh, RingInk, over + 1, from, neck, tip, side, side * ArrowBarb);
        var onScreen = Rig.Cam.UnprojectPosition(tip) - Rig.Cam.UnprojectPosition(from);
        var along = onScreen.Normalized();
        var beside = new Vector2(along.Y, -along.X) * (arrow == Handle.Up ? -LetterOff : LetterOff);
        _c.Labels.Set(LetterKey + Letter(arrow), tip, Letter(arrow), RingInk, LetterSize, beside - along * LetterOff / 2);
    }

    void Letters(IEnumerable<Handle> shown)
    {
        foreach (var arrow in EveryArrow.Except(shown)) _c.Labels.Remove(LetterKey + Letter(arrow));
    }

    static void Shape(ImmediateMesh mesh, Color ink, int over, Vector3 from, Vector3 neck, Vector3 tip, Vector3 side, Vector3 barb)
    {
        var mat = new StandardMaterial3D
        {
            AlbedoColor = ink, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = over,
        };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, mat);
        foreach (var v in new[] { from - side, from + side, neck + side, from - side, neck + side, neck - side, neck - barb, neck + barb, tip }) mesh.SurfaceAddVertex(v);
        mesh.SurfaceEnd();
    }

    static float Off(Vector2 p, Vector2 a, Vector2 b)
    {
        var along = b - a;
        float t = along.LengthSquared() < 1e-6f ? 0 : Mathf.Clamp((p - a).Dot(along) / along.LengthSquared(), 0, 1);
        return (p - (a + along * t)).Length();
    }

    float? UpAt(Vector2 screen)
    {
        Vector3 from = Rig.Cam.ProjectRayOrigin(screen), ray = Rig.Cam.ProjectRayNormal(screen), to = _liftStand - from;
        float along = Vector3.Up.Dot(ray), apart = 1 - along * along;
        if (apart < 1e-4f) return null;
        float above = Vector3.Up.Dot(to), ahead = ray.Dot(to);
        return ahead - along * above <= 0 ? float.NegativeInfinity : (along * ahead - above) / apart;
    }

    CameraRig Rig => _c.Rig;

    public void Draw()
    {
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        if (_c.Tool != Tool.Transform || _selectTool.BoxSelecting || nodes.Count == 0 && sheet == null)
        {
            Letters(Array.Empty<Handle>());
            return;
        }
        Measure(nodes, sheet);
        Letters(Arrows(sheet != null));
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
        foreach (var arrow in Arrows(sheet != null)) DrawArrow(mesh, arrow);
    }

    float Lift => _overlay.Lift;

    const int RingSegments = 72;

    Vector3 Seated(Vector3 w) => w with { Y = _c.SheetsTop + Lift + (float)_c.S.Ground.At(Coords.FromWorld(w)) };

    const float KnobPixels = 8, LeastUpOnScreen = 0.25f;

    public float KnobSize() => Mathf.Max(0.05f, KnobPixels / Mathf.Max(0.01f, Rig.PixelsPerUnit()));

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
        _radius = Mathf.Max(r * 1.15f, RingLeast / Mathf.Max(0.01f, Rig.PixelsPerUnit()));
    }

    void MeasureSheet(SheetNode s)
    {
        if (Held != Handle.None) return;
        _center = s.Position with { Y = 0 };
        float r = new Vector2((float)s.Def.Size.W, (float)s.Def.Size.H).Length() / 2 * s.Scale.X;
        _radius = Mathf.Max(r * 1.06f, RingLeast / Mathf.Max(0.01f, Rig.PixelsPerUnit()));
    }

    public Vector3 RotateKnobWorld() => Seated(_center + new Vector3(0, 0, -_radius).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));
    public Vector3 ScaleKnobWorld() => Seated(_center + new Vector3(_radius * 0.7071f, 0, _radius * 0.7071f).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));

    public Handle At(Vector2 screen)
    {
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        if (nodes.Count == 0 && sheet == null) return Handle.None;
        Measure(nodes, sheet);
        var nearest = (Handle: Handle.None, Off: 1f);
        void Offer(Handle handle, float off, float reach) { if (off / reach < nearest.Off) nearest = (handle, off / reach); }
        Offer(Handle.Rotate, (Rig.Cam.UnprojectPosition(RotateKnobWorld()) - screen).Length(), KnobReach);
        Offer(Handle.Scale, (Rig.Cam.UnprojectPosition(ScaleKnobWorld()) - screen).Length(), KnobReach);
        foreach (var arrow in Arrows(sheet != null))
        {
            var (from, tip) = Grip(arrow);
            Offer(arrow, Off(screen, Rig.Cam.UnprojectPosition(from), Rig.Cam.UnprojectPosition(tip)), ArrowReach);
        }
        return nearest.Handle;
    }

    const float KnobReach = 16;

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
        _sheetStep = default;
        _origin.Clear();
        foreach (var n in _selection.Nodes) if (!n.Piece.Locked) _origin[n.Piece.Id] = _poses.Current(n);
        _lifted.Clear();
        if (h == Handle.Up)
            foreach (var n in _selection.Nodes.Where(PieceLift.Can)) _lifted[n.Piece.Id] = (n.Position.Y - (float)n.Piece.Aloft, n.Piece.Aloft, n.Piece.Aloft);
        _liftStand = ArrowWorld(Handle.Up).From;
        _grabUp = UpAt(screen) is { } grabbed && float.IsFinite(grabbed) ? grabbed : 0;
        var g = Rig.GroundAt(screen) ?? _center;
        _grabGround = Coords.FromWorld(g);
        var d = g - _center;
        _startAngle = Mathf.Atan2(d.Z, d.X);
        _startDist = Mathf.Max(0.001f, new Vector2(d.X, d.Z).Length());
    }

    void Raise(Vector2 screen, bool snap)
    {
        if (UpAt(screen) is not { } up) return;
        double said = 0;
        foreach (var n in _selection.Nodes)
        {
            if (!_lifted.TryGetValue(n.Piece.Id, out var was)) continue;
            double high = PieceLift.Settled(was.Aloft + up - _grabUp, snap);
            _lifted[n.Piece.Id] = was with { To = high };
            n.Position = n.Position with { Y = was.Foot + (float)high };
            said = Math.Max(said, high);
        }
        _c.Say(_lifted.Count == 0 ? "Locked pieces are pinned down · right-click → Locked to let them go." : $"{PieceLift.Said(said)} — Shift snaps to {PieceLift.SnapStep}");
    }

    void Along(Vec2 at, bool snap)
    {
        var moved = at - _grabGround;
        double by = Held == Handle.AlongX ? moved.X : moved.Z;
        if (snap) by = Math.Round(by);
        var step = Held == Handle.AlongX ? new Vec2(by, 0) : new Vec2(0, by);
        if (_sheet != null)
        {
            _sheets.Dragged = true;
            _sheetStep = step;
            _sheet.Position = Coords.ToWorld(_sheet.Def.Pos + step, _sheet.Def.Z * SheetNode.ZStep);
        }
        else if (_origin.Count == 0)
        {
            _c.Say("Locked pieces are pinned down · right-click → Locked to let them go.");
            return;
        }
        else
            foreach (var n in _selection.Nodes)
                if (_origin.TryGetValue(n.Piece.Id, out var o)) _poses.Set(n, o.pos + step, o.rot, o.scale, 1e9);
        _c.Say($"Along {(Held == Handle.AlongX ? "X" : "Y")} {by:+0.##;-0.##;0} u — Shift snaps to whole units");
    }

    public void Drag(Vector2 screen, bool snap)
    {
        if (Held == Handle.Up) { Raise(screen, snap); return; }
        var g = Rig.GroundAt(screen);
        if (g == null) return;
        if (Held is Handle.AlongX or Handle.AlongY) { Along(Coords.FromWorld(g.Value), snap); return; }
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

    void Land()
    {
        var lifts = new List<(PieceNode Node, double High)>();
        foreach (var n in _selection.Nodes)
        {
            if (!_lifted.TryGetValue(n.Piece.Id, out var was)) continue;
            n.Position = n.Position with { Y = was.Foot + (float)was.Aloft };
            lifts.Add((n, was.To));
        }
        _lifted.Clear();
        if (PieceLift.To(lifts) is { } lifted) _c.S.Do(lifted);
    }

    public void End()
    {
        bool lifting = Held == Handle.Up;
        Held = Handle.None;
        if (lifting)
        {
            Land();
            _c.NotifySelectionChanged();
            return;
        }
        if (_sheet != null)
        {
            var s = _sheet;
            _sheet = null;
            s.Scale = Vector3.One;
            _sheets.Dragged = false;
            bool resized = Math.Abs(_sheetPreviewK - 1) > 1e-6;
            _sheets.Move(s.Def, s.Def.Pos + _sheetStep, Table.Norm360(_sheetPreviewRot), resized ? new Size2(_sheetOrigin.size.W * _sheetPreviewK, _sheetOrigin.size.H * _sheetPreviewK) : null);
            _c.NotifySelectionChanged();
            return;
        }
        _poses.Flush();
        _c.NotifySelectionChanged();
    }
}
