using System;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

public readonly record struct Steering(float Yaw, float Pitch, Vector2 Move, bool Run, float Rise = 0);

public partial class AfterPieces : Node
{
    const int LastInTheFrame = 1000;

    public Action<double>? Then;

    public AfterPieces() { ProcessPriority = LastInTheFrame; }

    public override void _Process(double delta) => Then?.Invoke(delta);
}

sealed class Walker
{
    public const float StepShare = 0.3f, EyeShare = 0.92f, GirthShare = 0.18f;
    public const float WalkHeights = 1.9f, RunHeights = 4.2f, JumpHeights = 2.2f, FallHeights = 5.4f, SettleHeights = 6f;
    const float Grip = 12f, Lens = 75f, NearShare = 0.04f, ViewHeights = 8f;
    const float SlowestPace = 0.25f, FastestPace = 8f, PaceNotch = 1.25f, SizeNotch = 1.15f;
    const double ShareMovingEvery = 1.0 / 15, ShareStillEvery = 0.5, BoundsEvery = 1.0;
    const float BehindHeights = 3.2f, OverHeadShare = 0.12f, CameraRoomShare = 0.15f, SelfSeenFromHeights = 0.9f;
    const float Probe = 0.6f, BelowHeights = 60f, RoofHeights = 8f, EyesAdapt = 1.6f;
    const string Line = "Click the table to walk · W A S D · run Shift · jump Space · fly F, up Space, down C · view V · pace wheel · size = − · Tab frees the mouse";
    const float FlyBoost = 3f;
    const double SayAgainEvery = 2;

    readonly TableContext _c;
    readonly PieceLayer _pieces;
    readonly Func<bool> _typing;
    readonly Action<float> _shelter;

    public Walker(TableContext c, PieceLayer pieces, Func<bool> typing, Action<float> shelter)
    {
        _c = c;
        _pieces = pieces;
        _typing = typing;
        _shelter = shelter;
    }

    public event Action<AvatarPose>? Moved;
    public event Action<bool>? ImmersedChanged;
    public Func<Steering?>? Steer;

    CharacterBody3D? _body;
    CapsuleShape3D? _capsule;
    AvatarSolids? _solids;
    Vector3 _at, _before, _vel, _shown, _rideAt, _rideBefore;
    PieceNode? _ride, _under;
    Transform3D _rideWas;
    float _fall, _pace = 1, _near, _roofed, _behind, _yawSeen, _metres = 1;
    AvatarPose _sharedPose;
    bool _grounded, _jump, _asked, _immersed;
    double _shareIn, _boundsIn, _sayIn;
    TableArea _bounds;

    public bool Walking => _body != null;
    public bool Immersed => Walking && (Steer != null || Input.MouseMode == Input.MouseModeEnum.Captured);
    public float Height { get; private set; } = AvatarPose.Usual;
    public Vector3 Feet => _at;
    public bool ThirdPerson { get; set; }
    public bool Flying { get; set; }
    public int Colour { get; set; } = AvatarPose.NoColour;
    public string? Body { get; set; }
    public event Action? FlyingChanged;

    public void Fly(bool on)
    {
        if (on == Flying) return;
        Flying = on;
        _fall = 0;
        if (!on) _grounded = false;
        FlyingChanged?.Invoke();
    }
    public bool ShowsSelf => Walking && ThirdPerson && _behind > Height * SelfSeenFromHeights;
    public AvatarPose Pose => new(_shown, _c.Rig.Yaw, Height, null, Body);
    public bool Grounded => _grounded;
    public bool Riding => _ride != null;
    public Vector3 RideAt => _rideAt;
    public PieceNode? Ride => _ride;
    public int Solids => _solids?.Count ?? 0;

    public void FollowRole()
    {
        if (_c.S.IsPlayer && !Walking) Enter();
        else if (!_c.S.IsPlayer && Walking && !_asked) Leave();
    }

