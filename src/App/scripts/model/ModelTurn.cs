using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

internal sealed class TurnTool
{
    public const string Name = "turn";
    public const int Upright = 1;

    readonly ModelStudio _m;
    readonly record struct Start(ModelPart Part, float[] Pos, Basis Turn, Vector3 About);
    readonly record struct Trace(Vector2[] At, bool[] Behind, bool[] Gone);

    readonly List<Start> _held = new();
    int _ring = -1, _hover = -1;
    bool _kept;
    Vector2 _pressAt, _along;
    float _lever;

    const int Segments = 72;
    const float Reach = 0.14f, Width = 2.5f, LitWidth = 4, Pick = 9, BehindPenalty = 4, BehindAlpha = 0.35f, Lit = 0.35f;
    const float Step = 15, Fine = 1, LeastLever = 30, LeverStep = 0.25f, Hub = 4, Tidy = 1000;
    const int LetterSize = 13;
    static readonly Vector2 LabelBy = new(14, -14), LabelPad = new(5, 2);

    public TurnTool(ModelStudio m) => _m = m;

    public bool Held => _held.Count > 0;
    public float Turned { get; private set; }
    public int LitRing => Held ? _ring : _hover;

    Basis Axes => _m.Stand.Basis.Orthonormalized();

    Vector3 CentreOf(ModelPart p) =>
        p.IsGroup && _m.BoundsOf(p) is { } b ? b.GetCenter() : ModelTree.ParentXf(_m.Doc, p) * new Vector3(p.Pos[0], p.Pos[1], p.Pos[2]);

    (Vector3 centre, Basis axes, float reach)? Rings()
    {
        if (_m.Tool != Name || _m.SelPart is not { } p) return null;
        var centre = _m.Stand.Transform * CentreOf(p);
        if (_m.Cam.IsPositionBehind(centre)) return null;
        return (centre, Axes, _m.Camera.Dist * Reach);
    }

    static Vector3 On(Vector3 centre, Basis axes, float reach, int ring, float angle) =>
        centre + (axes[(ring + 1) % 3] * Mathf.Cos(angle) + axes[(ring + 2) % 3] * Mathf.Sin(angle)) * reach;

    static float AngleAt(int step) => Mathf.Tau * step / Segments;

    Trace Traced(Vector3 centre, Basis axes, float reach, int ring)
    {
        var cam = _m.Cam;
        var t = new Trace(new Vector2[Segments + 1], new bool[Segments + 1], new bool[Segments + 1]);
        for (int k = 0; k <= Segments; k++)
        {
            var world = On(centre, axes, reach, ring, AngleAt(k));
            t.Gone[k] = cam.IsPositionBehind(world);
            t.Behind[k] = (world - centre).Dot(cam.GlobalBasis.Z) < 0;
            t.At[k] = t.Gone[k] ? Vector2.Zero : cam.UnprojectPosition(world);
        }
        return t;
    }

    public Vector2? RingPoint(int ring, float angle) =>
        Rings() is var (centre, axes, reach) ? _m.Cam.UnprojectPosition(On(centre, axes, reach, ring, angle)) : null;

    (int ring, float angle)? RingAt(Vector2 at)
    {
        if (Rings() is not var (centre, axes, reach)) return null;
        (int, float)? best = null;
        float least = Pick;
        for (int ring = 0; ring < 3; ring++)
        {
            var t = Traced(centre, axes, reach, ring);
            for (int k = 0; k < Segments; k++)
            {
                if (t.Gone[k] || t.Gone[k + 1]) continue;
                float d = at.DistanceTo(Geometry2D.GetClosestPointToSegment(at, t.At[k], t.At[k + 1])) + (t.Behind[k] ? BehindPenalty : 0);
                if (d >= least) continue;
                least = d;
                best = (ring, AngleAt(k) + Mathf.Tau / Segments / 2);
            }
        }
        return best;
    }

    public void HoverAt(Vector2 at) => _hover = RingAt(at)?.ring ?? -1;

    public void Press(Vector2 at, string? hit)
    {
        if (RingAt(at) is var (ring, angle) && Rings() is var (centre, axes, reach))
        {
            var grip = On(centre, axes, reach, ring, angle);
            var tangent = axes[ring].Cross(grip - centre).Normalized();
            var along = _m.Cam.UnprojectPosition(grip + tangent * reach * LeverStep) - _m.Cam.UnprojectPosition(grip);
            Hold(new[] { _m.SelPart! }, ring, at, along.Length() < 1e-3f ? Vector2.Right : along.Normalized(), along.Length() / LeverStep);
            return;
        }
        if (hit == null || _m.Tools.PartHit(hit) is not { } under) return;
        if (under.Id != _m.SelPartId)
        {
            _m.SelPartId = under.Id;
            _m.RefreshSelection();
            _m.Studio.RebuildModelPanels();
        }
        if (Rings() is not var (mid, stand, wide)) return;
        float side = stand[Upright].Dot(Vector3.Up) < 0 ? -1 : 1;
        float lever = _m.Cam.UnprojectPosition(mid + _m.Cam.GlobalBasis.X * wide).DistanceTo(_m.Cam.UnprojectPosition(mid));
        var whole = ModelTree.IsSolidKey(hit) && _m.Solids.Chains.FirstOrDefault(c => c.Key == hit) is { } chain ? chain.Members : new List<ModelPart> { under };
        Hold(whole, Upright, at, Vector2.Right * side, lever);
    }

