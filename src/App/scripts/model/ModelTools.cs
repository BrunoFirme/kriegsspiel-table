using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client.Studio;

namespace KTable.App;

internal sealed class ModelCamera
{
    readonly Camera3D _cam;
    public float Yaw = Mathf.Pi - 0.6f, Pitch = -0.45f, Dist = 6;
    public Vector3 Target = new(0, 0.5f, 0);
    public bool Spin, Ortho;

    const float LeastFar = 500, FarPerDistance = 50;

    public ModelCamera(Camera3D cam) => _cam = cam;

    public void Apply()
    {
        Pitch = Mathf.Clamp(Pitch, -1.5f, 1.2f);
        var dir = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), -Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch));
        _cam.Position = Target + dir * Dist;
        _cam.LookAt(Target, Vector3.Up);
        _cam.Near = Mathf.Max(0.001f, Dist * 0.01f);
        _cam.Far = Mathf.Max(LeastFar, Dist * FarPerDistance);
        _cam.Projection = Ortho ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
        if (Ortho) _cam.Size = 2 * Dist * Mathf.Tan(Mathf.DegToRad(_cam.Fov) / 2);
    }

    public const float Level = 0, Above = -1.5f;

    public void Face(float yaw, float pitch)
    {
        Spin = false;
        Yaw = yaw;
        Pitch = pitch;
        Apply();
    }

    public void Frame(List<Aabb> aabbs)
    {
        if (aabbs.Count == 0)
        {
            Target = new Vector3(0, 0.5f, 0);
            Dist = 6;
            Apply();
            return;
        }
        var b = aabbs[0];
        foreach (var a in aabbs) b = b.Merge(a);
        Target = b.GetCenter();
        Dist = Mathf.Max(0.5f, b.Size.Length() * 1.6f);
        Apply();
    }
}

internal sealed class ModelTools
{
    readonly ModelStudio _m;
    bool _orbiting, _panning, _painting, _moving;
    Vector2 _last, _clickAt;
    public Vector2 Hover;
    Vector3 _moveFrom;
    float[]? _movePos;
    List<(ModelPart part, float[] from)> _dragged = new();
    float _strokeLeft;
    int _axis = -1, _hoverAxis = -1;
    float _axisFrom, _axisScale;
    Vector3 _axisCentre, _axisDir;
    const float AxisPick = 9, AxisLength = 0.2f, AxisWidth = 3, ArrowHead = 10, AxisHover = 0.35f;
    static readonly Color[] AxisColours = { new(0.92f, 0.32f, 0.3f), new(0.38f, 0.8f, 0.36f), new(0.36f, 0.56f, 0.96f) };

    public ModelTools(ModelStudio m) => _m = m;

    ModelCamera Camera => _m.Camera;