    public void Enter(Vector3? at = null, bool asked = false)
    {
        if (Walking) return;
        _asked = asked;
        var rig = _c.Rig;
        var start = at ?? rig.Target;
        _at = new Vector3(start.X, 0, start.Z);
        _at.Y = Ground(_at);
        _before = _at;
        _vel = Vector3.Zero;
        _fall = 0;
        _grounded = true;
        float perMetre = AvatarPose.UnitsPerMetre(_c.S.View);
        _metres = 1 / perMetre;
        Height = AvatarPose.PersonMetres * perMetre;
        _capsule = new CapsuleShape3D();
        _body = new CharacterBody3D { Name = "Walker", MotionMode = CharacterBody3D.MotionModeEnum.Floating, CollisionLayer = 0, CollisionMask = AvatarSolids.Layer };
        _body.AddChild(new CollisionShape3D { Shape = _capsule });
        _c.Scene.AddChild(_body);
        _solids = new AvatarSolids(_c.Scene, _pieces, _metres);
        _solids.Look(_at, now: true);
        _near = rig.Cam.Near;
        rig.Walking = true;
        Resize(Height);
        _boundsIn = 0;
        Show();
        _c.Say(Line);
    }

    public void MoveTo(Vector3 at)
    {
        if (!Walking) return;
        _at = new Vector3(at.X, 0, at.Z);
        _at.Y = Mathf.Max(Ground(_at), FloorUnder(Height * BelowHeights) ?? float.MinValue);
        _before = _at;
        _vel = Vector3.Zero;
        _fall = 0;
        _grounded = true;
        _ride = null;
        _solids?.Look(_at, now: true);
    }

    public void Leave()
    {
        if (_body is not { } body) return;
        _body = null;
        body.QueueFree();
        _solids?.Clear();
        _solids = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var rig = _c.Rig;
        rig.Cam.Near = _near;
        rig.Walking = false;
        rig.LeaveEye();
        Immerse(false);
        _roofed = 0;
        _shelter(0);
        _c.Say("");
    }

    void Resize(float height)
    {
        Height = Mathf.Clamp(height, AvatarPose.Smallest, AvatarPose.Largest);
        if (_capsule == null) return;
        _capsule.Radius = Height * GirthShare * _metres;
        _capsule.Height = Height * (1 - StepShare) * _metres;
        _c.Rig.Cam.Near = Mathf.Min(_near, Height * NearShare);
    }

    float Ground(Vector3 at) => (float)_c.S.Ground.At(Coords.FromWorld(at));

