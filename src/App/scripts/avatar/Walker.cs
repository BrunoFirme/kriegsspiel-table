using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using KTable.Client;
using KTable.Client.Prefs;
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

readonly record struct WalkRights(bool Fly, bool Take, bool View, bool Dice, bool Measure, bool Ping, bool Walk)
{
    public static readonly WalkRights All = new(true, true, true, true, true, true, true);
}

sealed class Walker
{
    public const float StepShare = 0.22f, EyeShare = 0.92f, GirthShare = 0.17f;
    public const float WalkHeights = 1.5f / AvatarPose.PersonMetres, RunHeights = 5f / AvatarPose.PersonMetres, JumpHeights = 2.2f, FallHeights = 5.4f, SettleHeights = 6f;
    public const float Grip = 60f;
    const float NearShare = 0.04f, ViewHeights = 8f;
    const float NearestBehind = 1.2f, FarthestBehind = 9f, BehindNotch = 1.2f, SizeNotch = 1.15f;
    const double ShareMovingEvery = 1.0 / 15, ShareStillEvery = 0.5, BoundsEvery = 1.0;
    const float BehindHeights = 3.6f, OverHeadShare = -0.08f, CameraRoomShare = 0.15f, SelfSeenFromHeights = 0.9f;
    const float Probe = 0.6f, BelowHeights = 60f, RoofHeights = 8f, EyesAdapt = 1.6f;
    const string Line = "W S walk · A D turn · Q E strafe · right-drag looks · wheel zooms · run Shift · jump Space · fly F · sneak X · march C · door click or R · take over T · measure M · ping middle click";
    public const float TurnRate = 3.1f;
    const float FaceEase = 28f, BoomEase = 14f, BoomPitch = -0.22f, SwoopHeights = 9f, HillFree = 0.27f;
    const double CommitEvery = 1, AskAgainEvery = 3, RideSettleSeconds = 0.25;
    const float SeatLow = 0.16f, SeatHigh = 0.47f, SeatReach = 0.75f, SeatStep = 0.09f, SeatClear = 0.45f, SeatLift = 0.01f, SeatHips = 0.27f, SeatEyeDrop = 0.27f;
    const float BackAbove = 0.25f, BackReach = 0.24f, TableLow = 0.39f, TableHigh = 0.62f, TableReach = 0.5f, TableStep = 0.1f, TableRise = 0.4f;
    const float SeatEdgeStep = 0.02f, SeatEdgeMost = 0.3f, SeatShiftMost = 0.12f, SeatSpan = 0.12f;
    const int SeatProbes = 16, OpenSides = 12;
    const float GodAheadHeights = 2, GodDistanceHeights = 8;
    const float SameSpotShare = 0.01f, SameTurnDegrees = 1f, TakeReachHeights = 4f, LiftedShare = 0.05f;
    const float ReanchorFrom = 1000f;
    public const float CruiseHeights = 4f, BoostHeights = 12f, AirGrip = 2.2f, AirDrag = 0.7f;
    const float BurstHeights = 9f, BurstEvery = 0.45f, TakeOffHeights = 1.2f;
    const float BoostLens = 14f, BurstLens = 10f, LensEase = 6f, FlashFade = 2.6f, CameraOutHeights = 2.5f;
    const float FloorDegrees = 46f, SteepDegrees = 60f, Margin = 0.01f, StairEase = 10f, StepAheadShare = 1.15f, Landing = 0.7f, ShiftHeights = 30f, GraceSeconds = 0.12f, EdgeShare = 0.2f;
    const float GrowWithin = 0.002f, GrowEase = 5f;
    const float HillPull = 1f, HillEase = 14f, SlideHeights = 1.1f, SlideGrip = 4f;
    const float BobHeights = 0.02f, WalkStepHeights = 0.42f, RunStepHeights = 0.72f, BobEase = 4f, BobFrom = 0.15f;
    const ulong JumpHeldMs = 120;
    const float SinkMost = 0.2f, SinkEase = 12f;
    const string GodLine = "Tab back to the avatar · C finds it";
    const string UmpireLine = "W S walk · A D turn · Q E strafe · right-drag looks · Tab or wheel out God view";
    const string HoldLine = "Waiting for an umpire to place you";
    const string NoFlyLine = "Flying is off for you", NoViewLine = "Third person is off for you", NoTakeLine = "Taking over is off for you";
    const string NoMeasureLine = "Measuring is off for you", NoPingLine = "Pinging is off for you", NoWalkLine = "Changing your walk is off for you";
    const float SneakHeights = 0.75f / AvatarPose.PersonMetres, QuickHeights = 1.4f / AvatarPose.PersonMetres, DoubleQuickHeights = 2.3f / AvatarPose.PersonMetres;
    const float AimHeights = 300f;
    const float HandShare = 0.75f, HandAhead = 0.3f, HandAside = 0.15f, ThrowLift = 0.35f, ThrowHeights = 2.4f, ThrowStep = 1f / 60, BounceKeep = 0.45f, StillHeights = 0.5f;
    const int ThrowSteps = 240, Bounces = 3;
    const ulong PingEveryMs = 600;

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
    public Func<Steering?>? Steer { get; set; }
    public BoundSpace? Fence;
    public event Action<Vector3, Vector3>? Fenced;

    CharacterBody3D? _body;
    CapsuleShape3D? _capsule;
    AvatarSolids? _solids;
    RaisedSolids? _raised;
    GroundPatch? _patch;
    Vector3 _at, _before, _vel, _shown, _rideAt, _rideBefore, _bodyWas, _bodyNow, _synced;
    bool _resync = true, _carried, _stepped, _inert;
    double _sinceHeld = double.PositiveInfinity;
    PieceNode? _ride, _under;
    (string Layer, string Id)? _rideKey;
    string? _sizedBy;
    float _bodyShare;
    Transform3D _rideWas;
    ulong _jumpAt;
    Vector3? _steep;
    Vector3 _bobWas;
    float _hill = 1, _bobPhase, _bobOn, _sink, _sinkShown;
    float _seenHeight, _far = BehindHeights, _near, _roofed, _behind, _yawSeen, _eyeLag, _rawWas = float.NaN, _airborne;
    WalkSpace _space = new(1);
    AvatarPose _sharedPose;
    bool _grounded, _asked, _immersed, _boosting;
    float _burstIn, _kick, _flash;
    readonly SphereShape3D _sweep = new();
    double _shareIn, _boundsIn;
    TableArea _bounds;