    public void OnInput(InputEvent e)
    {
        var vc = _m.Vc;
        switch (e)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight, Pressed: true } w:
                if (_m.Decal.Wheel(w)) { vc.AcceptEvent(); break; }
                if (w.ButtonIndex is MouseButton.WheelLeft or MouseButton.WheelRight) { vc.AcceptEvent(); break; }
                if (w.CtrlPressed && _m.Tool is "paint" or "erase") { _m.P.Brush = Mathf.Clamp(_m.P.Brush * (w.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), 1, 400); _m.Studio.RebuildModelOptions(); }
                else { Camera.Dist *= w.ButtonIndex == MouseButton.WheelUp ? 0.88f : 1 / 0.88f; Camera.Apply(); }
                vc.AcceptEvent();
                break;
            case InputEventMouseButton mb:
                vc.GrabFocus();
                if (mb.ButtonIndex == MouseButton.Right || (mb.ButtonIndex == MouseButton.Left && _m.Tool == "orbit"))
                {
                    _orbiting = mb.Pressed;
                    _last = mb.Position;
                    if (mb.Pressed && mb.ButtonIndex == MouseButton.Left) _clickAt = mb.Position;
                    else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left && (mb.Position - _clickAt).Length() < 4) PickPart(mb.Position);
                }
                else if (mb.ButtonIndex == MouseButton.Middle) { _panning = mb.Pressed; _last = mb.Position; }
                else if (mb.ButtonIndex == MouseButton.Left) LeftButton(mb);
                vc.AcceptEvent();
                break;
            case InputEventMouseMotion mm:
                Hover = mm.Position;
                DropLostButtons(mm);
                if (_orbiting)
                {
                    Camera.Yaw -= mm.Relative.X * 0.008f;
                    Camera.Pitch -= mm.Relative.Y * 0.008f;
                    Camera.Apply();
                }
                else if (_panning)
                {
                    float k = Camera.Dist * 0.0016f;
                    Camera.Target += (-_m.Cam.GlobalBasis.X * mm.Relative.X + _m.Cam.GlobalBasis.Y * mm.Relative.Y) * k;
                    Camera.Apply();
                }
                else if (_painting) PaintAlong(_last, mm.Position);
                else if (_moving && _axis >= 0) AxisMove(mm.Position);
                else if (_moving) MoveTo(mm.Position, mm.ShiftPressed);
                else if (_m.Tool == "move") _hoverAxis = AxisAt(mm.Position);
                _last = mm.Position;
                vc.AcceptEvent();
                _m.RedrawOverlay();
                break;
        }
    }

    void DropLostButtons(InputEventMouseMotion mm)
    {
        if (_orbiting && (mm.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Right)) == 0) _orbiting = false;
        if (_panning && (mm.ButtonMask & MouseButtonMask.Middle) == 0) _panning = false;
        if ((_painting || _moving) && (mm.ButtonMask & MouseButtonMask.Left) == 0) LeftButton(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = mm.Position });
    }

    void LeftButton(InputEventMouseButton mb)
    {
        if (!mb.Pressed)
        {
            if (_painting) { _painting = false; _m.EndStroke(); }
            if (_moving)
            {
                _moving = false;
                _dragged.Clear();
                _axis = -1;
                _m.Changed(mesh: true);
                _m.Studio.RebuildModelPanels();
                _m.RedrawOverlay();
            }
            return;
        }
        var hit = Ray(mb.Position);
        switch (_m.Tool)
        {
            case "paint":
            case "erase":
                if (mb.AltPressed && hit != null) { PickAt(hit.Value); return; }
                _m.BeginStroke();
                _sight = null;
                _painting = true;
                _strokeLeft = 0;
                if (hit != null) Stamp(hit.Value);
                break;
            case "fill":
                if (hit == null || _m.Views[hit.Value.part].Fixed) return;
                _m.BeginStroke();
                if (mb.ShiftPressed) FillPart(_m.Views[hit.Value.part]);
                else FillRegion(_m.Views[hit.Value.part], hit.Value.tri, hit.Value.u, hit.Value.v);
                _m.EndStroke();
                break;
            case "pick":
                if (hit != null) PickAt(hit.Value);
                break;
            case "decal":
                _m.Decal.Press(mb.Position);
                break;
            case "move":
                if (AxisAt(mb.Position) is var axis and >= 0 && Gizmo() is { } g && AxisParam(mb.Position, g.centre, g.axes[axis].Normalized()) is { } from)
                {
                    _m.Snapshot();
                    _axis = axis;
                    _axisFrom = from;
                    _axisCentre = g.centre;
                    _axisDir = g.axes[axis].Normalized();
                    _axisScale = Mathf.Max(1e-6f, _m.Stand.Basis[axis].Length());
                    _movePos = _m.SelPart!.Pos.ToArray();
                    _dragged = new() { (_m.SelPart!, _m.SelPart!.Pos.ToArray()) };
                    _moving = true;
                    return;
                }
                if (hit == null || PartHit(hit.Value.part) is not { } picked) return;
                _m.SelPartId = picked.Id;
                _m.RefreshSelection();
                _m.Studio.RebuildModelPanels();
                _m.Snapshot();
                _moving = true;
                _moveFrom = hit.Value.world;
                _movePos = picked.Pos.ToArray();
                _dragged = (ModelTree.IsSolidKey(hit.Value.part) && _m.Solids.Chains.FirstOrDefault(c => c.Key == hit.Value.part) is { } chain ? chain.Members : new List<ModelPart> { picked })
                    .Select(x => (x, x.Pos.ToArray())).ToList();
                break;
        }
    }

    ModelPart? PartHit(string id) => ModelTree.IsSolidKey(id) ? _m.Doc.Parts.FirstOrDefault(p => p.Id == ModelTree.HeadOf(id)) : _m.Doc.Parts.FirstOrDefault(p => p.Id == id);

    void PickPart(Vector2 at)
    {
        if (Ray(at) is { } h && PartHit(h.part) is { } p)
        {
            _m.SelPartId = p.Id;
            _m.RefreshSelection();
            _m.Studio.RebuildModelPanels();
        }
    }

    (Vector3 centre, Vector3[] axes)? Gizmo()
    {
        if (_m.Tool != "move" || _m.SelPart is not { } p || _m.BoundsOf(p) is not { } box) return null;
        var centre = (_m.Stand.Transform * box).GetCenter();
        float length = Camera.Dist * AxisLength;
        var b = _m.Stand.Basis;
        return (centre, new[] { b.X.Normalized() * length, b.Y.Normalized() * length, b.Z.Normalized() * length });
    }

    int AxisAt(Vector2 at)
    {
        if (Gizmo() is not { } g || _m.Cam.IsPositionBehind(g.centre)) return -1;
        var c = _m.Cam.UnprojectPosition(g.centre);
        int best = -1;
        float bestD = AxisPick;
        for (int i = 0; i < g.axes.Length; i++)
        {
            var tip = _m.Cam.UnprojectPosition(g.centre + g.axes[i]);
            float d = at.DistanceTo(Geometry2D.GetClosestPointToSegment(at, c, tip));
            if (d < bestD) { best = i; bestD = d; }
        }
        return best;
    }

    float? AxisParam(Vector2 at, Vector3 centre, Vector3 dir)
    {
        var o = _m.Cam.ProjectRayOrigin(at);
        var d = _m.Cam.ProjectRayNormal(at);
        float b = dir.Dot(d), denom = 1 - b * b;
        if (denom < 1e-4f) return null;
        var w = centre - o;
        return (b * d.Dot(w) - dir.Dot(w)) / denom;
    }

    void AxisMove(Vector2 at)
    {
        if (_movePos == null || AxisParam(at, _axisCentre, _axisDir) is not { } t) return;
        var world = Vector3.Zero;
        world[_axis] = (t - _axisFrom) / _axisScale;
        Shift(world, _axis);
    }

    void Shift(Vector3 rootDelta, int axis = -1)
    {
        foreach (var (part, from) in _dragged)
        {
            var into = ModelTree.ParentXf(_m.Doc, part).Basis.Inverse() * rootDelta;
            var pos = new[] { from[0] + into.X, from[1] + into.Y, from[2] + into.Z };
            if (_m.SnapStep is > 0 and var step)
                for (int i = 0; i < 3; i++)
                    if (axis < 0 ? Mathf.Abs(into[i]) > 1e-6f : i == axis) pos[i] = Mathf.Snapped(pos[i], step);
            part.Pos = pos;
        }
        _m.RefreshSelection();
    }

    public void DrawGizmo(Control o)
    {
        if (Gizmo() is not { } g || _m.Cam.IsPositionBehind(g.centre)) return;
        var c = _m.Cam.UnprojectPosition(g.centre);
        int lit = _axis >= 0 ? _axis : _hoverAxis;
        for (int i = 0; i < g.axes.Length; i++)
        {
            var tip = _m.Cam.UnprojectPosition(g.centre + g.axes[i]);
            if ((tip - c).Length() < 2) continue;
            var col = i == lit ? AxisColours[i].Lightened(AxisHover) : AxisColours[i];
            var dir = (tip - c).Normalized();
            var side = new Vector2(-dir.Y, dir.X) * (ArrowHead / 2);
            o.DrawLine(c, tip, col, AxisWidth, true);
            o.DrawColoredPolygon(new[] { tip + dir * ArrowHead, tip + side, tip - side }, col);
            o.DrawString(ThemeDB.FallbackFont, tip + dir * (ArrowHead + 6) + new Vector2(-4, 5), "XYZ"[i].ToString(), HorizontalAlignment.Left, -1, 13, col);
        }
        o.DrawCircle(c, 4, Colors.White);
    }

    void MoveTo(Vector2 at, bool vertical)
    {
        if (_m.SelPart is not { } p || _movePos == null) return;
        var cam = _m.Cam;
        var o = cam.ProjectRayOrigin(at);
        var d = cam.ProjectRayNormal(at);
        Vector3 now;
        if (vertical)
        {
            var n = (cam.GlobalPosition - _moveFrom) with { Y = 0 };
            var plane = new Plane(n.Normalized(), _moveFrom);
            if (plane.IntersectsRay(o, d) is not { } hit) return;
            now = new Vector3(_moveFrom.X, hit.Y, _moveFrom.Z);
        }
        else
        {
            var plane = new Plane(Vector3.Up, _moveFrom.Y);
            if (plane.IntersectsRay(o, d) is not { } hit) return;
            now = hit;
        }
        Shift((now - _moveFrom) / _m.Doc.Scale);
    }

    public (string part, int tri, Vector3 local, Vector3 world, float u, float v)? Ray(Vector2 at)
    {
        var o = _m.Cam.ProjectRayOrigin(at);
        var dir = _m.Cam.ProjectRayNormal(at);
        (string, int, Vector3, Vector3, float, float)? best = null;
        float bestT = float.MaxValue;
        foreach (var v in _m.Drawn)
        foreach (var place in v.Places)
        {
            var xf = _m.Stand.Transform * place;
            var inv = xf.AffineInverse();
            var lo = inv * o;
            var ld = (inv.Basis * dir);
            var V = v.Data.V;
            for (int t = 0; t < v.Data.Tris; t++)
            {
                var a = V[t * 3];
                var e1 = V[t * 3 + 1] - a;
                var e2 = V[t * 3 + 2] - a;
                var pv = ld.Cross(e2);
                float det = e1.Dot(pv);
                if (Mathf.Abs(det) < 1e-12f) continue;
                float inv_ = 1 / det;
                var tv = lo - a;
                float u = tv.Dot(pv) * inv_;
                if (u < 0 || u > 1) continue;
                var qv = tv.Cross(e1);
                float w = ld.Dot(qv) * inv_;
                if (w < 0 || u + w > 1) continue;
                float tt = e2.Dot(qv) * inv_;
                if (tt <= 0) continue;
                var local = lo + ld * tt;
                var world = xf * local;
                float dist = (world - o).Length();
                if (dist < bestT) { bestT = dist; best = (v.Part.Id, t, local, world, u, w); }
            }
        }
        return best;
    }

    void PaintAlong(Vector2 from, Vector2 to)
    {
        float r = Mathf.Max(1, _m.P.Brush / 2);
        float step = Mathf.Max(1, r * 0.3f);
        float len = (to - from).Length();
        float at = step - _strokeLeft;
        while (at <= len)
        {
            if (Ray(from.Lerp(to, at / Mathf.Max(1e-3f, len))) is { } hit) Stamp(hit);
            at += step;
        }
        _strokeLeft = len - (at - step);
    }

    ModelSight? _sight;
    const float SightPad = 2, SightEdge = 4;

    public void Stamp((string part, int tri, Vector3 local, Vector3 world, float u, float v) hit)
    {
        var vp = _m.Vp.Size;
        var cam = _m.Cam;
        float distCam = (cam.GlobalPosition - hit.world).Length();
        float worldPerPx = 2 * distCam * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2) / Mathf.Max(1, vp.Y);
        float px = Mathf.Max(1, _m.P.Brush / 2);
        float R = px * worldPerPx;
        if (_sight == null || !_sight.Fits(cam, vp)) _sight = new ModelSight(vp, cam, _m.Stand.Transform, _m.Drawn);
        var sight = _sight;
        if (!sight.Eye.Project(hit.world, out var at, out _)) return;
        float pad = px * SightPad + SightEdge;
        int x0 = Math.Max(0, (int)(at.X - pad)), y0 = Math.Max(0, (int)(at.Y - pad));
        int x1 = Math.Min(vp.X - 1, (int)(at.X + pad)), y1 = Math.Min(vp.Y - 1, (int)(at.Y + pad));
        if (x1 < x0 || y1 < y0) return;
        sight.Look(new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
        bool erase = _m.Tool == "erase";
        var ink = _m.Ink;
        var under = _m.Views.TryGetValue(hit.part, out var hv) && !hv.Fixed ? hv : null;
        int at0 = under == null ? -1 : TexelIndex(under, hit.tri, hit.u, hit.v);
        uint was = at0 < 0 ? 0 : BitConverter.ToUInt32(under!.Paint, at0);
        foreach (var v in _m.Drawn)
        {
            if (v.Fixed) continue;
            bool any = false;
            var places = v.Places;
            for (int pi = 0; pi < places.Length; pi++)
            {
                int slot = sight.Slot(v, pi);
                if (slot < 0) continue;
                var xf = _m.Stand.Transform * places[pi];
                var inv = xf.AffineInverse();
                var centre = inv * hit.world;
                float scale = (xf.Basis.Scale.X + xf.Basis.Scale.Y + xf.Basis.Scale.Z) / 3;
                float r = R / Mathf.Max(1e-6f, scale);
                any |= Dab(v, centre, r, (t, p) => sight.Sees(slot, t, xf * p), erase, ink, _m.P.ModelSoft);
            }
            if (any) v.Repaint();
        }
        if (at0 < 0 || BitConverter.ToUInt32(under!.Paint, at0) != was) return;
        Touch(under, at0, 1, erase, ink);
        under.Repaint();
    }

    static (int x, int y) TexelAt(ModelPartView view, int tri, float u, float v)
    {
        var d = view.Data;
        var uv = d.UV[tri * 3] * (1 - u - v) + d.UV[tri * 3 + 1] * u + d.UV[tri * 3 + 2] * v;
        return (Math.Clamp((int)(uv.X * view.W), 0, view.W - 1), Math.Clamp((int)(uv.Y * view.H), 0, view.H - 1));
    }

    static int TexelIndex(ModelPartView view, int tri, float u, float v)
    {
        var (x, y) = TexelAt(view, tri, u, v);
        return (y * view.W + x) * 4;
    }

    static void Touch(ModelPartView v, int o, float f, bool erase, Color ink)
    {
        if (erase)
        {
            float k = 1 - f;
            v.Paint[o] = (byte)(v.Paint[o] * k);
            v.Paint[o + 1] = (byte)(v.Paint[o + 1] * k);
            v.Paint[o + 2] = (byte)(v.Paint[o + 2] * k);
            v.Paint[o + 3] = (byte)(v.Paint[o + 3] * k);
            return;
        }
        float alpha = f * ink.A, keep = 1 - alpha;
        v.Paint[o] = (byte)Math.Min(255, ink.R * 255 * alpha + v.Paint[o] * keep);
        v.Paint[o + 1] = (byte)Math.Min(255, ink.G * 255 * alpha + v.Paint[o + 1] * keep);
        v.Paint[o + 2] = (byte)Math.Min(255, ink.B * 255 * alpha + v.Paint[o + 2] * keep);
        v.Paint[o + 3] = (byte)Math.Min(255, 255 * alpha + v.Paint[o + 3] * keep);
    }

    static bool Dab(ModelPartView v, Vector3 centre, float r, Func<int, Vector3, bool> seen, bool erase, Color ink, float soft)
    {
        var V = v.Data.V;
        var UV = v.Data.UV;
        bool any = false;
        for (int t = 0; t < v.Data.Tris; t++)
        {
            if ((v.Centre[t] - centre).LengthSquared() > (r + v.Radius[t]) * (r + v.Radius[t])) continue;
            var a = V[t * 3];
            var b = V[t * 3 + 1];
            var c = V[t * 3 + 2];
            int tri = t;
            var ta = UV[t * 3] * new Vector2(v.W, v.H);
            var tb = UV[t * 3 + 1] * new Vector2(v.W, v.H);
            var tc = UV[t * 3 + 2] * new Vector2(v.W, v.H);
            ModelImport.Raster(ta, tb, tc, Math.Max(v.W, v.H), Reach(a, b, c, ta, tb, tc, centre, r), (x, y, w0, w1, w2) =>
            {
                if (x >= v.W || y >= v.H) return;
                var p = a * w0 + b * w1 + c * w2;
                float d = (p - centre).Length() / r;
                if (d >= 1 || !seen(tri, p)) return;
                float edge = Mathf.Clamp(soft, 0, 1);
                float f = d <= 1 - edge ? 1 : (1 - d) / edge;
                Touch(v, (y * v.W + x) * 4, f, erase, ink);
                any = true;
            });
        }
        return any;
    }

    static Rect2I? Reach(Vector3 a, Vector3 b, Vector3 c, Vector2 ta, Vector2 tb, Vector2 tc, Vector3 centre, float r)
    {
        var e1 = b - a;
        var e2 = c - a;
        var n = e1.Cross(e2);
        float nl = n.Length(), l1 = e1.Length();
        if (nl < 1e-12f || l1 < 1e-9f) return null;
        var x = e1 / l1;
        var y = (n / nl).Cross(x);
        float e2x = e2.Dot(x), e2y = e2.Dot(y);
        if (Mathf.Abs(e2y) < 1e-12f) return null;
        var f1 = tb - ta;
        var f2 = tc - ta;
        var cx = f1 / l1;
        var cy = f1 * (-e2x / (l1 * e2y)) + f2 / e2y;
        var d = centre - a;
        var uv = ta + cx * d.Dot(x) + cy * d.Dot(y);
        float hx = r * Mathf.Sqrt(cx.X * cx.X + cy.X * cy.X) + 2, hy = r * Mathf.Sqrt(cx.Y * cx.Y + cy.Y * cy.Y) + 2;
        if (!float.IsFinite(hx) || !float.IsFinite(hy) || !float.IsFinite(uv.X) || !float.IsFinite(uv.Y)) return null;
        return new Rect2I((int)Mathf.Floor(uv.X - hx), (int)Mathf.Floor(uv.Y - hy), (int)Mathf.Ceil(2 * hx) + 1, (int)Mathf.Ceil(2 * hy) + 1);
    }

    void FillPart(ModelPartView v)
    {
        var ink = _m.Ink;
        byte r = (byte)(ink.R * ink.A * 255), g = (byte)(ink.G * ink.A * 255), b = (byte)(ink.B * ink.A * 255), a = (byte)(ink.A * 255);
        for (int i = 0; i < v.Paint.Length; i += 4)
        {
            v.Paint[i] = r;
            v.Paint[i + 1] = g;
            v.Paint[i + 2] = b;
            v.Paint[i + 3] = a;
        }
        v.Repaint();
    }

    void FillRegion(ModelPartView v, int tri, float u, float w)
    {
        var (r, g, b, a) = UvPaint.Premultiplied(_m.Ink.R, _m.Ink.G, _m.Ink.B, _m.Ink.A);
        if (UvRegion.Fill(v.Surface(), v.Paint, v.Base.GetData(), new RegionSeed(tri, u, w), _m.P.ModelFillMatch, _m.P.ModelFillBend, r, g, b, a) > 0) v.Repaint();
    }

    void PickAt((string part, int tri, Vector3 local, Vector3 world, float u, float v) hit)
    {
        var view = _m.Views[hit.part];
        var (x, y) = TexelAt(view, hit.tri, hit.u, hit.v);
        var bc = view.Base.GetPixel(x, y);
        int o = (y * view.W + x) * 4;
        float a = view.Paint[o + 3] / 255f;
        var c = new Color(bc.R * (1 - a) + view.Paint[o] / 255f, bc.G * (1 - a) + view.Paint[o + 1] / 255f, bc.B * (1 - a) + view.Paint[o + 2] / 255f) * Hex.To(view.Part.Tint, Colors.White);
        _m.Studio.PickInk(c with { A = 1 });
    }
}
