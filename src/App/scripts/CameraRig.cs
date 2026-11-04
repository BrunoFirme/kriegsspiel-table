using System;
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

    public override void _Ready()
    {
        Cam = new Camera3D { Near = 0.05f, Far = 10000, Fov = 50, CullMask = 0xFFFFF & ~Table.PictureOnlyLayer };
        AddChild(Cam);
        Apply();
    }

    public event Action? ViewChanged;

    public void ToggleTopDown()
    {
        TopDown = !TopDown;
        ViewChanged?.Invoke();
        Apply();
    }

    public void Orbit(Vector2 rel)
    {
        if (TopDown) { Yaw -= rel.X * 0.005f; Apply(); return; }
        Yaw -= rel.X * 0.005f;
        Pitch = Mathf.Clamp(Pitch - rel.Y * 0.005f, SteepestPitchRadians, FlattestPitchRadians);
        Apply();
    }

    public void PanScreen(Vector2 from, Vector2 to)
    {
        var a = PlaneAt(from, Target.Y); var b = PlaneAt(to, Target.Y);
        if (a == null || b == null) return;
        Target += a.Value - b.Value;
        Settle();
        Apply();
    }

    public void PanWorld(Vector3 d) { _zoomAnchor = null; Target += d; Settle(); Apply(); }

    public Func<Ground> Terrain = () => Ground.Flat;
    const float AboveGround = 0.6f;
    const int MarchSteps = 160, Halvings = 14;

    void Settle() => Target.Y = (float)Terrain().At(Coords.FromWorld(Target));

    public const float GlideDamping = 10f, SpinDamping = 14f;
    Vector3 _glide;
    Vector2 _spin;

    public void Fling(Vector3 unitsPerSecond)
    {
        _glide = unitsPerSecond; _glide.Y = 0;
        float cap = Distance * 2;
        if (_glide.Length() > cap) _glide = _glide.Normalized() * cap;
    }
    public void FlingOrbit(Vector2 radiansPerSecond) => _spin = radiansPerSecond.LimitLength(Mathf.Pi);
    public void StopGliding() { _glide = Vector3.Zero; _spin = Vector2.Zero; _yawTo = null; }
    public void Still() { StopGliding(); _zoomTo = null; }
    public bool Gliding => _glide != Vector3.Zero || _spin != Vector2.Zero || _yawTo != null || _zoomTo != null;

    const float ZoomEase = 16f, ZoomSettled = 0.001f;
    float? _zoomTo;
    Vector3? _zoomAnchor;
    Vector2 _zoomCursor;

    public float ZoomGoal => _zoomTo ?? Distance;

    float? _yawTo;

    public void FaceNorth()
    {
        _spin = Vector2.Zero;
        _yawTo = Yaw + Mathf.Wrap(Mathf.Pi - Yaw, -Mathf.Pi, Mathf.Pi);
    }

    public void Tick(double delta)
    {
        if (!Gliding) return;
        float dt = (float)Math.Min(delta, 0.1);
        if (_glide != Vector3.Zero)
        {
            Target += _glide * dt; Settle();
            _glide *= Mathf.Exp(-dt * GlideDamping);
            if (_glide.Length() < 0.004f * Distance) _glide = Vector3.Zero;
        }
        if (_yawTo is float to)
        {
            Yaw = Mathf.Lerp(Yaw, to, 1 - Mathf.Exp(-dt * 12));
            if (Mathf.Abs(to - Yaw) < 0.002f) { Yaw = to; _yawTo = null; }
        }
        if (_spin != Vector2.Zero)
        {
            Yaw -= _spin.X * dt;
            if (!TopDown) Pitch = Mathf.Clamp(Pitch - _spin.Y * dt, SteepestPitchRadians, FlattestPitchRadians);
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
        Settle();
        Apply();
    }

    public readonly record struct View(Vector3 Target, float Distance, float Yaw, float Pitch, bool TopDown);

    public View Now => new(Target, Distance, Yaw, Pitch, TopDown);

    public void Show(View v)
    {
        Still();
        Target = new Vector3(v.Target.X, 0, v.Target.Z);
        Settle();
        Distance = Mathf.Clamp(v.Distance, MinDistance, MaxDistance);
        Yaw = v.Yaw;
        Pitch = Mathf.Clamp(v.Pitch, SteepestPitchRadians, FlattestPitchRadians);
        if (v.TopDown != TopDown) ToggleTopDown(); else Apply();
    }

    public void Frame(Vector3 center, float size)
    {
        Still();
        Target = new Vector3(center.X, 0, center.Z);
        Settle();
        Distance = Mathf.Clamp(size * 1.1f, MinDistance, MaxDistance);
        Apply();
    }

    public void Apply()
    {
        if (Cam == null) return;
        float pitch = TopDown ? -Mathf.Pi / 2 + 0.0001f : Pitch;
        var dir = new Vector3(Mathf.Sin(Yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(Yaw) * Mathf.Cos(pitch));
        var pos = Target - dir * Distance;
        Cam.Projection = TopDown ? Camera3D.ProjectionType.Orthogonal : Camera3D.ProjectionType.Perspective;
        if (TopDown) Cam.Size = Distance * 0.9f;
        if (Terrain() is { Any: true } g && !TopDown) pos.Y = Mathf.Max(pos.Y, (float)g.At(Coords.FromWorld(pos)) + AboveGround);
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
            for (int k = 0; k < Halvings; k++) { float mid = (lo + hi) / 2; if (Above(mid) > 0) lo = mid; else hi = mid; }
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
        float h = GetViewport().GetVisibleRect().Size.Y;
        if (Cam.Projection == Camera3D.ProjectionType.Orthogonal) return h / Mathf.Max(0.0001f, Cam.Size);
        var forward = -Cam.GlobalTransform.Basis.Z;
        float d = Mathf.Max(Cam.Near, (world - Cam.GlobalPosition).Dot(forward));
        return h / (2f * d * Mathf.Tan(Mathf.DegToRad(Cam.Fov) / 2f));
    }

    public float PixelsPerUnit()
    {
        var vp = GetViewport().GetVisibleRect().Size;
        var c = vp / 2;
        var a = PlaneAt(c, Target.Y); var b = PlaneAt(c + new Vector2(0, 50), Target.Y);
        if (a == null || b == null) return 1;
        return 50f / Mathf.Max(0.0001f, (a.Value - b.Value).Length());
    }
}