    float Lens => _c.Settings is { } cfg ? Math.Clamp(cfg.EyeFovDeg, Settings.EyeFovLeast, Settings.EyeFovMost) : Settings.EyeFovUsual;

    public bool Walking => _body != null;
    public bool Immersed => Walking && !GodView;
    public float Height { get; private set; } = AvatarPose.Usual;
    public Vector3 Feet => _at;
    bool _third, _looking, _wantSeat;
    string? _walk, _stance, _act;
    Vector3? _seatAt;
    Vector3 _seatFrom;
    float _seatYaw;

    public string? Style => _stance ?? _walk;
    public string? Stance => _stance;
    public bool Styled => BodyNode?.Def?.Rig != null;

    public string? WalkChoice
    {
        get => _walk;
        set
        {
            if (Allowed.Walk) _walk = value;
            else _c.Say(NoWalkLine);
        }
    }

    public string? ActChoice
    {
        get => _act;
        set
        {
            if (!Allowed.Walk)
            {
                _c.Say(NoWalkLine);
                return;
            }
            _act = value;
            _wantSeat = RigMotion.Parts(value).Whole == RigMotion.Sit && _seatAt == null;
        }
    }

    public bool Sitting => _seatAt != null;

    public void Stand(string? stance)
    {
        if (Allowed.Walk) _stance = stance;
        else _c.Say(NoWalkLine);
    }

    float Pace(bool run) => PaceOf(_stance, run);

    public static float PaceOf(string? stance, bool run) =>
        stance == RigMotion.Sneak ? SneakHeights : stance == RigMotion.March ? (run ? DoubleQuickHeights : QuickHeights) : run ? RunHeights : WalkHeights;
    Vector2 _lookedFrom;
    bool Orbiting => _third && Steer == null;

    public bool ThirdPerson
    {
        get => _third;
        set
        {
            if (value == _third) return;
            _third = value;
            if (GodView) return;
            if (value) _facing = _c.Rig.Yaw;
            else _c.Rig.Yaw = _facing;
        }
    }
    public bool Flying { get; set; }
    public event Action? Burst;
    public event Action<Vector3>? Pinged;
    public bool Measuring { get; set; }
    ulong _pingedAt;
    public float Flash => _flash;
    public float Speed => Walking ? _vel.Length() / Height : 0;
    public bool Boosting => Flying && _boosting;
    public WalkRights Allowed
    {
        get
        {
            if (!_c.S.IsPlayer) return WalkRights.All;
            var own = _c.S.View.RightsOf(_c.S.Me.Name);
            var body = BodyNode?.Piece.Rights ?? new BodyRights();
            return new WalkRights(body.Fly, own.Take, body.View, own.Dice, own.Measure, own.Ping, body.Walk);
        }
    }
    public int Colour { get; set; } = AvatarPose.NoColour;
    string? _spec, _bodyPiece;
    Vec2? _sentPos, _caughtPos;
    double _sentRot, _sentY, _caughtRot, _commitIn, _askIn;
    readonly Queue<(Vec2 Pos, double Rot)> _written = new();
    const int KeptWrites = 4;

    public event Action<string, Vector3, float>? BodyWanted;
    public event Action? BodyLeft;

    bool Tools => _c.S.IsUmpire;

    public bool InControl => Walking && _asked;

    public bool GodView { get; private set; }
    float _facing, _eyeYaw, _eyePitch;
    CameraRig.View? _godCamera;
    float Facing => _seatAt != null ? _seatYaw : GodView || Orbiting ? _facing : _c.Rig.Yaw;

    void Face(float yaw)
    {
        _facing = yaw;
        if (!GodView) _c.Rig.Yaw = yaw;
    }

    public void SeeAsGod(bool on)
    {
        if (on == GodView || on && !(InControl && Tools)) return;
        var rig = _c.Rig;
        if (!on)
        {
            _godCamera = rig.Now;
            GodView = false;
            rig.Walking = true;
            (rig.Yaw, rig.Pitch) = (_eyeYaw, _eyePitch);
            Resize(Height);
            _resync = true;
            _behind = Height * SwoopHeights;
            return;
        }
        if (!Orbiting) _facing = rig.Yaw;
        (_eyeYaw, _eyePitch) = (rig.Yaw, rig.Pitch);
        GodView = true;
        rig.Walking = false;
        rig.Cam.Near = _near;
        rig.LeaveEye();
        WorldShift.Set(Vector3.Zero);
        _shelter(0);
        if (_godCamera is { } left) rig.Show(left);
        else
        {
            var above = AvatarFromAbove();
            rig.Show(above with { Distance = Height * _far });
            rig.ZoomTo(above.Distance);
        }
        _c.Say(GodLine);
    }

    public void FindAvatar()
    {
        if (GodView) _c.Rig.Show(AvatarFromAbove());
    }

    CameraRig.View AvatarFromAbove()
    {
        var ahead = _shown + new Vector3(Mathf.Sin(_facing), 0, Mathf.Cos(_facing)) * (Height * GodAheadHeights);
        return new CameraRig.View(ahead, Height * GodDistanceHeights, _facing, CameraRig.LookingDownRadians, _c.Rig.TopDown);
    }

    public void TakeControl(string pieceId, Vector3 at)
    {
        _spec = AvatarBody.OfTaking(pieceId);
        if (Walking) AskBody();
        else Enter(at, asked: true);
    }

    public void LeaveControl()
    {
        if (!Walking || _c.S.IsPlayer) return;
        if (BodyNode is { Piece.Locked: false } own)
        {
            _commitIn = 0;
            Commit(own, 0);
        }
        Leave();
        _spec = null;
        _bodyPiece = null;
        BodyLeft?.Invoke();
    }
    public Func<PieceNode, bool> CarriedElsewhere { get; set; } = _ => false;

    public string? Body
    {
        get => _spec;
        set
        {
            _spec = value;
            if (Walking) AskBody();
        }
    }

    public string? BodyPiece => _bodyPiece;

    public void BodyIs(string? pieceId)
    {
        if (pieceId == _bodyPiece) return;
        _bodyPiece = pieceId;
        _sentPos = _caughtPos = null;
        _written.Clear();
        if (_solids != null) _solids.Ignore = pieceId;
        if (pieceId == null || _c.S.View.Find(Layers.Umpire, pieceId) is not { } p || (p.Pos - Coords.FromWorld(_at)).Length > Height) return;
        (_sentPos, _sentRot, _commitIn) = (p.Pos, p.RotDeg, 0);
        Wrote(p.Pos, p.RotDeg);
    }

