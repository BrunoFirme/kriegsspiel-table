using System;
using System.Linq;
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
    public const float StepShare = 0.22f, EyeShare = 0.92f, GirthShare = 0.17f;
    public const float WalkHeights = 1.0f, RunHeights = 2.6f, JumpHeights = 2.2f, FallHeights = 5.4f, SettleHeights = 6f;
    const float Grip = 12f, Lens = 100f, NearShare = 0.04f, ViewHeights = 8f;
    const float SlowestPace = 0.25f, FastestPace = 8f, PaceNotch = 1.25f, SizeNotch = 1.15f;
    const double ShareMovingEvery = 1.0 / 15, ShareStillEvery = 0.5, BoundsEvery = 1.0;
    const float BehindHeights = 3.2f, OverHeadShare = 0.12f, CameraRoomShare = 0.15f, SelfSeenFromHeights = 0.9f;
    const float Probe = 0.6f, BelowHeights = 60f, RoofHeights = 8f, EyesAdapt = 1.6f;
    const string Line = "Click the table to walk · W A S D · run Shift · jump Space · fly F, up Space, down C · view V · take over T · open a door E · pace wheel · Tab frees the mouse";
    const double CommitEvery = 1, AskAgainEvery = 3;
    const float SameSpotShare = 0.01f, SameTurnDegrees = 1f, TakeReachHeights = 4f, LiftedShare = 0.05f;
    static readonly string DefaultBody = AvatarBody.OfPiece(AvatarBody.StandardPack + ".townsman");
    const float FlyBoost = 3f, ReanchorFrom = 1000f;
    const float FloorDegrees = 50f, Margin = 0.01f, StairEase = 16f, StepAheadShare = 1.15f, Landing = 0.7f, ShiftHeights = 30f, GraceSeconds = 0.12f, EdgeShare = 0.2f;
    const float GrowWithin = 0.002f, GrowEase = 5f;
    const double SayAgainEvery = 2;
    const string ToolsLine = "Tab walks on · Leave on the bar ends control";
    const string HoldLine = "Waiting for an umpire to place you";

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
    GroundPatch? _patch;
    Vector3 _at, _before, _vel, _shown, _rideAt, _rideBefore, _bodyWas, _bodyNow, _synced;
    bool _resync = true, _carried, _stepped;
    PieceNode? _ride, _under;
    (string Layer, string Id)? _rideKey;
    string? _sizedBy;
    float _bodyShare;
    Transform3D _rideWas;
    float _seenHeight, _pace = 1, _near, _roofed, _behind, _yawSeen, _eyeLag, _rawWas = float.NaN, _airborne;
    WalkSpace _space = new(1);
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
    string? _spec, _bodyPiece;
    Vec2? _sentPos, _caughtPos;
    double _sentRot, _sentY, _caughtRot, _commitIn, _askIn;

    public event Action<string, Vector3, float>? BodyWanted;
    public event Action? BodyLeft;

    bool Tools => _c.S.IsUmpire;

    public bool InControl => Walking && _asked;

    public void TakeControl(string pieceId, Vector3 at)
    {
        _spec = AvatarBody.OfTaking(pieceId);
        if (Walking) BodyWanted?.Invoke(Body, _at, _c.Rig.Yaw);
        else Enter(at, asked: true);
    }

    public void LeaveControl()
    {
        if (!Walking || _c.S.IsPlayer) return;
        Leave();
        _spec = null;
        _bodyPiece = null;
        BodyLeft?.Invoke();
    }
    public Func<PieceNode, bool> CarriedElsewhere { get; set; } = _ => false;

    public string Body
    {
        get => _spec ?? DefaultBody;
        set
        {
            _spec = value;
            if (Walking) BodyWanted?.Invoke(Body, _at, _c.Rig.Yaw);
        }
    }

    public string? BodyPiece => _bodyPiece;

    public void BodyIs(string? pieceId)
    {
        if (pieceId == _bodyPiece) return;
        _bodyPiece = pieceId;
        _sentPos = _caughtPos = null;
        if (_solids != null) _solids.Ignore = pieceId;
        if (pieceId == null || _c.S.View.Find(Layers.Umpire, pieceId) is not { } p || (p.Pos - Coords.FromWorld(_at)).Length > Height) return;
        (_sentPos, _sentRot, _commitIn) = (p.Pos, p.RotDeg, 0);
    }

    PieceNode? BodyNode => _bodyPiece != null && _pieces.NodeOf(Layers.Umpire, _bodyPiece) is { } n && GodotObject.IsInstanceValid(n) ? n : null;
    public event Action? FlyingChanged;

    public void Fly(bool on)
    {
        if (on == Flying) return;
        Flying = on;
        _vel.Y = 0;
        if (!on) _grounded = false;
        FlyingChanged?.Invoke();
    }
    public bool ShowsSelf => Walking && ThirdPerson && _behind > Height * SelfSeenFromHeights;
    public AvatarPose Pose => new(_shown, _c.Rig.Yaw, Height, null, _bodyPiece);
    public bool Grounded => _grounded;
    public float EyeLag => _eyeLag;
    public bool Riding => _ride != null;
    public Vector3 RideAt => _rideAt;
    public PieceNode? Ride => _ride;
    public int Solids => _solids?.Count ?? 0;

    public void FollowRole()
    {
        if (_c.S.IsPlayer && !Walking) _c.Say(HoldLine);
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
        _eyeLag = 0;
        _grounded = true;
        Height = AvatarPose.PersonMetres * AvatarPose.UnitsPerMetre(_c.S.View);
        _capsule = new CapsuleShape3D();
        _body = new CharacterBody3D
        {
            Name = "Walker", MotionMode = CharacterBody3D.MotionModeEnum.Grounded, UpDirection = Vector3.Up, FloorMaxAngle = Mathf.DegToRad(FloorDegrees),
            FloorStopOnSlope = true, FloorConstantSpeed = true, FloorBlockOnWall = true, SafeMargin = Margin, CollisionLayer = 0, CollisionMask = AvatarSolids.Layer, TopLevel = true,
        };
        _body.AddChild(new CollisionShape3D { Shape = _capsule });
        _c.Scene.AddChild(_body);
        _patch = new GroundPatch(_c.Scene, p => _c.S.Ground.At(p));
        _near = rig.Cam.Near;
        rig.Walking = true;
        Resize(Height);
        if (at is { } given) MoveTo(given);
        _boundsIn = 0;
        Show(0);
        _c.Say(Tools ? ToolsLine : Line);
        BodyWanted?.Invoke(Body, _at, _c.Rig.Yaw);
    }

    public void PlacedAt(Vector3 feet, float yaw, string? piece)
    {
        if (!_c.S.IsPlayer) return;
        if (piece != null) _spec = AvatarBody.OfTaking(piece);
        _c.Rig.Yaw = yaw;
        if (!Walking) Enter(feet);
        else
        {
            MoveTo(feet);
            BodyWanted?.Invoke(Body, _at, _c.Rig.Yaw);
        }
    }

    public void MoveTo(Vector3 at)
    {
        if (!Walking) return;
        _at = at;
        _solids?.Look(_at, now: true);
        _at.Y = Mathf.Max(Ground(_at), FloorUnder(Height * StepShare) ?? float.MinValue);
        _before = _at;
        _bodyWas = _bodyNow = Centre(_at);
        _resync = true;
        _vel = Vector3.Zero;
        _eyeLag = 0;
        _rawWas = float.NaN;
        _grounded = true;
        _ride = null;
    }

    public void Leave()
    {
        if (_body is not { } body) return;
        _body = null;
        body.QueueFree();
        _solids?.Clear();
        _solids = null;
        _patch?.Clear();
        _patch = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var rig = _c.Rig;
        rig.Cam.Near = _near;
        rig.Walking = false;
        rig.LeaveEye();
        WorldShift.Set(Vector3.Zero);
        Immerse(false);
        _roofed = 0;
        _shelter(0);
        _c.Say("");
    }

    void Resize(float height)
    {
        Height = Mathf.Clamp(height, AvatarPose.Smallest, AvatarPose.Largest);
        if (_capsule == null) return;
        Rescale(AvatarPose.PersonMetres / Height);
        _capsule.Radius = AvatarPose.PersonMetres * GirthShare;
        _capsule.Height = AvatarPose.PersonMetres;
        if (_body != null) _body.FloorSnapLength = AvatarPose.PersonMetres * StepShare;
        _c.Rig.Cam.Near = Mathf.Min(_near, Height * NearShare);
    }

    void Rescale(float scale)
    {
        if (_solids != null && Mathf.IsEqualApprox(scale, _space.Scale)) return;
        _space = new WalkSpace(scale) { Anchor = _at };
        _resync = true;
        _bodyWas = _bodyNow = Centre(_at);
        if (_solids != null)
        {
            _solids.Respace(_space);
            return;
        }
        _solids = new AvatarSolids(_c.Scene, _pieces, _space) { Ignore = _bodyPiece };
        _solids.Look(_at, now: true);
    }

    void Reanchor()
    {
        if ((_at - _space.Anchor).Length() * _space.Scale <= ReanchorFrom) return;
        _space.Anchor = _at;
        _resync = true;
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
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } when !held && !Tools:
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
        if (!held && Tools && k.Keycode != Godot.Key.Tab) return false;
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
            case Godot.Key.T:
                TakeAimed();
                return true;
            case Godot.Key.E:
                UseAimed();
                return true;
            case Godot.Key.Equal or Godot.Key.KpAdd:
                Resize(Height * SizeNotch);
                KeepShare();
                return true;
            case Godot.Key.Minus or Godot.Key.KpSubtract:
                Resize(Height / SizeNotch);
                KeepShare();
                return true;
            default:
                return false;
        }
    }

    void KeepShare()
    {
        if (BodyNode is { Thickness: > 0 } own) (_sizedBy, _bodyShare) = (own.Piece.Id, Height / own.Thickness);
    }

    Vector2 Keys()
    {
        if (_typing() || Tools && !Immersed) return Vector2.Zero;
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
        float dt = (float)Math.Min(delta, 0.1), h = Height;
        var rig = _c.Rig;
        Reanchor();
        _solids?.Tick(delta, _at);
        var own = BodyNode;
        if (own != null && CarriedElsewhere(own))
        {
            Follow(own);
            Share(delta);
            return;
        }
        if (own != null)
        {
            Catch(own);
            Grow(own);
        }
        bool frozen = own?.Piece.Locked == true;
        var steer = Steer?.Invoke();
        if (steer is { } s)
        {
            rig.Yaw = s.Yaw;
            rig.Pitch = s.Pitch;
        }
        Carried();
        var move = frozen ? Vector2.Zero : steer?.Move ?? Keys();
        _jump &= !frozen;
        bool run = steer?.Run ?? Input.IsKeyPressed(Godot.Key.Shift);
        float rise = frozen ? 0 : steer?.Rise ?? (_typing() ? 0 : (Input.IsKeyPressed(Godot.Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Godot.Key.C) ? 1 : 0));
        var forward = new Vector3(Mathf.Sin(rig.Yaw), 0, Mathf.Cos(rig.Yaw));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var want = (forward * move.Y + right * move.X).LimitLength() * h * (run ? RunHeights : WalkHeights) * _pace * (Flying ? FlyBoost : 1);
        var flat = new Vector3(_vel.X, 0, _vel.Z).Lerp(want, 1 - Mathf.Exp(-dt * Grip));
        _vel = flat with { Y = Flying ? rise * h * RunHeights * _pace : _grounded ? (_jump ? JumpHeights * h : 0) : _vel.Y - FallHeights * h * dt };
        if (Flying) _grounded = false;
        bool leaps = _jump && _grounded && !Flying;
        _jump = false;
        _before = _at;
        _patch?.Follow(_at, _space);
        if (_resync || _at != _synced) body.GlobalPosition = Centre(_at);
        _resync = false;
        _bodyWas = body.GlobalPosition;
        _stepped = false;
        if (!leaps && !Flying && _vel.Y <= 0) StepUp(body, flat * dt * _space.Scale);
        body.Velocity = _vel * _space.Scale;
        body.MoveAndSlide();
        _at = _space.Out(body.GlobalPosition) - Vector3.Up * (AvatarPose.PersonMetres / 2 / _space.Scale);
        Landed(body, h);
        _airborne = _grounded ? 0 : leaps ? GraceSeconds : _airborne + dt;
        KeepOnTable(dt);
        _bodyNow = body.GlobalPosition;
        _synced = _at;
        Mount();
        Adapt(dt, h);
        Share(delta);
        if (own != null && !frozen) Commit(own, delta);
    }

    Vector3 Centre(Vector3 feet) => _space.In(feet) + Vector3.Up * (AvatarPose.PersonMetres / 2);

    void StepUp(CharacterBody3D body, Vector3 move)
    {
        if (move.LengthSquared() < 1e-10f) return;
        var from = body.GlobalTransform;
        if (!body.TestMove(from, move, null, Margin)) return;
        float lift = AvatarPose.PersonMetres * StepShare;
        var hit = new KinematicCollision3D();
        float room = body.TestMove(from, Vector3.Up * lift, hit, Margin) ? hit.GetTravel().Y : lift;
        var raised = from.Translated(Vector3.Up * room);
        var ahead = move.Normalized() * Mathf.Max(move.Length(), AvatarPose.PersonMetres * GirthShare * StepAheadShare);
        if (body.TestMove(raised, ahead, null, Margin)) return;
        float feet = from.Origin.Y - AvatarPose.PersonMetres / 2;
        var over = raised.Origin + ahead;
        var probe = PhysicsRayQueryParameters3D.Create(over, over + Vector3.Down * (AvatarPose.PersonMetres / 2 + room + Margin * 2), AvatarSolids.Layer);
        var found = body.GetWorld3D().DirectSpaceState.IntersectRay(probe);
        if (found.Count == 0 || !found.TryGetValue("position", out var at) || !found.TryGetValue("normal", out var up)) return;
        float gained = at.AsVector3().Y - feet;
        if (gained <= Margin * EdgeShare || gained > lift || up.AsVector3().AngleTo(Vector3.Up) > body.FloorMaxAngle) return;
        body.GlobalPosition += Vector3.Up * gained;
        _stepped = true;
    }

    void Landed(CharacterBody3D body, float h)
    {
        float ground = Ground(_at);
        if (Flying)
        {
            _under = null;
            _ride = null;
            if (_at.Y < ground)
            {
                _at.Y = ground;
                body.GlobalPosition = Centre(_at);
            }
            return;
        }
        _grounded = body.IsOnFloor() || _stepped;
        if (body.IsOnCeiling() && _vel.Y > 0) _vel.Y = 0;
        if (_at.Y < ground)
        {
            _at.Y = ground;
            _grounded = true;
            body.GlobalPosition = Centre(_at);
        }
        if (_grounded) _vel.Y = 0;
        _under = _grounded && Ray(_at + Vector3.Up * (h * StepShare), _at + Vector3.Down * (h * StepShare * Landing), out var on) != null && on != null ? _solids?.Owner(on) : null;
    }

    void Grow(PieceNode own)
    {
        float tall = own.Thickness;
        if (tall <= 0) return;
        if (_sizedBy != own.Piece.Id)
        {
            (_sizedBy, _bodyShare) = (own.Piece.Id, Height / tall);
            return;
        }
        float want = Mathf.Clamp(tall * _bodyShare, AvatarPose.Smallest, AvatarPose.Largest);
        if (Mathf.Abs(want - Height) > Height * GrowWithin) Resize(want);
    }

    void Follow(PieceNode own)
    {
        _at = own.TablePosition();
        _before = _at;
        _bodyWas = _bodyNow = Centre(_at);
        _resync = true;
        _vel = Vector3.Zero;
        _ride = null;
        _c.Rig.Yaw = own.GlobalRotation.Y - Mathf.Pi;
        _sentPos = _caughtPos = null;
    }

    void Catch(PieceNode own)
    {
        var p = own.Piece;
        if (_sentPos is { } sent && Same(p.Pos, sent, p.RotDeg, _sentRot)) return;
        _sentPos = p.Pos;
        _sentRot = p.RotDeg;
        if (_caughtPos is { } caught && Same(p.Pos, caught, p.RotDeg, _caughtRot)) return;
        _caughtPos = p.Pos;
        _caughtRot = p.RotDeg;
        var at = Coords.ToWorld(p.Pos);
        MoveTo(at with { Y = p.Y > 0 ? (float)p.Y : Ground(at) });
        _c.Rig.Yaw = AvatarBody.YawOf(p.RotDeg);
    }

    void Commit(PieceNode own, double delta)
    {
        if ((_commitIn -= delta) > 0) return;
        _commitIn = CommitEvery;
        var pos = Coords.FromWorld(_at);
        double rot = AvatarBody.RotOf(_c.Rig.Yaw);
        float lift = _at.Y - Ground(_at);
        double y = lift > Height * LiftedShare ? _at.Y : 0;
        if (_sentPos is { } sent && Same(pos, sent, rot, _sentRot) && Math.Abs(y - _sentY) < Height * LiftedShare) return;
        _sentPos = pos;
        _sentRot = rot;
        _sentY = y;
        _c.S.Do(new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = own.Piece.Id, Pos = pos, RotDeg = rot, Y = y });
    }

    bool Same(Vec2 a, Vec2 b, double rotA, double rotB) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < Height * SameSpotShare && Math.Abs(Mathf.PosMod((float)(rotA - rotB) + 180, 360) - 180) < SameTurnDegrees;

    PieceNode? Aimed()
    {
        var rig = _c.Rig;
        var look = new Vector3(Mathf.Sin(rig.Yaw) * Mathf.Cos(rig.Pitch), Mathf.Sin(rig.Pitch), Mathf.Cos(rig.Yaw) * Mathf.Cos(rig.Pitch));
        var eye = _at + Vector3.Up * (Height * EyeShare);
        return Ray(eye, eye + look * (Height * TakeReachHeights), out var hit) != null && hit != null ? _solids?.Owner(hit) : null;
    }

    public void UseAimed()
    {
        if (Aimed() is not { IsPart: true } node || node.GetParent() is not PieceNode { Def: { } def } building
            || building.Parts.Where(p => p.Def?.Tags.Contains(Doors.Tag) == true).ToList().IndexOf(node) is var door && door < 0
            || Doors.Toggled(def, building.Piece.State, door, _c.S.Def) is not { } to)
        {
            _c.Say("No door to open there");
            return;
        }
        _c.S.Do(new KTable.Core.Protocol.SetState { Layer = building.Layer, Id = building.Piece.Id, State = to });
    }

    public void TakeAimed()
    {
        if (Aimed() is not { } node)
        {
            _c.Say("Nothing to take over there");
            return;
        }
        if (!AvatarBody.Takeable(node.Def))
        {
            _c.Say($"{node.CardTitle} can't be taken over");
            return;
        }
        Body = AvatarBody.OfTaking(node.Piece.Id);
    }

    bool Rides(out Transform3D now)
    {
        now = default;
        if (_ride != null && !GodotObject.IsInstanceValid(_ride) && _rideKey is { } key) _ride = _pieces.NodeOf(key.Layer, key.Id);
        if (_ride != null && GodotObject.IsInstanceValid(_ride) && AvatarSolids.Where(_ride) is { } at)
        {
            now = at;
            return true;
        }
        _ride = null;
        return false;
    }

    void Carried()
    {
        _carried = false;
        if (!Rides(out var now) || now.IsEqualApprox(_rideWas)) return;
        _carried = true;
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
        if (_grounded)
        {
            _ride = _under;
            _rideKey = _under is { } on ? (on.Layer, on.Piece.Id) : null;
        }
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

    Vector3? Ray(Vector3 from, Vector3 to) => Ray(from, to, out _);

    Vector3? Ray(Vector3 from, Vector3 to, out StaticBody3D? body)
    {
        body = null;
        var space = _c.Scene.GetWorld3D().DirectSpaceState;
        var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(_space.In(from), _space.In(to), AvatarSolids.Layer));
        if (hit.Count == 0 || !hit.TryGetValue("position", out var at)) return null;
        if (hit.TryGetValue("collider", out var what)) body = what.AsGodotObject() as StaticBody3D;
        return _space.Out(at.AsVector3());
    }

    void Adapt(float dt, float h)
    {
        var eye = ThirdPerson ? WorldShift.ToTable(_c.Rig.Cam.GlobalPosition) : _at + Vector3.Up * (h * EyeShare);
        bool under = Ray(eye, eye + Vector3.Up * (h * RoofHeights)) != null;
        _roofed = Mathf.MoveToward(_roofed, under ? 1 : 0, dt * EyesAdapt);
        _shelter(_roofed);
    }

    void Share(double delta)
    {
        _shareIn += delta;
        float yaw = _c.Rig.Yaw;
        var pose = Rides(out var now) ? new AvatarPose(_rideAt, yaw - YawOf(now), Height, _ride!.Piece.Id, _bodyPiece) : new AvatarPose(_at, yaw, Height, null, _bodyPiece);
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
        _seenHeight = _seenHeight > 0 ? Mathf.Lerp(_seenHeight, Height, 1 - Mathf.Exp(-(float)delta * GrowEase)) : Height;
        Rebase();
        Show((float)delta);
        Immerse(Immersed);
        AskAgain(delta);
        _sayIn -= delta;
        if (_immersed || _sayIn > 0 || Tools) return;
        _sayIn = SayAgainEvery;
        _c.Say(Line);
    }

    void AskAgain(double delta)
    {
        if (BodyNode != null || (_askIn -= delta) > 0) return;
        _askIn = AskAgainEvery;
        if (AvatarBody.Taking(_spec) != null) _spec = null;
        BodyWanted?.Invoke(Body, _at, _c.Rig.Yaw);
    }

    void Immerse(bool on)
    {
        if (on == _immersed) return;
        _immersed = on;
        if (Tools && Walking) _c.Say(on ? Line : ToolsLine);
        ImmersedChanged?.Invoke(on);
    }

    void Ease(float raw, float delta, bool rides)
    {
        float most = Height * StepShare;
        bool follows = !Flying && !rides && !float.IsNaN(_rawWas) && Mathf.Abs(raw - _rawWas) < most * 2;
        _eyeLag = follows ? Mathf.Clamp(_eyeLag - (_airborne < GraceSeconds ? raw - _rawWas : 0), -most, most) * Mathf.Exp(-delta * StairEase) : 0;
        _rawWas = raw;
    }

    void Rebase()
    {
        var by = WorldShift.By;
        if (new Vector2(_at.X - by.X, _at.Z - by.Z).Length() > Height * ShiftHeights || by == Vector3.Zero)
            WorldShift.Set(new Vector3(Mathf.Round(_at.X), 0, Mathf.Round(_at.Z)));
    }

    void Show(float delta)
    {
        var rig = _c.Rig;
        float between = (float)Engine.GetPhysicsInterpolationFraction();
        bool rides = Rides(out var carried) && _carried;
        if (rides) Turn(carried);
        var own = BodyNode;
        bool follows = own != null && CarriedElsewhere(own);
        var exact = follows ? own!.GlobalPosition
            : rides ? WorldShift.ToGlobal(carried * _rideBefore.Lerp(_rideAt, between))
            : WorldShift.ToGlobal(_space.Anchor) + (_bodyWas.Lerp(_bodyNow, between) - Vector3.Up * (AvatarPose.PersonMetres / 2)) / _space.Scale;
        var feet = WorldShift.ToTable(exact);
        _shown = feet;
        Ease(exact.Y, delta, rides || follows);
        rig.Target = feet;
        rig.Distance = Mathf.Clamp(Height * ViewHeights, rig.MinDistance, rig.MaxDistance);
        var up = Vector3.Up * ((_seenHeight > 0 ? _seenHeight : Height) * EyeShare + _eyeLag);
        rig.LookFrom(ThirdPerson ? Behind(feet + up) : feet + up, Lens, exact: ThirdPerson ? null : exact + up);
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
