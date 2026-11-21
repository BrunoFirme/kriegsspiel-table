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
    readonly NamePick _names;
    public Gizmo(TableContext c, SheetLayer sheets, ToolOverlay overlay, Selection selection, PendingPoses poses, NamePick names)
    {
        _c = c;
        _sheets = sheets;
        _overlay = overlay;
        _selection = selection;
        _poses = poses;
        _names = names;
    }

    SelectTool _selectTool = null!;
    public void Connect(SelectTool selectTool) => _selectTool = selectTool;

    public enum Handle { None, Rotate, Scale, AlongX, AlongY, Up, SizeX, SizeY, SizeZ }
    public Handle Held { get; private set; }
    public bool Dragging => Held != Handle.None;
    Vector3 _center;
    float _radius, _gripRadius;
    float _startAngle, _startDist;
    readonly Dictionary<string, (Vec2 pos, double rot, double scale)> _origin = new();
    SheetNode? _sheet;
    (double rot, Size2 size) _sheetOrigin;
    double _sheetPreviewRot, _sheetPreviewK = 1;
    Vec2 _sheetStep, _grabGround;
    float _grabUp;
    Vector3 _liftStand;
    readonly Dictionary<string, (float Foot, double Aloft, double To)> _lifted = new();
    (PieceNode Node, Vector3 Way, float Level, Vector3 Grab, double Start, double Least, double Most)? _sizing;
    (Stroke Start, Vec2 Centre, double Letter)? _name;
    Stroke? _nameNow;
    public SheetNode? Sheet => _sheets.Selected;

    Stroke? NameAlone(List<PieceNode> nodes, SheetNode? sheet) => nodes.Count == 0 && sheet == null ? _names.Picked : null;

    static readonly Color RingInk = new(1, 0.93f, 0.22f, 1);
    const float ArrowHead = 1.6f, SteepView = 0.97f;
    static float RingLeast => 64 * Ui.Scale;
    static float ArrowReach => 9 * Ui.Scale;
    const float ArrowClear = 3, ArrowLeast = 4.5f, ArrowMost = 8, GroundShare = 0.7f, GrabFrom = 0.4f, UpGrabFrom = 0.68f, LetterOff = 10;
    const int LetterSize = 11;
    const string LetterKey = "ring-arrow-";
    static readonly string[] EveryLetter = { "X", "Y", "Z" };

    static bool IsUp(Handle arrow) => arrow is Handle.Up or Handle.SizeZ;

    static bool IsSize(Handle h) => h is Handle.SizeX or Handle.SizeY or Handle.SizeZ;

    Vector3 Way(Handle arrow) => arrow switch
    {
        Handle.AlongX => Vector3.Right,
        Handle.AlongY => Vector3.Forward,
        Handle.SizeX or Handle.SizeY when Sized is { } n => SideWay(n, arrow),
        _ => Vector3.Up,
    };

    static string Letter(Handle arrow) => arrow switch { Handle.AlongX or Handle.SizeX => "X", Handle.AlongY or Handle.SizeY => "Y", _ => "Z" };

    bool Upright => Mathf.Abs(Rig.Cam.GlobalBasis.Z.Y) < SteepView;

    bool CanLift => Sheet == null && _selection.Touchable.Any(PieceLift.Can) && Upright;

    public bool Sizing => Dragging ? IsSize(Held) : Input.IsKeyPressed(SizeKey) && Sized != null;

    public const Key SizeKey = Key.Alt;

    public IEnumerable<Handle> Arrows(bool flat)
    {
        if (!flat && Sizing && Sized is { } n)
        {
            yield return Handle.SizeX;
            yield return Handle.SizeY;
            if (PieceStretch.HasHeight(n) && Upright) yield return Handle.SizeZ;
            yield break;
        }
        yield return Handle.AlongX;
        yield return Handle.AlongY;
        if (!flat && _selection.Nodes.Any() && CanLift) yield return Handle.Up;
    }

    public (Vector3 From, Vector3 Neck, Vector3 Tip) ArrowWorld(Handle arrow)
    {
        float k = KnobSize(), tallest = Mathf.Clamp(_radius - k * ArrowClear, k * ArrowLeast, k * ArrowMost);
        var foot = Seated(_center);
        var way = Way(arrow);
        if (!IsUp(arrow)) return (foot, foot + way * (tallest * GroundShare - k * ArrowHead), foot + way * tallest * GroundShare);
        var (_, tip, stretch) = UpRun(foot, k, tallest);
        return (foot, foot + way * (tip - k * ArrowHead * stretch), foot + way * tip);
    }

    float UpStretch(Vector3 foot, float k) => 1 / Mathf.Max((Rig.Screen(foot + Vector3.Up * k) - Rig.Screen(foot)).Length() / KnobPixels, LeastUpOnScreen);

    (float Grip, float Tip, float Stretch) UpRun(Vector3 foot, float k, float tallest)
    {
        float stretch = UpStretch(foot, k), tip = tallest * stretch;
        return (tip * UpGrabFrom, tip, stretch);
    }

    public (Vector3 From, Vector3 Tip) Grip(Handle arrow)
    {
        var (from, _, tip) = ArrowWorld(arrow);
        if (!IsUp(arrow)) return (from.Lerp(tip, GrabFrom), tip);
        float k = KnobSize();
        return (from + Vector3.Up * UpRun(from, k, Mathf.Clamp(_radius - k * ArrowClear, k * ArrowLeast, k * ArrowMost)).Grip, tip);
    }

    const double SizeSnap = 0.1;

    PieceNode? Sized => _selection.Nodes.Take(2).ToList() is [var one] && _selection.Touchable.Contains(one) ? one : null;

    static Vector3 SideWay(PieceNode n, Handle side)
    {
        var near = side == Handle.SizeX ? Vector3.Right : Vector3.Forward;
        var way = (side == Handle.SizeX ? n.Transform.Basis.X : n.Transform.Basis.Z) with { Y = 0 };
        if (way.LengthSquared() < 1e-8f) return near;
        way = way.Normalized();
        return way.Dot(near) < 0 ? -way : way;
    }

    Vector3? PlaneAt(Vector2 screen, float level)
    {
        Vector3 from = Rig.RayFrom(screen), ray = Rig.Cam.ProjectRayNormal(screen);
        if (Mathf.Abs(ray.Y) < 1e-5f) return null;
        float along = (level - from.Y) / ray.Y;
        return along > 0 ? from + ray * along : null;
    }

    void BeginSize(PieceNode n, Handle side, Vector2 screen)
    {
        var foot = ArrowWorld(side).From;
        double now = side == Handle.SizeX ? n.Footprint.X : side == Handle.SizeY ? n.Footprint.Y : n.Thickness;
        double by = side == Handle.SizeX ? n.Piece.WideBy : side == Handle.SizeY ? n.Piece.DeepBy : n.Piece.TallBy;
        var (least, most) = PendingPoses.StretchReach(now, by);
        _sizing = (n, Way(side), foot.Y, PlaneAt(screen, foot.Y) ?? foot, now, least, most);
    }

    void Resize((PieceNode Node, Vector3 Way, float Level, Vector3 Grab, double Start, double Least, double Most) sizing, Vector2 screen, bool snap)
    {
        if (!GodotObject.IsInstanceValid(sizing.Node)) return;
        double to;
        if (Held == Handle.SizeZ)
        {
            if (UpAt(screen) is not { } up) return;
            to = sizing.Start + up - _grabUp;
        }
        else
        {
            if (PlaneAt(screen, sizing.Level) is not { } at) return;
            to = sizing.Start + 2 * (at - sizing.Grab).Dot(sizing.Way);
        }
        if (snap) to = Math.Round(to / SizeSnap) * SizeSnap;
        to = Math.Clamp(to, sizing.Least, sizing.Most);
        _poses.Stretch(sizing.Node, Held == Handle.SizeX ? to : null, Held == Handle.SizeY ? to : null, Held == Handle.SizeZ ? to : null, 1e9);
        _c.Say(FormattableString.Invariant($"Size {Letter(Held)} {to:0.##} u — Shift snaps to {SizeSnap}"));
    }

    static float ShaftHalfPx => 2.5f * Ui.Scale;
    static float HeadPx => 13 * Ui.Scale;
    static float HeadHalfPx => 7.5f * Ui.Scale;
    static float CasingPx => 1.5f * Ui.Scale;
    static float HubPx => 4 * Ui.Scale;
    static float ShortestArrowPx => 40 * Ui.Scale;
    static float EndOnPx => 3 * Ui.Scale;
    const int CasingOver = 0, InkOver = 1, HubSides = 16;

    public (Vector2 From, Vector2 Tip)? OnScreen(Handle arrow)
    {
        var (from, _, tip) = ArrowWorld(arrow);
        if (Rig.Behind(from) || Rig.Behind(tip)) return null;
        Vector2 a = Rig.Screen(from), run = Rig.Screen(tip) - a;
        float length = run.Length();
        if (length < EndOnPx) return null;
        return (a, a + run / length * Mathf.Max(length, ShortestArrowPx));
    }

    void DrawArrows(ImmediateMesh mesh, IEnumerable<Handle> arrows)
    {
        var shown = new List<(Handle Arrow, Vector2 From, Vector2 Tip, Vector2 Along)>();
        foreach (var arrow in arrows)
            if (OnScreen(arrow) is var (from, tip)) shown.Add((arrow, from, tip, (tip - from).Normalized()));
            else _c.Labels.Remove(LetterKey + Letter(arrow));
        if (shown.Count == 0) return;
        float depth = -(Rig.TableEye.AffineInverse() * ArrowWorld(shown[0].Arrow).From).Z;
        Vector3 World(Vector2 px) => WorldShift.ToTable(Rig.Cam.ProjectPosition(px, depth));
        var foot = shown[0].From;
        foreach (var (arrow, from, tip, along) in shown)
            Shape(mesh, SelectionAura.Casing, CasingOver, World, from, tip + along * CasingPx * (IsSize(arrow) ? 1 : 2), along, ShaftHalfPx + CasingPx,
                HeadHalfPx + CasingPx * (IsSize(arrow) ? 1 : 2), HeadPx + CasingPx * 3, IsSize(arrow));
        Hub(mesh, SelectionAura.Casing, CasingOver, World, foot, HubPx + CasingPx);
        foreach (var (arrow, from, tip, along) in shown)
        {
            Shape(mesh, RingInk, InkOver, World, from, tip, along, ShaftHalfPx, HeadHalfPx, HeadPx, IsSize(arrow));
            var side = new Vector2(along.Y, -along.X) * (IsUp(arrow) ? -LetterOff : LetterOff);
            _c.Labels.Set(LetterKey + Letter(arrow), World(tip), Letter(arrow), RingInk, LetterSize, side - along * LetterOff / 2);
        }
        Hub(mesh, RingInk, InkOver, World, foot, HubPx);
    }

    static StandardMaterial3D Flat(Color ink, int over) =>
        new()
        {
            AlbedoColor = ink, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, NoDepthTest = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, RenderPriority = over,
        };

    static void Hub(ImmediateMesh mesh, Color ink, int over, Func<Vector2, Vector3> world, Vector2 at, float radius)
    {
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, Flat(ink, over));
        for (int i = 0; i < HubSides; i++)
        {
            mesh.SurfaceAddVertex(world(at));
            mesh.SurfaceAddVertex(world(at + Vector2.FromAngle(Mathf.Tau * i / HubSides) * radius));
            mesh.SurfaceAddVertex(world(at + Vector2.FromAngle(Mathf.Tau * (i + 1) / HubSides) * radius));
        }
        mesh.SurfaceEnd();
    }

    void Letters(IEnumerable<Handle> shown)
    {
        foreach (var letter in EveryLetter.Except(shown.Select(Letter))) _c.Labels.Remove(LetterKey + letter);
    }

    static void Shape(ImmediateMesh mesh, Color ink, int over, Func<Vector2, Vector3> world, Vector2 from, Vector2 tip, Vector2 along, float shaft, float head, float headLength,
        bool boxed = false)
    {
        var neck = tip - along * Mathf.Min(boxed ? head * 2 : headLength, (tip - from).Length());
        Vector2 beside = new(-along.Y, along.X), s = beside * shaft, h = beside * head;
        var end = boxed ? new[] { neck - h, neck + h, tip + h, neck - h, tip + h, tip - h } : new[] { neck - h, neck + h, tip };
        mesh.SurfaceBegin(Mesh.PrimitiveType.Triangles, Flat(ink, over));
        foreach (var v in new[] { from - s, from + s, neck + s, from - s, neck + s, neck - s }.Concat(end)) mesh.SurfaceAddVertex(world(v));
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
        Vector3 from = Rig.RayFrom(screen), ray = Rig.Cam.ProjectRayNormal(screen), to = _liftStand - from;
        float along = Vector3.Up.Dot(ray), apart = 1 - along * along;
        if (apart < 1e-4f) return null;
        float above = Vector3.Up.Dot(to), ahead = ray.Dot(to);
        return ahead - along * above <= 0 ? float.NegativeInfinity : (along * ahead - above) / apart;
    }

    CameraRig Rig => _c.Rig;

    public void Draw()
    {
        if (_c.Tool != Tool.Transform || _selectTool.BoxSelecting)
        {
            Letters(Array.Empty<Handle>());
            return;
        }
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        var name = NameAlone(nodes, sheet);
        if (nodes.Count == 0 && sheet == null && name == null)
        {
            Letters(Array.Empty<Handle>());
            return;
        }
        Measure(nodes, sheet);
        bool flat = sheet != null || name != null;
        Letters(Arrows(flat));
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
        DrawArrows(mesh, Arrows(flat));
    }

    float Lift => _overlay.Lift;

    const int RingSegments = 72;

    Vector3 Seated(Vector3 w) => w with { Y = _c.SheetsTop + Lift + (float)_c.S.Ground.At(Coords.FromWorld(w)) };

    const float LeastUpOnScreen = 0.25f;
    static float KnobPixels => 8 * Ui.Scale;

    public float KnobSize() => Mathf.Max(0.05f, KnobPixels * UnitsPerPixel());

    float UnitsPerPixel() => 1 / Mathf.Max(0.01f, Rig.PixelsPerUnitAt(Seated(_center)));

    float Ring(float r) => Mathf.Max(r, RingLeast * UnitsPerPixel());

    void Grow(double k) => _radius = Ring(_gripRadius * (float)k);

    public void Measure(List<PieceNode> nodes, SheetNode? sheet)
    {
        if (sheet != null) MeasureSheet(sheet);
        else if (nodes.Count > 0) MeasurePieces(nodes);
        else MeasureName();
    }

    void MeasureName()
    {
        if (Held != Handle.None || _names.Box is not { } box) return;
        _center = Coords.ToWorld(box.Centre);
        float r = (float)new Vec2(box.HalfWide, box.HalfTall).Length;
        _radius = Ring(r * 1.15f);
    }

    void MeasurePieces(List<PieceNode> nodes)
    {
        if (Held != Handle.None) return;
        static Vector3 Flat(PieceNode n) => n.Position with { Y = 0 };
        var c = nodes.Aggregate(Vector3.Zero, (a, n) => a + Flat(n)) / nodes.Count;
        float r = nodes.Max(n => (Flat(n) - c).Length() + (n.FootprintCentre.Length() + n.Footprint.Length() / 2) * n.Scale.X);
        _center = c with { Y = 0 };
        _radius = Ring(r * 1.15f);
    }

    void MeasureSheet(SheetNode s)
    {
        if (Held != Handle.None) return;
        _center = s.Position with { Y = 0 };
        float r = new Vector2((float)s.Def.Size.W, (float)s.Def.Size.H).Length() / 2 * s.Scale.X;
        _radius = Ring(r * 1.06f);
    }

    public Vector3 RotateKnobWorld() => Seated(_center + new Vector3(0, 0, -_radius).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));
    public Vector3 ScaleKnobWorld() => Seated(_center + new Vector3(_radius * 0.7071f, 0, _radius * 0.7071f).Rotated(Vector3.Up, Rig.Yaw + Mathf.Pi));

    public Handle At(Vector2 screen)
    {
        var nodes = _selection.Nodes.ToList();
        var sheet = nodes.Count == 0 ? Sheet : null;
        var name = NameAlone(nodes, sheet);
        if (nodes.Count == 0 && sheet == null && name == null) return Handle.None;
        Measure(nodes, sheet);
        var nearest = (Handle: Handle.None, Off: 1f);
        void Offer(Handle handle, float off, float reach) { if (off / reach < nearest.Off) nearest = (handle, off / reach); }
        Offer(Handle.Rotate, (Rig.Screen(RotateKnobWorld()) - screen).Length(), KnobReach);
        Offer(Handle.Scale, (Rig.Screen(ScaleKnobWorld()) - screen).Length(), KnobReach);
        foreach (var arrow in Arrows(sheet != null || name != null))
            if (OnScreen(arrow) is var (from, tip))
                Offer(arrow, Off(screen, IsUp(arrow) ?Rig.Screen(Grip(arrow).From) : from.Lerp(tip, GrabFrom), tip), ArrowReach);
        return nearest.Handle;
    }

    static float KnobReach => 16 * Ui.Scale;

    public void Begin(Handle h, Vector2 screen)
    {
        Held = h;
        _gripRadius = _radius;
        _sheet = _selection.Nodes.Any() ? null : Sheet;
        _name = null;
        _nameNow = null;
        if (NameAlone(_selection.Nodes.ToList(), _sheet) is { } name && _names.Box is { } box) _name = (name, box.Centre, _names.LetterOf(name));
        if (_sheet != null)
        {
            _sheetOrigin = (_sheet.Def.RotDeg, _sheet.Def.Size);
            _sheetPreviewRot = _sheetOrigin.rot;
            _sheetPreviewK = 1;
        }
        _sheetStep = default;
        _origin.Clear();
        foreach (var n in _selection.Touchable) _origin[n.Piece.Id] = _poses.Current(n);
        _lifted.Clear();
        if (h == Handle.Up)
            foreach (var n in _selection.Touchable.Where(PieceLift.Can)) _lifted[n.Piece.Id] = (n.Position.Y - (float)n.Piece.Aloft, n.Piece.Aloft, n.Piece.Aloft);
        _liftStand = ArrowWorld(Handle.Up).From;
        _grabUp = UpAt(screen) is { } grabbed && float.IsFinite(grabbed) ? grabbed : 0;
        var g = Rig.GroundAt(screen) ?? _center;
        _grabGround = Coords.FromWorld(g);
        var d = g - _center;
        _startAngle = Mathf.Atan2(d.Z, d.X);
        _startDist = Mathf.Max(0.001f, new Vector2(d.X, d.Z).Length());
        _sizing = null;
        if (h is Handle.SizeX or Handle.SizeY or Handle.SizeZ && Sized is { } sized) BeginSize(sized, h, screen);
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
        if (_lifted.Count > 0) _c.Say($"{PieceLift.Said(said)} — Shift snaps to {PieceLift.SnapStep}");
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
        else if (_name is { } name) ShowName(NameEdits.Moved(name.Start, step));
        else if (_origin.Count == 0) return;
        else
            foreach (var n in _selection.Nodes)
                if (_origin.TryGetValue(n.Piece.Id, out var o)) _poses.Set(n, o.pos + step, o.rot, o.scale, 1e9);
        _c.Say($"Along {(Held == Handle.AlongX ? "X" : "Y")} {by:+0.##;-0.##;0} u — Shift snaps to whole units");
    }

    const double TurnSnap = 15, FineTurnSnap = 1;

    static double Stepped(double delta, bool snap, bool fine) => snap ? Math.Round(delta / TurnSnap) * TurnSnap : fine ? Math.Round(delta / FineTurnSnap) * FineTurnSnap : delta;

    public void Drag(Vector2 screen, bool snap, bool fine = false)
    {
        if (Held == Handle.Up) { Raise(screen, snap); return; }
        if (Held is Handle.SizeX or Handle.SizeY or Handle.SizeZ)
        {
            if (_sizing is { } sizing) Resize(sizing, screen, snap);
            return;
        }
        var g = Rig.GroundAt(screen);
        if (g == null) return;
        if (Held is Handle.AlongX or Handle.AlongY) { Along(Coords.FromWorld(g.Value), snap); return; }
        var d = g.Value - _center;
        if (_sheet != null) { DragSheet(_sheet, d, snap, fine); return; }
        if (_name != null) { DragName(d, snap, fine); return; }
        if (_origin.Count == 0) return;
        var center = Coords.FromWorld(_center);
        if (Held == Handle.Rotate)
        {
            double delta = Stepped(Mathf.RadToDeg(Mathf.Wrap(Mathf.Atan2(d.Z, d.X) - _startAngle, -Mathf.Pi, Mathf.Pi)), snap, fine);
            var rad = delta * Math.PI / 180;
            foreach (var n in _selection.Nodes)
            {
                if (!_origin.TryGetValue(n.Piece.Id, out var o)) continue;
                var off = o.pos - center;
                var rot = new Vec2(off.X * Math.Cos(rad) + off.Z * Math.Sin(rad), -off.X * Math.Sin(rad) + off.Z * Math.Cos(rad));
                _poses.Set(n, center + rot, o.rot + delta, o.scale, 1e9);
            }
            _c.Say($"Rotate {delta:+0.#;-0.#;0}° — Shift snaps to 15°, Ctrl to 1°");
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
            Grow(k);
            _c.Say($"Scale ×{k:0.00} — Shift snaps to 0.1");
        }
    }

    void DragSheet(SheetNode sheet, Vector3 d, bool snap, bool fine)
    {
        if (Held == Handle.Rotate)
        {
            double delta = Stepped(Mathf.RadToDeg(Mathf.Wrap(Mathf.Atan2(d.Z, d.X) - _startAngle, -Mathf.Pi, Mathf.Pi)), snap, fine);
            _sheetPreviewRot = _sheetOrigin.rot + delta;
            sheet.Rotation = new Vector3(0, Coords.Yaw(_sheetPreviewRot), 0);
            _c.Say($"Turn the sheet {delta:+0.#;-0.#;0}° — Shift snaps to 15°, Ctrl to 1°");
        }
        else
        {
            double k = Math.Clamp(new Vector2(d.X, d.Z).Length() / _startDist, 0.05, 20);
            if (snap) k = Math.Max(0.1, Math.Round(k * 10) / 10);
            _sheetPreviewK = k;
            sheet.Scale = new Vector3((float)k, 1, (float)k);
            Grow(k);
            _c.Say($"Resize the sheet ×{k:0.00}: {_sheetOrigin.size.W * k:0.#} × {_sheetOrigin.size.H * k:0.#} u — Shift snaps to 0.1");
        }
    }

    void ShowName(Stroke now)
    {
        _nameNow = now;
        _names.Show(now);
    }

    void DragName(Vector3 d, bool snap, bool fine)
    {
        if (_name is not { } name) return;
        if (Held == Handle.Rotate)
        {
            double delta = Stepped(Mathf.RadToDeg(Mathf.Wrap(Mathf.Atan2(d.Z, d.X) - _startAngle, -Mathf.Pi, Mathf.Pi)), snap, fine);
            ShowName(NameEdits.Turned(name.Start, name.Centre, delta));
            _c.Say($"Turn the name {delta:+0.#;-0.#;0}° — Shift snaps to 15°, Ctrl to 1°");
            return;
        }
        double k = Math.Clamp(new Vector2(d.X, d.Z).Length() / _startDist, 0.05, 20);
        if (snap) k = Math.Max(0.1, Math.Round(k * 10) / 10);
        ShowName(NameEdits.Sized(name.Start, name.Centre, k, name.Letter));
        Grow(k);
        _c.Say($"Letters ×{k:0.00}: {Fmt.Distance(name.Letter * k, _c.S.View)} tall — Shift snaps to 0.1");
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
        _sizing = null;
        if (lifting)
        {
            Land();
            _c.NotifySelectionChanged();
            return;
        }
        if (_name != null)
        {
            _name = null;
            var now = _nameNow;
            _nameNow = null;
            _names.Settle(now);
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