    PieceNode? BodyNode => _bodyPiece != null && _pieces.NodeOf(Layers.Umpire, _bodyPiece) is { } n && GodotObject.IsInstanceValid(n) ? n : null;
    public event Action? FlyingChanged;

    public void Fly(bool on)
    {
        if (on == Flying || on && !Allowed.Fly) return;
        Flying = on;
        _vel.Y = on ? Height * TakeOffHeights : 0;
        Moves();
        if (on)
        {
            _flash = 1;
            Burst?.Invoke();
        }
        if (!on) _grounded = false;
        FlyingChanged?.Invoke();
    }

    void Moves()
    {
        if (_body != null) _body.MotionMode = Flying ? CharacterBody3D.MotionModeEnum.Floating : CharacterBody3D.MotionModeEnum.Grounded;
    }

    public bool ShowsSelf => Walking && (GodView || ThirdPerson && _behind > Height * SelfSeenFromHeights);
    public AvatarPose Pose => new(_shown, Facing, Height, null, _bodyPiece, Style, _act);
    public bool Grounded => _grounded;
    public float EyeLag => _eyeLag;
    public bool Riding => _ride != null;
    public bool Following => BodyNode is { } own && CarriedElsewhere(own) && !_inert;
    public Vector3 RideAt => _rideAt;
    public PieceNode? Ride => _ride;
    public int Solids => _solids?.Count ?? 0;
    public int RaisedSolids => _raised?.Count ?? 0;
    public Func<Vector3, float, IEnumerable<MeshInstance3D>>? Standing { get; set; }

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
            FloorSnapLength = AvatarPose.PersonMetres * StepShare,
            FloorStopOnSlope = true, FloorConstantSpeed = true, FloorBlockOnWall = true, SafeMargin = Margin, CollisionLayer = 0, CollisionMask = AvatarSolids.Layer, TopLevel = true,
        };
        _body.AddChild(new CollisionShape3D { Shape = _capsule });
        _c.Scene.AddChild(_body);
        Moves();
        _patch = new GroundPatch(_c.Scene, p => _c.S.Ground.At(p));
        _near = rig.Cam.Near;
        rig.Walking = true;
        Resize(Height);
        if (at is { } given) MoveTo(given);
        _third = false;
        ThirdPerson = Allowed.View;
        if (Steer == null) rig.Pitch = BoomPitch;
        _behind = Height * SwoopHeights;
        _boundsIn = 0;
        Show(0);
        _c.Say(Tools ? UmpireLine : Line);
        AskBody();
    }

    public void PlacedAt(Vector3 feet, float yaw, string? piece)
    {
        if (!_c.S.IsPlayer) return;
        _spec = piece != null ? AvatarBody.OfTaking(piece) : _spec ?? AvatarBody.OfPiece(FirmeEffects.FreshBody);
        (_c.Rig.Yaw, _facing) = (yaw, yaw);
        if (!Walking) Enter(feet);
        else
        {
            MoveTo(feet);
            AskBody();
        }
    }

    public void MoveTo(Vector3 at)
    {
        if (!Walking) return;
        _at = at;
        _solids?.Look(_at, now: true);
        _raised?.Look(_at, now: true);
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
        StopLooking();
        body.QueueFree();
        _solids?.Clear();
        _solids = null;
        _raised?.Clear();
        _raised = null;
        _patch?.Clear();
        _patch = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        var rig = _c.Rig;
        rig.Cam.Near = _near;
        rig.Walking = false;
        rig.LeaveEye();
        WorldShift.Set(Vector3.Zero);
        GodView = false;
        _godCamera = null;
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
            _raised?.Respace(_space);
            return;
        }
        _solids = new AvatarSolids(_c.Scene, _pieces, _space) { Ignore = _bodyPiece };
        _solids.Look(_at, now: true);
        if (Standing == null) return;
        _raised = new RaisedSolids(_c.Scene, Standing, _space);
        _raised.Look(_at, now: true);
    }

    void Reanchor()
    {
        if ((_at - _space.Anchor).Length() * _space.Scale <= ReanchorFrom) return;
        _space.Anchor = _at;
        _resync = true;
    }

    float Ground(Vector3 at) => (float)_c.S.Ground.At(Coords.FromWorld(at));

    public bool TakesFirst(InputEvent e) =>
        e is InputEventKey { Pressed: true, Echo: false, Keycode: Godot.Key.Tab } k && Tools && InControl && !_typing() && Key(k);

    public bool Takes(InputEvent e)
    {
        if (!Walking) return false;
        switch (e)
        {
            case InputEventMouseMotion mm when !GodView && Input.IsMouseButtonPressed(MouseButton.Right):
                if (!_looking && Steer == null)
                {
                    (_looking, _lookedFrom) = (true, mm.Position - mm.Relative);
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                }
                _c.Rig.Orbit(mm.Relative);
                return true;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Right } when _looking:
                StopLooking();
                return false;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } when !GodView:
                Zoom(closer: true);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } when !GodView:
                Zoom(closer: false);
                return true;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Middle } when !GodView:
                PingAimed();
                return true;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click when !GodView:
                if (click.Pressed && !click.DoubleClick) UseAimed(quiet: true);
                return true;
            case InputEventMouseMotion when !GodView && Input.IsMouseButtonPressed(MouseButton.Left):
                return true;
            case InputEventKey { Pressed: true, Echo: false } k:
                return Key(k);
            default:
                return false;
        }
    }

    void StopLooking()
    {
        if (!_looking) return;
        _looking = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Input.WarpMouse(_lookedFrom);
    }

    void Zoom(bool closer)
    {
        if (closer)
        {
            if (!ThirdPerson) return;
            if (_far / BehindNotch < NearestBehind) ThirdPerson = false;
            else _far /= BehindNotch;
            return;
        }
        if (!ThirdPerson)
        {
            if (Allowed.View) (ThirdPerson, _far) = (true, NearestBehind);
            else _c.Say(NoViewLine);
        }
        else if (_far * BehindNotch <= FarthestBehind) _far *= BehindNotch;
        else if (Tools && InControl) SeeAsGod(true);
    }

    bool Key(InputEventKey k)
    {
        if (k.Keycode == Godot.Key.Tab && Tools && InControl)
        {
            SeeAsGod(!GodView);
            return true;
        }
        if (GodView)
        {
            if (k.Keycode != Godot.Key.C) return false;
            FindAvatar();
            return true;
        }
        if (k.CtrlPressed && k.Keycode != Godot.Key.Ctrl) return false;
        switch (k.Keycode)
        {
            case Godot.Key.Space:
                _jumpAt = Time.GetTicksMsec();
                return true;
            case Godot.Key.F:
                if (Flying || Allowed.Fly) Fly(!Flying);
                else _c.Say(NoFlyLine);
                return true;
            case Godot.Key.V:
                if (ThirdPerson || Allowed.View) ThirdPerson = !ThirdPerson;
                else _c.Say(NoViewLine);
                return true;
            case Godot.Key.M:
                if (Measuring || Allowed.Measure) Measuring = !Measuring;
                else _c.Say(NoMeasureLine);
                return true;
            case Godot.Key.W or Godot.Key.A or Godot.Key.S or Godot.Key.D or Godot.Key.Q or Godot.Key.E or Godot.Key.Ctrl:
                return true;
            case Godot.Key.T:
                TakeAimed();
                return true;
            case Godot.Key.X:
                Stand(_stance == RigMotion.Sneak ? null : RigMotion.Sneak);
                return true;
            case Godot.Key.C:
                Stand(_stance == RigMotion.March ? null : RigMotion.March);
                return true;
            case Godot.Key.R:
                UseAimed();
                return true;
            case Godot.Key.Equal or Godot.Key.KpAdd when !Tools:
                Resize(Height * SizeNotch);
                KeepShare();
                return true;
            case Godot.Key.Minus or Godot.Key.KpSubtract when !Tools:
                Resize(Height / SizeNotch);
                KeepShare();
                return true;
            default:
                return false;
        }
    }

    public void SizeTo(float height)
    {
        Resize(height);
        KeepShare();
    }

    void KeepShare()
    {
        if (BodyNode is { Thickness: > 0 } own) (_sizedBy, _bodyShare) = (own.Piece.Id, Height / own.Thickness);
    }

    bool Idle => _typing() || GodView || Input.IsKeyPressed(Godot.Key.Ctrl) && !Flying;

    Vector2 Keys()
    {
        if (Idle) return Vector2.Zero;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Godot.Key.W)) move.Y += 1;
        if (Input.IsKeyPressed(Godot.Key.S)) move.Y -= 1;
        if (Input.IsKeyPressed(Godot.Key.E)) move.X += 1;
        if (Input.IsKeyPressed(Godot.Key.Q)) move.X -= 1;
        if (!Input.IsMouseButtonPressed(MouseButton.Right)) return move;
        if (Input.IsKeyPressed(Godot.Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Godot.Key.A)) move.X -= 1;
        return move;
    }

    float Turning() =>
        Idle || Input.IsMouseButtonPressed(MouseButton.Right) ? 0 : (Input.IsKeyPressed(Godot.Key.D) ? 1 : 0) - (Input.IsKeyPressed(Godot.Key.A) ? 1 : 0);

    (Vector3 From, Vector3 Along) Pointed()
    {
        var cam = _c.Rig.Cam;
        var view = cam.GetViewport();
        var screen = Steer != null ? view.GetVisibleRect().Size / 2 : Pointer.In(view);
        return (WorldShift.ToTable(cam.ProjectRayOrigin(screen)), cam.ProjectRayNormal(screen));
    }

    public void Step(double delta)
    {
        if (_body is not { } body || _capsule == null) return;
        float dt = (float)Math.Min(delta, 0.1), h = Height;
        var rig = _c.Rig;
        Reanchor();
        _solids?.Tick(delta, _at);
        _raised?.Tick(delta, _at);
        var own = BodyNode;
        if (own != null && CarriedElsewhere(own) && !RideHeld())
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
        _inert = Inert(dt);
        if (_inert)
        {
            Hold(body);
            Share(delta);
            return;
        }
        if (Seated(body, frozen ? Vector2.Zero : steer?.Move ?? Keys(), frozen ? 0 : steer?.Rise ?? (Idle || !Input.IsKeyPressed(Godot.Key.Space) ? 0 : 1)))
        {
            Share(delta);
            return;
        }
        if (steer == null && !frozen && Turning() is var turn && turn != 0) (rig.Yaw, _facing) = (rig.Yaw - turn * TurnRate * dt, _facing - turn * TurnRate * dt);
        var move = frozen ? Vector2.Zero : steer?.Move ?? Keys();
        if (!GodView) _facing = !Orbiting ? rig.Yaw : move == Vector2.Zero ? _facing : Mathf.LerpAngle(_facing, rig.Yaw, 1 - Mathf.Exp(-dt * FaceEase));
        bool run = steer?.Run ?? Input.IsKeyPressed(Godot.Key.Shift);
        float rise = frozen ? 0 : steer?.Rise ?? (Idle ? 0 : (Input.IsKeyPressed(Godot.Key.Space) ? 1 : 0) - (Input.IsKeyPressed(Godot.Key.Ctrl) ? 1 : 0));
        var forward = new Vector3(Mathf.Sin(rig.Yaw), 0, Mathf.Cos(rig.Yaw));
        var right = new Vector3(-forward.Z, 0, forward.X);
        var heading = (forward * move.Y + right * move.X).LimitLength();
        _hill = Mathf.Lerp(_hill, _grounded && !Flying ? Hill(body, heading) : 1, 1 - Mathf.Exp(-dt * HillEase));
        var want = heading * h * Pace(run) * _hill;
        var flat = new Vector3(_vel.X, 0, _vel.Z).Lerp(want, 1 - Mathf.Exp(-dt * Grip));
        bool leaps = !frozen && !Flying && _steep == null && Time.GetTicksMsec() - _jumpAt < JumpHeldMs && (_grounded || _airborne < GraceSeconds);
        if (leaps) _jumpAt = 0;
        _vel = Flying ? Flown(move, rise, run, dt, h) : flat with { Y = leaps ? JumpHeights * h : _grounded ? 0 : _vel.Y - FallHeights * h * dt };
        if (_steep is { } steep && !Flying && !leaps) _vel = _vel.Lerp(Downhill(steep) * (SlideHeights * h), 1 - Mathf.Exp(-dt * SlideGrip));
        if (Flying) _grounded = false;
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
        _steep = _grounded || Flying ? null : Steep(body);
        _sink = _grounded && !Flying && body.IsOnFloor() ? Mathf.Min(h * GirthShare * (1 / Mathf.Max(body.GetFloorNormal().Y, 0.5f) - 1), h * SinkMost) : 0;
        _airborne = _grounded ? 0 : leaps ? GraceSeconds : _airborne + dt;
        KeepOnTable(dt);
        KeepInBounds(body);
        _bodyNow = body.GlobalPosition;
        _synced = _at;
        Mount();
        Adapt(dt, h);
        Share(delta);
        if (own != null && !frozen) Commit(own, delta);
    }

    Vector3 Flown(Vector2 move, float rise, bool boost, float dt, float h)
    {
        var rig = _c.Rig;
        var look = new Vector3(Mathf.Sin(rig.Yaw) * Mathf.Cos(rig.Pitch), Mathf.Sin(rig.Pitch), Mathf.Cos(rig.Yaw) * Mathf.Cos(rig.Pitch));
        var side = new Vector3(-Mathf.Cos(rig.Yaw), 0, Mathf.Sin(rig.Yaw));
        var aim = (look * move.Y + side * move.X + Vector3.Up * rise).LimitLength();
        _burstIn -= dt;
        if (boost && !_boosting && aim != Vector3.Zero && _burstIn <= 0)
        {
            _vel += aim.Normalized() * (h * BurstHeights);
            (_burstIn, _flash) = (BurstEvery, 1);
            Burst?.Invoke();
        }
        _boosting = boost;
        var want = aim * (h * (boost ? BoostHeights : CruiseHeights));
        return _vel.Lerp(want, 1 - Mathf.Exp(-dt * (aim == Vector3.Zero ? AirDrag : AirGrip)));
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
        if (body.TestMove(raised, move, null, Margin)) return;
        float feet = from.Origin.Y - AvatarPose.PersonMetres / 2;
        var over = raised.Origin + move + move.Normalized() * (AvatarPose.PersonMetres * GirthShare * StepAheadShare);
        var probe = PhysicsRayQueryParameters3D.Create(over, over + Vector3.Down * (AvatarPose.PersonMetres / 2 + room + Margin * 2), AvatarSolids.Layer);
        var found = body.GetWorld3D().DirectSpaceState.IntersectRay(probe);
        if (found.Count == 0 || !found.TryGetValue("position", out var at) || !found.TryGetValue("normal", out var up)) return;
        float gained = at.AsVector3().Y - feet;
        if (gained <= Margin * EdgeShare || gained > lift || up.AsVector3().AngleTo(Vector3.Up) > body.FloorMaxAngle) return;
        body.GlobalPosition += Vector3.Up * Mathf.Min(gained + Margin * 2, room);
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
        if (!_grounded && _vel.Y <= 0 && _airborne < GraceSeconds && Underfoot(body, h) is { } drop)
        {
            body.GlobalPosition += drop;
            _at = _space.Out(body.GlobalPosition) - Vector3.Up * (AvatarPose.PersonMetres / 2 / _space.Scale);
            _grounded = true;
        }
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
        Face(own.GlobalRotation.Y - Mathf.Pi);
        _sentPos = _caughtPos = null;
        _written.Clear();
    }

    void Wrote(Vec2 pos, double rot)
    {
        _written.Enqueue((pos, rot));
        while (_written.Count > KeptWrites) _written.Dequeue();
    }

    void Catch(PieceNode own)
    {
        var p = own.Piece;
        if (_written.Any(w => Same(p.Pos, w.Pos, p.RotDeg, w.Rot))) return;
        if (_caughtPos is { } caught && Same(p.Pos, caught, p.RotDeg, _caughtRot)) return;
        (_caughtPos, _caughtRot, _sentPos, _sentRot) = (p.Pos, p.RotDeg, p.Pos, p.RotDeg);
        Wrote(p.Pos, p.RotDeg);
        var at = Coords.ToWorld(p.Pos);
        MoveTo(at with { Y = p.Y > 0 ? (float)p.Y : Ground(at) });
        Face(AvatarBody.YawOf(p.RotDeg));
    }

    void Commit(PieceNode own, double delta)
    {
        if ((_commitIn -= delta) > 0) return;
        _commitIn = CommitEvery;
        var pos = Coords.FromWorld(_at);
        double rot = AvatarBody.RotOf(Facing);
        float lift = _at.Y - Ground(_at);
        double y = lift > Height * LiftedShare ? _at.Y : 0;
        if (_sentPos is { } sent && Same(pos, sent, rot, _sentRot) && Math.Abs(y - _sentY) < Height * LiftedShare) return;
        (_sentPos, _sentRot, _sentY) = (pos, rot, y);
        Wrote(pos, rot);
        _c.S.Do(new KTable.Core.Protocol.MovePiece { Layer = Layers.Umpire, Id = own.Piece.Id, Pos = pos, RotDeg = rot, Y = y });
    }

    bool Same(Vec2 a, Vec2 b, double rotA, double rotB) =>
        Math.Abs(a.X - b.X) + Math.Abs(a.Z - b.Z) < Height * SameSpotShare && Math.Abs(Mathf.PosMod((float)(rotA - rotB) + 180, 360) - 180) < SameTurnDegrees;

    PieceNode? Aimed()
    {
        var (from, along) = Pointed();
        var eye = _at + Vector3.Up * (Height * EyeShare);
        float reach = Height * TakeReachHeights;
        return Ray(from, from + along * (from.DistanceTo(eye) + reach), out var hit) is { } at && hit != null && at.DistanceTo(eye) <= reach ? _solids?.Owner(hit) : null;
    }

    public void UseAimed(bool quiet = false)
    {
        if (Aimed() is not { IsPart: true } node || node.GetParent() is not PieceNode { Def: { } def } building
            || building.Parts.Where(p => p.Def?.Tags.Contains(Doors.Tag) == true).ToList().IndexOf(node) is var door && door < 0
            || Doors.Toggled(def, building.Piece.State, door, _c.S.Def) is not { } to)
        {
            if (!quiet) _c.Say("No door to open there");
            return;
        }
        _c.S.Do(new KTable.Core.Protocol.SetState { Layer = building.Layer, Id = building.Piece.Id, State = to });
    }

    public void PingAimed()
    {
        if (!Allowed.Ping)
        {
            _c.Say(NoPingLine);
            return;
        }
        ulong now = Time.GetTicksMsec();
        if (now - _pingedAt < PingEveryMs || AimPoint() is not { } at) return;
        _pingedAt = now;
        Pinged?.Invoke(at);
    }

    public List<double>? ThrowPath(float turn = 0)
    {
        if (!Walking) return null;
        var rig = _c.Rig;
        float h = Height, fall = FallHeights * h, spent = 0;
        var forward = new Vector3(Mathf.Sin(rig.Yaw + turn), 0, Mathf.Cos(rig.Yaw + turn));
        var side = new Vector3(-forward.Z, 0, forward.X);
        var p = _at + Vector3.Up * (h * HandShare) + forward * (h * HandAhead) + side * (h * HandAside);
        float pitch = Mathf.Clamp(rig.Pitch + ThrowLift, -0.6f, 1.1f);
        var vel = (forward * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch)) * (h * ThrowHeights);
        var path = new List<double> { h, p.X, p.Y, p.Z, 0 };
        for (int bounce = 0, step = 0; bounce < Bounces && step < ThrowSteps; step++)
        {
            var next = p + vel * ThrowStep + Vector3.Down * (0.5f * fall * ThrowStep * ThrowStep);
            vel += Vector3.Down * (fall * ThrowStep);
            spent += ThrowStep;
            var hit = Struck(p, next);
            if (hit == null && next.Y < Ground(next)) hit = (next with { Y = Ground(next) }, Vector3.Up);
            if (hit is not var (at, normal))
            {
                p = next;
                continue;
            }
            p = at + normal * (h * 0.01f);
            path.AddRange([at.X, at.Y, at.Z, spent]);
            bounce++;
            vel = (vel - normal * (2 * vel.Dot(normal))) * BounceKeep;
            if (vel.Length() < h * StillHeights) break;
        }
        return path.Count > 5 ? path : null;
    }

    (Vector3 At, Vector3 Normal)? Struck(Vector3 from, Vector3 to)
    {
        var hit = _c.Scene.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(_space.In(from), _space.In(to), AvatarSolids.Layer));
        if (!hit.TryGetValue("position", out var at) || !hit.TryGetValue("normal", out var normal)) return null;
        return (_space.Out(at.AsVector3()), normal.AsVector3().Normalized());
    }

    public Vector3? AimPoint()
    {
        if (!Walking) return null;
        var (from, along) = Pointed();
        var view = _c.Rig.Cam.GetViewport();
        return Ray(from, from + along * (Height * AimHeights)) ?? _c.Rig.GroundAt(Steer != null ? view.GetVisibleRect().Size / 2 : Pointer.In(view));
    }

    public void TakeAimed()
    {
        if (!Allowed.Take)
        {
            _c.Say(NoTakeLine);
            return;
        }
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

    static PieceNode WholeOf(PieceNode node)
    {
        while (node.IsPart && node.GetParent() is PieceNode up) node = up;
        return node;
    }

    bool RideHeld() => _ride != null && _rideKey is { } key && _pieces.NodeOf(key.Layer, key.Id) is { } root && CarriedElsewhere(root);

    bool Inert(float dt)
    {
        _sinceHeld = RideHeld() || _carried && _sinceHeld < RideSettleSeconds ? 0 : _sinceHeld + dt;
        return _ride != null && _sinceHeld < RideSettleSeconds;
    }

    bool Seated(CharacterBody3D body, Vector2 move, float rise)
    {
        if (_wantSeat)
        {
            _wantSeat = false;
            if (!Flying && FindSeat() is var (feet, yaw))
            {
                (_seatAt, _seatFrom, _seatYaw) = (feet, _at, yaw);
                if (!GodView) _c.Rig.Yaw = yaw;
            }
        }
        if (_seatAt is not { } seat) return false;
        if (move != Vector2.Zero || rise > 0 || _carried || Flying || RigMotion.Parts(_act).Whole != RigMotion.Sit)
        {
            StandUp();
            return false;
        }
        _at = seat;
        _facing = _seatYaw;
        if (Rides(out var now)) _rideAt = now.AffineInverse() * _at;
        Hold(body);
        return true;
    }

    void StandUp()
    {
        if (_seatAt == null) return;
        _at = _seatFrom;
        _seatAt = null;
        _resync = true;
        var (whole, arm) = RigMotion.Parts(_act);
        if (whole == RigMotion.Sit) _act = RigMotion.Combined(null, arm);
    }

    (Vector3 Feet, float Yaw)? FindSeat()
    {
        float h = Height, floor = _at.Y;
        var found = new List<(Vector3 Top, float Score)>();
        for (float dx = -SeatReach; dx <= SeatReach; dx += SeatStep)
            for (float dz = -SeatReach; dz <= SeatReach; dz += SeatStep)
            {
                float far = Mathf.Sqrt(dx * dx + dz * dz);
                if (far > SeatReach) continue;
                var above = new Vector3(_at.X + dx * h, floor + (SeatHigh + SeatStep) * h, _at.Z + dz * h);
                if (Ray(above, above + Vector3.Down * ((SeatHigh - SeatLow + 2 * SeatStep) * h)) is not { } top) continue;
                float rise = (top.Y - floor) / h;
                if (rise < SeatLow || rise > SeatHigh || Ray(top + Vector3.Up * (SeatLift * h), top + Vector3.Up * (SeatClear * h)) != null) continue;
                found.Add((top, far + (rise > TableRise ? TableRise : 0)));
            }
        foreach (var (top, _) in found.OrderBy(f => f.Score))
        {
            if (Centred(top, h) is not { } seat) continue;
            return (new Vector3(seat.X, Mathf.Max(floor, seat.Y - SeatHips * h), seat.Z), SeatYaw(seat, h, floor));
        }
        return null;
    }

    Vector3? Centred(Vector3 top, float h)
    {
        var seat = top;
        foreach (var axis in new[] { Vector3.Right, Vector3.Back })
        {
            float Edge(Vector3 dir)
            {
                for (float d = SeatEdgeStep; d <= SeatEdgeMost; d += SeatEdgeStep)
                {
                    var over = top + dir * (d * h) + Vector3.Up * (SeatEdgeStep * h);
                    if (Ray(over, over + Vector3.Down * (2 * SeatEdgeStep * h)) is not { } hit || Mathf.Abs(hit.Y - top.Y) > SeatLift * h) return d - SeatEdgeStep;
                }
                return SeatEdgeMost;
            }
            float plus = Edge(axis), minus = Edge(-axis);
            if (plus + minus < SeatSpan) return null;
            seat += axis * (Mathf.Clamp((plus - minus) / 2, -SeatShiftMost, SeatShiftMost) * h);
        }
        return seat;
    }

    float SeatYaw(Vector3 seat, float h, float floor)
    {
        int hits = 0, nearest = -1;
        float closest = float.MaxValue;
        var from = seat + Vector3.Up * (BackAbove * h);
        for (int k = 0; k < SeatProbes; k++)
        {
            if (Ray(from, from + Around(k) * (BackReach * h)) is not { } hit) continue;
            hits++;
            if (from.DistanceTo(hit) < closest) (closest, nearest) = (from.DistanceTo(hit), k);
        }
        if (hits > 0 && hits < OpenSides) return Mathf.Atan2(-Around(nearest).X, -Around(nearest).Z);
        for (float d = TableStep; d <= TableReach; d += TableStep)
            for (int k = 0; k < SeatProbes; k++)
            {
                var over = new Vector3(seat.X, floor + TableHigh * h, seat.Z) + Around(k) * (d * h);
                if (Ray(over, over + Vector3.Down * ((TableHigh - TableLow) * h)) != null) return Mathf.Atan2(Around(k).X, Around(k).Z);
            }
        var away = _at - seat;
        return new Vector2(away.X, away.Z).LengthSquared() > SeatLift * h * h ? Mathf.Atan2(away.X, away.Z) : _facing;
    }

    static Vector3 Around(int k) => new(Mathf.Sin(k * Mathf.Tau / SeatProbes), 0, Mathf.Cos(k * Mathf.Tau / SeatProbes));

    void Hold(CharacterBody3D body)
    {
        if (Rides(out var frame)) _rideWas = frame;
        _rideBefore = _rideAt;
        _before = _at;
        _vel = Vector3.Zero;
        _grounded = true;
        body.GlobalPosition = Centre(_at);
        _bodyWas = _bodyNow = body.GlobalPosition;
        _synced = _at;
    }

    void Turn(Transform3D now)
    {
        float yaw = YawOf(now);
        float by = Mathf.AngleDifference(_yawSeen, yaw);
        _facing += by;
        if (!GodView) _c.Rig.Yaw += by;
        _yawSeen = yaw;
    }

    void Mount()
    {
        var was = _ride;
        if (_grounded)
        {
            _ride = _under is { } on ? WholeOf(on) : null;
            _rideKey = _ride is { } whole ? (whole.Layer, whole.Piece.Id) : null;
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

    float Hill(CharacterBody3D body, Vector3 heading)
    {
        if (heading.LengthSquared() < 1e-6f || !body.IsOnFloor()) return 1;
        var n = body.GetFloorNormal();
        var along = heading.Normalized();
        float grade = -(n.X * along.X + n.Z * along.Z) / Mathf.Max(n.Y, 0.1f);
        return HillFactor(grade);
    }

    public static float HillFactor(float grade) => Mathf.Exp(-HillPull * Mathf.Max(0, grade - HillFree));

    static Vector3? Steep(CharacterBody3D body)
    {
        float wall = Mathf.Cos(Mathf.DegToRad(SteepDegrees)), floor = Mathf.Cos(Mathf.DegToRad(FloorDegrees));
        for (int i = 0; i < body.GetSlideCollisionCount(); i++)
            if (body.GetSlideCollision(i).GetNormal() is var n && n.Y >= wall && n.Y < floor) return n;
        return null;
    }

    static Vector3 Downhill(Vector3 normal) => (Vector3.Down - normal * normal.Dot(Vector3.Down)).Normalized();

    void KeepInBounds(CharacterBody3D body)
    {
        if (Fence == null || !_c.S.IsPlayer || Fence.For(_c.S.Me.Name) is not { Empty: false } space) return;
        var kept = Fence.Slide(space, _before, _at);
        if (kept.IsEqualApprox(_at)) return;
        var push = _at - kept;
        _at = kept;
        body.GlobalPosition = Centre(_at);
        if (push.Y < 0 && _vel.Y < 0) (_vel.Y, _grounded) = (0, !Flying);
        if (push.Y > 0 && _vel.Y > 0) _vel.Y = 0;
        var across = push with { Y = 0 };
        if (across.LengthSquared() > 1e-12f && _vel.Dot(across.Normalized()) is var into and > 0) _vel -= across.Normalized() * into;
        Fenced?.Invoke(kept, across.LengthSquared() > 1e-12f ? across.Normalized() : push.Normalized());
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

    float Swept(Vector3 from, Vector3 to, float radius)
    {
        if (from.IsEqualApprox(to)) return 1;
        _sweep.Radius = radius * _space.Scale;
        var query = new PhysicsShapeQueryParameters3D
        {
            Shape = _sweep, Transform = new Transform3D(Basis.Identity, _space.In(from)), Motion = _space.In(to) - _space.In(from), CollisionMask = AvatarSolids.Layer,
        };
        var fractions = _c.Scene.GetWorld3D().DirectSpaceState.CastMotion(query);
        return fractions.Length > 0 ? fractions[0] : 1;
    }

    Vector3? Ray(Vector3 from, Vector3 to) => Ray(from, to, out _);

    Vector3? Underfoot(CharacterBody3D body, float h)
    {
        var hit = new KinematicCollision3D();
        if (!body.TestMove(body.GlobalTransform, Vector3.Down * (AvatarPose.PersonMetres * StepShare), hit, Margin)) return null;
        float floor = Mathf.Cos(Mathf.DegToRad(FloorDegrees));
        if (hit.GetNormal().Y >= floor) return hit.GetTravel();
        var space = _c.Scene.GetWorld3D().DirectSpaceState;
        var probe = PhysicsRayQueryParameters3D.Create(_space.In(_at + Vector3.Up * (h * StepShare)), _space.In(_at + Vector3.Down * (h * StepShare)), AvatarSolids.Layer);
        var under = space.IntersectRay(probe);
        return under.Count > 0 && under.TryGetValue("normal", out var up) && up.AsVector3().Y >= floor ? hit.GetTravel() : null;
    }

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
        if (GodView)
        {
            _roofed = 0;
            return;
        }
        var eye = ThirdPerson ? WorldShift.ToTable(_c.Rig.Cam.GlobalPosition) : _at + Vector3.Up * (h * EyeShare);
        bool under = Ray(eye, eye + Vector3.Up * (h * RoofHeights)) != null;
        _roofed = Mathf.MoveToward(_roofed, under ? 1 : 0, dt * EyesAdapt);
        _shelter(_roofed);
    }

    void Share(double delta)
    {
        _shareIn += delta;
        float yaw = Facing;
        var pose = Rides(out var now)
            ? new AvatarPose(_rideAt, yaw - YawOf(now), Height, _ride!.Piece.Id, _bodyPiece, Style, _act)
            : new AvatarPose(_at - Vector3.Up * _sink, yaw, Height, null, _bodyPiece, Style, _act);
        bool moved = pose.Ride != _sharedPose.Ride || pose.Body != _sharedPose.Body || pose.Gait != _sharedPose.Gait || pose.Act != _sharedPose.Act
            || !pose.At.IsEqualApprox(_sharedPose.At) || !Mathf.IsEqualApprox(pose.Yaw, _sharedPose.Yaw)
            || !Mathf.IsEqualApprox(pose.Height, _sharedPose.Height);
        if (_shareIn < (moved ? ShareMovingEvery : ShareStillEvery)) return;
        _shareIn = 0;
        _sharedPose = pose;
        Moved?.Invoke(pose);
    }

    public void Tick(double delta)
    {
        if (!Walking) return;
        var allowed = Allowed;
        if (Flying && !allowed.Fly) Fly(false);
        if (ThirdPerson && !allowed.View) ThirdPerson = false;
        if (Measuring && !allowed.Measure) Measuring = false;
        if (!allowed.Walk) (_walk, _stance, _act) = (null, null, null);
        _kick = Mathf.Lerp(_kick, Boosting ? BoostLens : 0, 1 - Mathf.Exp(-(float)delta * LensEase));
        _flash = Mathf.Max(0, _flash - (float)delta * FlashFade);
        _seenHeight = _seenHeight > 0 ? Mathf.Lerp(_seenHeight, Height, 1 - Mathf.Exp(-(float)delta * GrowEase)) : Height;
        if (!GodView) Rebase();
        Show((float)delta);
        Immerse(Immersed);
        AskAgain(delta);
    }

    void AskAgain(double delta)
    {
        if (BodyNode != null || (_askIn -= delta) > 0) return;
        if (AvatarBody.Taking(_spec) != null && _asked)
        {
            LeaveControl();
            return;
        }
        if (AvatarBody.Taking(_spec) != null) _spec = null;
        AskBody();
    }

    void AskBody()
    {
        _askIn = AskAgainEvery;
        if (Body is { } wanted) BodyWanted?.Invoke(wanted, _at, Facing);
    }

    void Immerse(bool on)
    {
        if (on == _immersed) return;
        _immersed = on;
        if (Tools && Walking) _c.Say(on ? UmpireLine : GodLine);
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
        bool follows = own != null && CarriedElsewhere(own) && !_inert;
        var exact = follows ? own!.GlobalPosition
            : rides ? WorldShift.ToGlobal(carried * _rideBefore.Lerp(_rideAt, between))
            : WorldShift.ToGlobal(_space.Anchor) + (_bodyWas.Lerp(_bodyNow, between) - Vector3.Up * (AvatarPose.PersonMetres / 2)) / _space.Scale;
        _sinkShown = Mathf.Lerp(_sinkShown, rides || follows ? 0 : _sink, 1 - Mathf.Exp(-delta * SinkEase));
        exact -= Vector3.Up * _sinkShown;
        var feet = WorldShift.ToTable(exact);
        _shown = feet;
        Ease(exact.Y, delta, rides || follows || _inert);
        if (GodView) return;
        rig.Target = feet;
        rig.Distance = Mathf.Clamp(Height * ViewHeights, rig.MinDistance, rig.MaxDistance);
        var up = Vector3.Up * ((_seenHeight > 0 ? _seenHeight : Height) * (EyeShare - (_seatAt != null ? SeatEyeDrop : 0)) + _eyeLag + Bob(feet, delta));
        rig.LookFrom(ThirdPerson ? Behind(feet + up, delta) : feet + up, Lens + _kick, exact: ThirdPerson ? null : exact + up);
    }

    float Bob(Vector3 feet, float delta)
    {
        float moved = new Vector2(feet.X - _bobWas.X, feet.Z - _bobWas.Z).Length();
        _bobWas = feet;
        float pace = delta > 0 ? moved / delta / Height : 0;
        bool bobs = _c.Settings.HeadBob && !ThirdPerson && !Flying && _grounded && pace > BobFrom && moved < Height;
        _bobOn = Mathf.MoveToward(_bobOn, bobs ? 1 : 0, delta * BobEase);
        if (_bobOn <= 0) return 0;
        float running = Mathf.Clamp((pace - WalkHeights) / (RunHeights - WalkHeights), 0, 1);
        _bobPhase = Mathf.PosMod(_bobPhase + moved / (Height * Mathf.Lerp(WalkStepHeights, RunStepHeights, running)), 1);
        return -Height * BobHeights * _c.Settings.HeadBobStrength * _bobOn * (1 - Mathf.Cos(Mathf.Tau * _bobPhase)) / 2;
    }

    Vector3 Behind(Vector3 head, float delta)
    {
        var rig = _c.Rig;
        var look = new Vector3(Mathf.Sin(rig.Yaw) * Mathf.Cos(rig.Pitch), Mathf.Sin(rig.Pitch), Mathf.Cos(rig.Yaw) * Mathf.Cos(rig.Pitch));
        float room = Height * CameraRoomShare, far = Height * _far;
        var middle = _shown + Vector3.Up * (Height / 2);
        var pivot = middle.Lerp(head + Vector3.Up * (Height * OverHeadShare), Swept(middle, head + Vector3.Up * (Height * OverHeadShare), room));
        float free = far * Swept(pivot, pivot - look * far, room);
        _behind = free < far && free < _behind ? free : Mathf.Lerp(_behind, free, 1 - Mathf.Exp(-delta * BoomEase));
        var at = pivot - look * _behind;
        float ground = Ground(at) + room;
        return at.Y < ground ? at with { Y = ground } : at;
    }
}