    Start StartOf(ModelPart p, Vector3 about)
    {
        var turn = Basis.FromEuler(new Vector3(Mathf.DegToRad(p.Rot[0]), Mathf.DegToRad(p.Rot[1]), Mathf.DegToRad(p.Rot[2])));
        return new Start(p, p.Pos.ToArray(), turn, ModelTree.ParentXf(_m.Doc, p).AffineInverse() * about);
    }

    Vector3 MiddleOf(IReadOnlyList<ModelPart> parts)
    {
        if (parts.Count == 1) return CentreOf(parts[0]);
        Aabb? all = null;
        foreach (var p in parts)
            if (_m.BoundsOf(p) is { } b) all = all is { } a ? a.Merge(b) : b;
        return all?.GetCenter() ?? CentreOf(parts[0]);
    }

    void Hold(IReadOnlyList<ModelPart> parts, int ring, Vector2 at, Vector2 along, float lever)
    {
        var about = MiddleOf(parts);
        _held.Clear();
        _held.AddRange(parts.Select(p => StartOf(p, about)));
        _ring = ring;
        _pressAt = at;
        _along = along;
        _lever = Mathf.Max(LeastLever, lever);
        _kept = false;
        Turned = 0;
    }

    public void Drag(Vector2 at, bool steps)
    {
        if (!Held) return;
        float deg = Mathf.Snapped(Mathf.RadToDeg((at - _pressAt).Dot(_along) / _lever), steps ? Step : Fine);
        if (Mathf.IsEqualApprox(deg, Turned)) return;
        if (!_kept) _m.Snapshot();
        _kept = true;
        Turned = deg;
        foreach (var from in _held) Apply(from, _ring, deg);
        _m.RefreshSelection();
    }

    public void Release()
    {
        if (!Held) return;
        bool turned = _kept;
        _held.Clear();
        _ring = -1;
        _kept = false;
        Turned = 0;
        if (turned)
        {
            _m.Changed(mesh: true);
            _m.Studio.RebuildModelPanels();
        }
        _m.RedrawOverlay();
    }

    public bool Key(InputEventKey k)
    {
        if (_m.Tool != Name || k.CtrlPressed || k.MetaPressed || k.AltPressed || k.Keycode is not (Godot.Key.Q or Godot.Key.E)) return false;
        if (Held || _m.SelPart is not { } p) return true;
        int ring = _hover >= 0 ? _hover : Upright;
        var towards = _m.Cam.GlobalPosition - _m.Stand.Transform * CentreOf(p);
        float seen = Axes[ring].Dot(towards) < 0 ? -1 : 1;
        float step = (k.ShiftPressed ? Fine : Step) * (k.Keycode == Godot.Key.Q ? 1 : -1) * seen;
        if (!k.Echo) _m.Snapshot();
        By(p, ring, step);
        _m.Studio.RebuildModelPanels();
        _m.RedrawOverlay();
        return true;
    }

    public void By(ModelPart p, int ring, float deg)
    {
        Apply(StartOf(p, CentreOf(p)), ring, deg);
        _m.RefreshSelection();
        _m.Changed(mesh: true);
    }

    void Apply(Start from, int ring, float deg)
    {
        var axis = Vector3.Zero;
        axis[ring] = 1;
        var into = ModelTree.ParentXf(_m.Doc, from.Part).Basis;
        var by = (into.Inverse() * new Basis(axis, Mathf.DegToRad(deg)) * into).Orthonormalized();
        var at = from.About + by * (new Vector3(from.Pos[0], from.Pos[1], from.Pos[2]) - from.About);
        var e = (by * from.Turn).Orthonormalized().GetEuler();
        from.Part.Pos = new[] { at.X, at.Y, at.Z };
        from.Part.Rot = new[] { Degrees(e.X), Degrees(e.Y), Degrees(e.Z) };
    }

    static float Degrees(float radians) => Mathf.Round(Mathf.RadToDeg(radians) * Tidy) / Tidy + 0f;

    public void Draw(Control o)
    {
        if (Rings() is not var (centre, axes, reach)) return;
        var mid = _m.Cam.UnprojectPosition(centre);
        int lit = LitRing;
        for (int ring = 0; ring < 3; ring++)
        {
            var colour = ring == lit ? ModelTools.AxisColours[ring].Lightened(Lit) : ModelTools.AxisColours[ring];
            var t = Traced(centre, axes, reach, ring);
            for (int k = 0; k < Segments; k++)
                if (!t.Gone[k] && !t.Gone[k + 1]) o.DrawLine(t.At[k], t.At[k + 1], t.Behind[k] ? colour with { A = BehindAlpha } : colour, ring == lit ? LitWidth : Width, true);
        }
        o.DrawCircle(mid, Hub, Colors.White);
        if (!Held || Turned == 0) return;
        var text = $"{(Turned > 0 ? "+" : "−")}{Mathf.Abs(Turned):0}°";
        var size = Ui.Sans().GetStringSize(text, HorizontalAlignment.Left, -1, LetterSize);
        var corner = mid + LabelBy;
        o.DrawRect(new Rect2(corner - LabelPad, size + LabelPad * 2), new Color(Ui.Bar, 0.86f));
        o.DrawString(Ui.Sans(), corner + new Vector2(0, Ui.Sans().GetAscent(LetterSize)), text, HorizontalAlignment.Left, -1, LetterSize, Colors.White);
    }
}
