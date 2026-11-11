using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class CameraRig : Node3D
{
    public Camera3D Cam { get; private set; } = null!;
    public Vector3 Target = Vector3.Zero;
    public float Yaw = Mathf.Pi;
    const float LookingDownRadians = -1.0f, SteepestPitchRadians = -1.55f, FlattestPitchRadians = -0.12f;
    public float Pitch = LookingDownRadians;
    public float Distance = 120;
    public bool TopDown { get; private set; }
    public float MinDistance = 1.5f, MaxDistance = 2500;

    public const float TableFov = 50, NormalLens = 60, WideLens = 90;
    public const float EyeTiltRadians = -0.12f;
    const float EyeLowestRadians = -1.3f, EyeHighestRadians = 1.3f;
    public Vector3? Eye { get; private set; }
    View? _before;

    public override void _Ready()
    {
        Cam = new Camera3D { Near = 0.05f, Far = 10000, Fov = TableFov, CullMask = 0xFFFFF & ~Table.PictureOnlyLayer };
        AddChild(Cam);
        Apply();
    }

    public bool Flat { get; private set; }

    public event Action? ViewChanged;
    public event Action? FlatChanged;

    void Project(Camera3D.ProjectionType kind)
    {
        Cam.Projection = kind;
        bool flat = kind == Camera3D.ProjectionType.Orthogonal;
        if (flat == Flat) return;
        Flat = flat;
        FlatChanged?.Invoke();
    }

    public void ToggleTopDown()
    {
        TopDown = !TopDown;
        ViewChanged?.Invoke();
        Apply();
    }

    public void LookFrom(Vector3 eye, float lens, float? yaw = null)
    {
        bool entering = Eye == null;
        if (entering)
        {
            _before = Now;
            _yawTo = _pitchTo = null;
            _spin = Vector2.Zero;
            if (yaw is { } y) Yaw = y;
            Pitch = EyeTiltRadians;
            Cam.KeepAspect = Camera3D.KeepAspectEnum.Width;
        }
        Eye = eye;
        if (Cam.Fov != lens) Cam.Fov = lens;
        if (entering && TopDown) ToggleTopDown();
        else Apply();
    }

    public void LeaveEye()
    {
        if (Eye == null) return;
        Eye = null;
        _pitchTo = null;
        Cam.Fov = TableFov;
        Cam.KeepAspect = Camera3D.KeepAspectEnum.Height;
        if (_before is { } v) Show(v);
        else Apply();
        _before = null;
    }

    float ClampPitch(float p) => Eye != null ? Mathf.Clamp(p, EyeLowestRadians, EyeHighestRadians) : Mathf.Clamp(p, SteepestPitchRadians, FlattestPitchRadians);

    public void Orbit(Vector2 rel)
    {
        Yaw -= rel.X * 0.005f;
        if (!TopDown || Eye != null) Pitch = ClampPitch(Pitch - rel.Y * 0.005f);
        Apply();
    }

    public void PanScreen(Vector2 from, Vector2 to)
    {
        if (Eye != null) return;
        var a = PlaneAt(from, Target.Y);
        var b = PlaneAt(to, Target.Y);
        if (a == null || b == null) return;
        Target += a.Value - b.Value;
        Settle();
        Apply();
    }

    public void PanWorld(Vector3 d)
    {
        if (Eye != null) return;
        _zoomAnchor = null;
        Target += d;
        Settle();
        Apply();
    }

    public Func<Ground> Terrain = () => Ground.Flat;
    const float AboveGround = 0.6f;
    const int MarchSteps = 160, Halvings = 14;

    const float GroundEase = 8f, LiftEase = 5f, Settled = 0.001f;
    float _groundGoal, _lift, _liftGoal;

    float GroundUnderTarget() => (float)Terrain().At(Coords.FromWorld(Target));
    void Settle() => _groundGoal = GroundUnderTarget();
    void SettleNow() { Settle(); Target.Y = _groundGoal; }

    bool Ease(float dt)
    {
        bool moving = false;
        if (Mathf.Abs(Target.Y - _groundGoal) > Settled) { Target.Y = Mathf.Lerp(Target.Y, _groundGoal, 1 - Mathf.Exp(-dt * GroundEase)); moving = true; }
        if (_lift > _liftGoal + Settled) { _lift = Mathf.Lerp(_lift, _liftGoal, 1 - Mathf.Exp(-dt * LiftEase)); moving = true; }
        return moving;
    }

    public const float GlideDamping = 10f, SpinDamping = 14f;
    Vector3 _glide;
    Vector2 _spin;

    public void Fling(Vector3 unitsPerSecond)
    {
        if (Eye != null) return;
        _glide = unitsPerSecond;
        _glide.Y = 0;
        float cap = Distance * 2;
        if (_glide.Length() > cap) _glide = _glide.Normalized() * cap;
    }
    public void FlingOrbit(Vector2 radiansPerSecond) => _spin = radiansPerSecond.LimitLength(Mathf.Pi);
    public void StopGliding()
    {
        _glide = Vector3.Zero;
        _spin = Vector2.Zero;
        _yawTo = null;
        _pitchTo = null;
    }
    public void Still() { StopGliding(); _zoomTo = null; }
    public bool Gliding => _glide != Vector3.Zero || _spin != Vector2.Zero || _yawTo != null || _pitchTo != null || _zoomTo != null;

    const float ZoomEase = 8.5f, ZoomSettled = 0.001f;
    float? _zoomTo;
    Vector3? _zoomAnchor;
    Vector2 _zoomCursor;

    public float ZoomGoal => _zoomTo ?? Distance;

    const float TurnEase = 12f, TurnSettled = 0.002f;
    float? _yawTo, _pitchTo;

    public float YawGoal => _yawTo ?? Yaw;

    public static float YawLooking(double mapBearingDeg) => Mathf.Pi - Mathf.DegToRad((float)mapBearingDeg);

    public void FaceNorth(double northDeg) => Turn(YawLooking(northDeg));

    public void Turn(float yaw)
    {
        _spin = Vector2.Zero;
        _yawTo = Yaw + Mathf.Wrap(yaw - Yaw, -Mathf.Pi, Mathf.Pi);
    }

    public void Face(float yaw, float pitch)
    {
        Turn(yaw);
        _pitchTo = ClampPitch(pitch);
    }

    public void Tick(double delta)
    {
        float dt = (float)Math.Min(delta, 0.1);
        bool eased = Ease(dt);
        if (!Gliding) { if (eased) Apply(); return; }
        if (_glide != Vector3.Zero)
        {
            Target += _glide * dt;
            Settle();
            _glide *= Mathf.Exp(-dt * GlideDamping);
            if (_glide.Length() < 0.004f * Distance) _glide = Vector3.Zero;
        }
        if (_yawTo is float to)
        {
            Yaw = Mathf.Lerp(Yaw, to, 1 - Mathf.Exp(-dt * TurnEase));
            if (Mathf.Abs(to - Yaw) < TurnSettled) { Yaw = to; _yawTo = null; }
        }
        if (_pitchTo is float tilt)
        {
            Pitch = Mathf.Lerp(Pitch, tilt, 1 - Mathf.Exp(-dt * TurnEase));
            if (Mathf.Abs(tilt - Pitch) < TurnSettled) { Pitch = tilt; _pitchTo = null; }
        }
        if (_spin != Vector2.Zero)
        {
            Yaw -= _spin.X * dt;
            if (!TopDown || Eye != null) Pitch = ClampPitch(Pitch - _spin.Y * dt);
            _spin *= Mathf.Exp(-dt * SpinDamping);
            if (_spin.Length() < 0.003f) _spin = Vector2.Zero;
        }
        if (_zoomTo is float goal)
        {
            Distance = Mathf.Lerp(Distance, goal, 1 - Mathf.Exp(-dt * ZoomEase));
            if (Mathf.Abs(goal - Distance) < goal * ZoomSettled) { Distance = goal; _zoomTo = null; }
            Apply();
            KeepAnchorUnderCursor();
        }
        Apply();
    }

    public void Zoom(float factor, Vector2? cursor = null)
    {
        if (Eye != null) return;
        float goal = Mathf.Clamp(ZoomGoal * factor, MinDistance, MaxDistance);
        StopGliding();
        _zoomAnchor = cursor != null ? GroundAt(cursor.Value) : null;
        _zoomCursor = cursor ?? Vector2.Zero;
        _zoomTo = goal;
    }

    void KeepAnchorUnderCursor()
    {
        if (_zoomAnchor is not { } anchor || PlaneAt(_zoomCursor, anchor.Y) is not { } after) return;
        Target += anchor - after;
        Settle();
    }

    public void Centre(Vector3 at)
    {
        Still();
        Target = new Vector3(at.X, 0, at.Z);
        SettleNow();
        Apply();
    }

    public readonly record struct View(Vector3 Target, float Distance, float Yaw, float Pitch, bool TopDown);

    public View Now => new(Target, Distance, Yaw, Pitch, TopDown);

    public void Show(View v)
    {
        Still();
        Target = new Vector3(v.Target.X, 0, v.Target.Z);
        SettleNow();
        Distance = Mathf.Clamp(v.Distance, MinDistance, MaxDistance);
        Yaw = v.Yaw;
        Pitch = Mathf.Clamp(v.Pitch, SteepestPitchRadians, FlattestPitchRadians);
        if (v.TopDown != TopDown) ToggleTopDown();
        else Apply();
    }

    Func<Rect2>? _clear;
    List<Vector3>? _refit;

    public Func<Rect2>? Clear
    {
        get => _clear;
        set
        {
            _clear = value;
            if (value == null || _refit is not { } pts) return;
            _refit = null;
            Fit(pts);
        }
    }

    const float FitMargin = 0.04f, OrthoSpan = 0.9f;
    const int FitPasses = 4;

    public void Fit(IReadOnlyList<Vector3> pts)
    {
        if (pts.Count == 0 || Cam == null || Eye != null) return;
        _refit = Clear == null ? pts.ToList() : null;
        Still();
        float x0 = pts.Min(p => p.X), x1 = pts.Max(p => p.X), z0 = pts.Min(p => p.Z), z1 = pts.Max(p => p.Z);
        Target = new Vector3((x0 + x1) / 2, 0, (z0 + z1) / 2);
        SettleNow();
        var vp = GetViewport().GetVisibleRect().Size;
        if (vp.X < 1 || vp.Y < 1) return;
        var (l, r, b, t) = FreeBounds(vp);
        float aspect = vp.X / vp.Y;
        for (int pass = 0; pass <= FitPasses; pass++)
        {
            Apply();
            Distance = Mathf.Clamp(Needed(pts, l, r, b, t, aspect), MinDistance, MaxDistance);
            Apply();
            if (pass == FitPasses) break;
            float lo = float.MaxValue, hi = float.MinValue, bo = float.MaxValue, to = float.MinValue;
            foreach (var p in pts)
            {
                var s = Cam.UnprojectPosition(p);
                float nx = s.X / vp.X * 2 - 1, ny = 1 - s.Y / vp.Y * 2;
                lo = Mathf.Min(lo, nx);
                hi = Mathf.Max(hi, nx);
                bo = Mathf.Min(bo, ny);
                to = Mathf.Max(to, ny);
            }
            float dx = (lo + hi - l - r) / 2, dy = (bo + to - b - t) / 2;
            if (Mathf.Abs(dx) < 0.005f && Mathf.Abs(dy) < 0.005f) break;
            var (sx, sy) = Flat ? (Cam.Size / 2 * aspect, Cam.Size / 2) : (Mathf.Tan(Mathf.DegToRad(Cam.Fov) / 2) * aspect * Distance, Mathf.Tan(Mathf.DegToRad(Cam.Fov) / 2) * Distance);
            var bx = Cam.GlobalBasis.X;
            var by = Cam.GlobalBasis.Y;
            float det = bx.X * by.Z - bx.Z * by.X;
            if (Mathf.Abs(det) < 1e-4f) break;
            float wantX = dx * sx, wantY = dy * sy;
            Target += new Vector3((wantX * by.Z - bx.Z * wantY) / det, 0, (bx.X * wantY - wantX * by.X) / det);
            SettleNow();
        }
    }

    (float l, float r, float b, float t) FreeBounds(Vector2 vp)
    {
        var full = new Rect2(Vector2.Zero, vp);
        var free = Clear?.Invoke() is { } c ? c.Intersection(full) : full;
        if (free.Size.X < vp.X * 0.3f || free.Size.Y < vp.Y * 0.3f) free = full;
        float l = free.Position.X / vp.X * 2 - 1, r = free.End.X / vp.X * 2 - 1;
        float t = 1 - free.Position.Y / vp.Y * 2, b = 1 - free.End.Y / vp.Y * 2;
        float mx = (r - l) * FitMargin, my = (t - b) * FitMargin;
        l += mx;
        r -= mx;
        b += my;
        t -= my;
        if (l >= 0 || r <= 0 || b >= 0 || t <= 0) return (-1 + 2 * FitMargin, 1 - 2 * FitMargin, -1 + 2 * FitMargin, 1 - 2 * FitMargin);
        return (l, r, b, t);
    }

    float Needed(IReadOnlyList<Vector3> pts, float l, float r, float b, float t, float aspect)
    {
        var bx = Cam.GlobalBasis.X;
        var by = Cam.GlobalBasis.Y;
        var fwd = -Cam.GlobalBasis.Z;
        float need = 0;
        if (Flat)
        {
            foreach (var p in pts)
            {
                var q = p - Target;
                float x = q.Dot(bx), y = q.Dot(by);
                need = Mathf.Max(need, Mathf.Max(Mathf.Max(x / (r * aspect), x / (l * aspect)), Mathf.Max(y / t, y / b)));
            }
            return need * 2 / OrthoSpan;
        }
        float tanV = Mathf.Tan(Mathf.DegToRad(Cam.Fov) / 2), tanH = tanV * aspect;
        foreach (var p in pts)
        {
            var q = p - Target;
            float x = q.Dot(bx), y = q.Dot(by), z = q.Dot(fwd);
            need = Mathf.Max(need, Mathf.Max(Mathf.Max(x / (r * tanH), x / (l * tanH)), Mathf.Max(y / (t * tanV), y / (b * tanV))) - z);
            need = Mathf.Max(need, Cam.Near * 2 - z);
        }
        return need;
    }

    public void Frame(Vector3 center, float size)
    {
        Still();
        Target = new Vector3(center.X, 0, center.Z);
        SettleNow();
        Distance = Mathf.Clamp(size * 1.1f, MinDistance, MaxDistance);
        Apply();
    }

    public void Apply()
    {
        if (Cam == null) return;
        if (Eye is { } eye)
        {
            var look = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(Pitch), Mathf.Sin(Pitch), Mathf.Cos(Yaw) * Mathf.Cos(Pitch));
            Project(Camera3D.ProjectionType.Perspective);
            _lift = _liftGoal = 0;
            Cam.GlobalPosition = eye;
            Cam.LookAt(eye + look, Vector3.Up);
            return;
        }
        float pitch = TopDown ? -Mathf.Pi / 2 + 0.0001f : Pitch;
        var dir = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(Yaw) * Mathf.Cos(pitch));
        var pos = Target - dir * Distance;
        Project(TopDown ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective);
        if (TopDown) Cam.Size = Distance * OrthoSpan;
        _liftGoal = Terrain() is { Any: true } g && !TopDown ? Mathf.Max(0, (float)g.At(Coords.FromWorld(pos)) + AboveGround - pos.Y) : 0;
        if (_liftGoal > _lift) _lift = _liftGoal;
        pos.Y += _lift;
        Cam.GlobalPosition = pos;
        var up = TopDown ? new Vector3(Mathf.Sin(Yaw), 0, Mathf.Cos(Yaw)) : Vector3.Up;
        Cam.LookAt(Target, up);
    }

    public Vector3? GroundAt(Vector2 screen)
    {
        var g = Terrain();
        if (!g.Any) return PlaneAt(screen, 0);
        var o = Cam.ProjectRayOrigin(screen);
        var n = Cam.ProjectRayNormal(screen);
        float top = (float)g.Highest + 0.01f;
        float t0 = o.Y > top && n.Y < 0 ? (top - o.Y) / n.Y : 0;
        float t1 = n.Y < -1e-5f ? Mathf.Max(t0, -o.Y / n.Y) : t0 + Cam.Far;
        float Above(float t) { var p = o + n * t; return p.Y - (float)g.At(Coords.FromWorld(p)); }
        if (Above(t0) <= 0) return o + n * t0;
        float step = (t1 - t0) / MarchSteps, prev = t0;
        for (int i = 1; i <= MarchSteps; i++)
        {
            float t = t0 + step * i;
            if (Above(t) > 0) { prev = t; continue; }
            float lo = prev, hi = t;
            for (int k = 0; k < Halvings; k++)
            {
                float mid = (lo + hi) / 2;
                if (Above(mid) > 0) lo = mid;
                else hi = mid;
            }
            return o + n * hi;
        }
        return PlaneAt(screen, 0);
    }

    public Vector3? PlaneAt(Vector2 screen, float y)
    {
        var o = Cam.ProjectRayOrigin(screen);
        var n = Cam.ProjectRayNormal(screen);
        if (Mathf.Abs(n.Y) < 1e-5f) return null;
        var t = (y - o.Y) / n.Y;
        if (t < 0) return null;
        return o + n * t;
    }

    public float PixelsPerUnitAt(Vector3 world)
    {
        var vp = GetViewport().GetVisibleRect().Size;
        float across = Cam.KeepAspect == Camera3D.KeepAspectEnum.Width ? vp.X : vp.Y;
        if (Cam.Projection == Camera3D.ProjectionType.Orthogonal) return across / Mathf.Max(0.0001f, Cam.Size);
        var forward = -Cam.GlobalTransform.Basis.Z;
        float d = Mathf.Max(Cam.Near, (world - Cam.GlobalPosition).Dot(forward));
        return across / (2f * d * Mathf.Tan(Mathf.DegToRad(Cam.Fov) / 2f));
    }

    const float EyeGlanceUnits = 20f;

    public float PixelsPerUnit()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var c = vp / 2;
        if (Eye is { } eye) return PixelsPerUnitAt(GroundAt(c) ?? eye - Cam.GlobalTransform.Basis.Z * EyeGlanceUnits);
        var a = PlaneAt(c, Target.Y);
        var b = PlaneAt(c + new Vector2(0, 50), Target.Y);
        if (a == null || b == null) return 1;
        return 50f / Mathf.Max(0.0001f, (a.Value - b.Value).Length());
    }
}