    public bool Takes(InputEvent e)
    {
        if (!Walking) return false;
        bool held = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion mm when held:
                _c.Rig.Orbit(mm.Relative);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when held:
                _pace = Mathf.Min(FastestPace, _pace * PaceNotch);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when held:
                _pace = Mathf.Max(SlowestPace, _pace / PaceNotch);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when !held:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                return true;
            case InputEventMouseButton when held:
                return true;
            case InputEventKey { Pressed: true, Echo: false } k:
                return Key(k, held);
            default:
                return false;
        }
    }

    bool Key(InputEventKey k, bool held)
    {
        switch (k.Keycode)
        {
            case Godot.Key.Tab:
                Input.MouseMode = held ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
                return true;
            case Godot.Key.Escape when held:
                Input.MouseMode = Input.MouseModeEnum.Visible;
                return true;
            case Godot.Key.Space:
                _jump = true;
                return true;
            case Godot.Key.F:
                Fly(!Flying);
                return true;
            case Godot.Key.V:
                ThirdPerson = !ThirdPerson;
                return true;
            case Godot.Key.Equal or Godot.Key.KpAdd:
                Resize(Height * SizeNotch);
                return true;
            case Godot.Key.Minus or Godot.Key.KpSubtract:
                Resize(Height / SizeNotch);
                return true;
            default:
                return false;
        }
    }

    Vector2 Keys()
    {
        if (_typing()) return Vector2.Zero;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Godot.Key.W)) move.Y += 1;
        if (Input.IsKeyPressed(Godot.Key.S)) move.Y -= 1;
        if (Input.IsKeyPressed(Godot.Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Godot.Key.A)) move.X -= 1;
        return move;
    }

    public void Step(double delta)
    {
        if (_body is not { } body || _capsule == null) return;
        float dt = (float)Math.Min(delta, 0.1), h = Height, step = h * StepShare;
        var rig = _c.Rig;
        _solids?.Tick(delta, _at);
        var steer = Steer?.Invoke();
        if (steer is { } s)
        {
            rig.Yaw = s.Yaw;
            rig.Pitch = s.Pitch;
        }
        Carried();
        var move = steer?.Move ?? Keys();
        bool run = steer?.Run ?? Input.IsKeyPressed(Godot.Key.Shift);
        float rise = steer?.Rise ?? (_typing() ? 0 : (Input.IsKeyPressed(Godot.Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Godot.Key.C) ? 1 : 0));
        var forward = new Vector3(Mathf.Sin(rig.Yaw), 0, Mathf.Cos(rig.Yaw));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var want = (forward * move.Y + right * move.X).LimitLength() * h * (run ? RunHeights : WalkHeights) * _pace * (Flying ? FlyBoost : 1);
        _vel = _vel.Lerp(want, 1 - Mathf.Exp(-dt * Grip));
        _before = _at;
        body.GlobalPosition = (_at + Vector3.Up * step) * _metres + Vector3.Up * (_capsule.Height / 2);
        body.Velocity = _vel * _metres;
        body.MoveAndSlide();
        _at.X = body.GlobalPosition.X / _metres;
        _at.Z = body.GlobalPosition.Z / _metres;
        KeepOnTable(dt);
        if (Flying) Hover(dt, h, rise * h * RunHeights * _pace);
        else Settle(dt, h, step);
        Mount();
        Adapt(dt, h);
        Share(delta);
    }

    bool Rides(out Transform3D now)
    {
        now = default;
        if (_ride != null && AvatarSolids.Where(_ride) is { } at)
        {
            now = at;
            return true;
        }
        _ride = null;
        return false;
    }

    void Carried()
    {
        if (!Rides(out var now) || now.IsEqualApprox(_rideWas)) return;
        _at = now * _rideAt;
        Turn(now);
    }

    static float YawOf(Transform3D space) => space.Basis.GetEuler().Y;

    void Turn(Transform3D now)
    {
        float yaw = YawOf(now);
        _c.Rig.Yaw += Mathf.AngleDifference(_yawSeen, yaw);
        _yawSeen = yaw;
    }

    void Mount()
    {
        var was = _ride;
        if (_grounded) _ride = _under;
        if (!Rides(out var now)) return;
        if (_ride != was) _yawSeen = YawOf(now);
        _rideWas = now;
        var inside = _rideWas.AffineInverse();
        _rideAt = inside * _at;
        _rideBefore = inside * _before;
    }

    void KeepOnTable(float dt)
    {
        _boundsIn -= dt;
        if (_boundsIn <= 0)
        {
            _boundsIn = BoundsEvery;
            _bounds = TableExtent.Of(_c.S.View);
        }
        var centre = Coords.ToWorld(_bounds.Centre);
        float hx = (float)_bounds.Size.W / 2, hz = (float)_bounds.Size.H / 2;
        _at.X = Mathf.Clamp(_at.X, centre.X - hx, centre.X + hx);
        _at.Z = Mathf.Clamp(_at.Z, centre.Z - hz, centre.Z + hz);
    }

    void Settle(float dt, float h, float step)
    {
        float ground = Ground(_at), solid = FloorUnder(step, out var on) ?? float.MinValue, floor = Mathf.Max(ground, solid);
        _under = solid >= ground && on != null ? _solids?.Owner(on) : null;
        if (_grounded && _jump)
        {
            _fall = JumpHeights * h;
            _grounded = false;
        }
        _jump = false;
        if (_grounded)
        {
            if (floor < _at.Y - step) _grounded = false;
            else _at.Y = Mathf.MoveToward(_at.Y, floor, h * SettleHeights * dt);
        }
        if (!_grounded)
        {
            _fall -= FallHeights * h * dt;
            float rise = _fall * dt;
            if (rise > 0 && Ceiling(h, rise) is { } top)
            {
                rise = Mathf.Max(0, top - h - _at.Y);
                _fall = 0;
            }
            _at.Y += rise;
            if (_at.Y <= floor)
            {
                _at.Y = floor;
                _fall = 0;
                _grounded = true;
            }
        }
        if (_at.Y < ground) _at.Y = ground;
    }

    void Hover(float dt, float h, float climb)
    {
        _jump = false;
        _grounded = false;
        _under = null;
        _ride = null;
        float rise = climb * dt;
        if (rise > 0 && Ceiling(h, rise) is { } top) rise = Mathf.Max(0, top - h - _at.Y);
        _at.Y += rise;
        float floor = Mathf.Max(Ground(_at), FloorUnder(Height * StepShare) ?? float.MinValue);
        if (_at.Y < floor) _at.Y = floor;
    }

    float? FloorUnder(float step) => FloorUnder(step, out _);

    float? FloorUnder(float step, out StaticBody3D? on)
    {
        float reach = Height * GirthShare * Probe;
        float? best = null;
        on = null;
        foreach (var (dx, dz) in new[] { (0f, 0f), (reach, 0f), (-reach, 0f), (0f, reach), (0f, -reach) })
        {
            var from = new Vector3(_at.X + dx, _at.Y + step, _at.Z + dz);
            if (Ray(from, from + Vector3.Down * (Height * BelowHeights), out var body) is not { } hit || best != null && hit.Y <= best) continue;
            best = hit.Y;
            on = body;
        }
        return best;
    }

    float? Ceiling(float h, float rise)
    {
        var from = _at + Vector3.Up * (h * (1 - GirthShare));
        return Ray(from, from + Vector3.Up * (h * GirthShare + rise))?.Y;
    }

    Vector3? Ray(Vector3 from, Vector3 to) => Ray(from, to, out _);

    Vector3? Ray(Vector3 from, Vector3 to, out StaticBody3D? body)
    {
        body = null;
        var space = _c.Scene.GetWorld3D().DirectSpaceState;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from * _metres, to * _metres, AvatarSolids.Layer));
        if (hit.Count == 0 || !hit.TryGetValue("position", out var at)) return null;
        if (hit.TryGetValue("collider", out var what)) body = what.AsGodotObject() as StaticBody3D;
        return at.AsVector3() / _metres;
    }

    void Adapt(float dt, float h)
    {
        var eye = ThirdPerson ? _c.Rig.Cam.GlobalPosition : _at + Vector3.Up * (h * EyeShare);
        bool under = Ray(eye, eye + Vector3.Up * (h * RoofHeights)) != null;
        _roofed = Mathf.MoveToward(_roofed, under ? 1 : 0, dt * EyesAdapt);
        _shelter(_roofed);
    }

    void Share(double delta)
    {
        _shareIn += delta;
        float yaw = _c.Rig.Yaw;
        var pose = Rides(out var now) ? new AvatarPose(_rideAt, yaw - YawOf(now), Height, _ride!.Piece.Id, Body) : new AvatarPose(_at, yaw, Height, null, Body);
        bool moved = pose.Ride != _sharedPose.Ride || pose.Body != _sharedPose.Body || !pose.At.IsEqualApprox(_sharedPose.At) || !Mathf.IsEqualApprox(pose.Yaw, _sharedPose.Yaw)
            || !Mathf.IsEqualApprox(pose.Height, _sharedPose.Height);
        if (_shareIn < (moved ? ShareMovingEvery : ShareStillEvery)) return;
        _shareIn = 0;
        _sharedPose = pose;
        Moved?.Invoke(pose);
    }

    public void Tick(double delta)
    {
        if (!Walking) return;
        Show();
        Immerse(Immersed);
        _sayIn -= delta;
        if (_immersed || _sayIn > 0) return;
        _sayIn = SayAgainEvery;
        _c.Say(Line);
    }

    void Immerse(bool on)
    {
        if (on == _immersed) return;
        _immersed = on;
        ImmersedChanged?.Invoke(on);
    }

    void Show()
    {
        var rig = _c.Rig;
        float between = (float)Engine.GetPhysicsInterpolationFraction();
        bool rides = Rides(out var carried);
        if (rides) Turn(carried);
        var feet = rides ? carried * _rideBefore.Lerp(_rideAt, between) : _before.Lerp(_at, between);
        _shown = feet;
        rig.Target = feet;
        rig.Distance = Mathf.Clamp(Height * ViewHeights, rig.MinDistance, rig.MaxDistance);
        var eye = feet + Vector3.Up * (Height * EyeShare);
        rig.LookFrom(ThirdPerson ? Behind(eye) : eye, Lens);
    }

    Vector3 Behind(Vector3 head)
    {
        var rig = _c.Rig;
        var look = new Vector3(Mathf.Sin(rig.Yaw) * Mathf.Cos(rig.Pitch), Mathf.Sin(rig.Pitch), Mathf.Cos(rig.Yaw) * Mathf.Cos(rig.Pitch));
        var pivot = head + Vector3.Up * (Height * OverHeadShare);
        float room = Height * CameraRoomShare;
        _behind = Height * BehindHeights;
        if (Ray(pivot, pivot - look * (_behind + room)) is { } hit) _behind = Mathf.Max(0, (hit - pivot).Length() - room);
        var at = pivot - look * _behind;
        float ground = Ground(at) + room;
        return at.Y < ground ? at with { Y = ground } : at;
    }
}
