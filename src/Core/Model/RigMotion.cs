using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public readonly record struct RigMove(float Walk, float WalkPhase, float Run, float RunPhase, float Idle, float IdlePhase, float Fly = 0, float Glide = 0, float Pulse = 0,
    float Ahead = 1, float Aside = 0, float Speed = 0, float Air = 0, float Rise = 0, float Squat = 0, float Seconds = 0, float Grade = 0, float Tilt = 0,
    float Base = 0, string? Act = null, float ActSeconds = 0, int Hold = 0)
{
    public static readonly RigMove Still = new(0, 0, 0, 0, 1, 0);
}

public sealed partial class RigMotion
{
    const float ArmForward = 4f;
    const float Tau = MathF.PI * 2;

    sealed record Stride(float Thigh, float ThighBias, float KneeRest, float KneeSwing, float Foot, float Bob, float Twist, float Lean, float Arm, float ElbowRest, float ElbowSwing,
        float Roll, float Crouch, float ArmLag, float Wrist);

    sealed record Gait(string Id, string Name, float ArmOut, Stride Walk, Stride Run, bool Stance = false);

    public const string Natural = "natural", March = "march", Sneak = "sneak";

    const float FootGap = 0.08f, ReachMost = 0.45f, LegShare = 0.47f, HipShare = 0.1f, ShortestLeg = 0.2f, SideKneeDrop = 0.4f, KneeKept = 0.6f;
    const float WalkGive = 14f, RunGive = 24f, WalkDuty = 0.6f, RunDuty = 0.38f, CrouchKnee = 250f;
    const float LandKnee = 34f, AirLean = 6f, AirCurl = 8f, AirLegOut = 4f, AirToes = 18f, AirElbow = 30f;
    const float RiseLead = 42f, RiseLeadKnee = 78f, RiseTrail = 18f, RiseTrailKnee = 55f, FallLead = 16f, FallLeadKnee = 24f, FallTrail = 4f, FallTrailKnee = 18f;
    const float RiseArm = 40f, RiseArmOut = 12f, FallArm = 8f, FallArmOut = 28f, SpineUnturn = 0.65f, ChestUnturn = 0.25f;
    float _squat, _seconds, _grade, _tilt, _base;

    static readonly Gait[] Sets =
    [
        new(Natural, "Natural", 9, new(26, 2, 5, 58, 14, 0.012f, 6, 3, 22, 16, 18, 3.5f, 0, 0.35f, 8), new(42, 10, 16, 100, 20, 0.026f, 10, 11, 46, 80, 24, 5, 0, 0.25f, 12)),
        new(March, "Marching", 5, new(30, 0, 2, 50, 10, 0.008f, 3, 0, 32, 4, 6, 1.5f, 0, 0.1f, 0), new(40, 8, 14, 95, 18, 0.022f, 6, 6, 40, 85, 18, 3, 0, 0.2f, 6), Stance: true),
        new("heavy", "Heavy", 24, new(22, 4, 10, 48, 10, 0.02f, 9, 6, 16, 28, 10, 7, 0.015f, 0.45f, 10), new(36, 10, 20, 90, 16, 0.034f, 12, 14, 34, 70, 20, 8, 0.02f, 0.3f, 12)),
        new("graceful", "Graceful", 14, new(22, 0, 4, 52, 16, 0.008f, 9, 1, 12, 14, 12, 6, 0, 0.4f, 14), new(38, 8, 14, 96, 22, 0.02f, 11, 8, 34, 78, 20, 6, 0, 0.3f, 14)),
        new("stooped", "Stooped", 7, new(16, 6, 12, 36, 8, 0.006f, 3, 16, 8, 22, 8, 2.5f, 0.02f, 0.4f, 6), new(26, 10, 18, 70, 12, 0.014f, 5, 20, 22, 60, 16, 3, 0.02f, 0.3f, 8)),
        new(Sneak, "Sneaking", 12, new(24, 14, 26, 50, 18, 0.004f, 4, 14, 6, 60, 6, 2, 0.07f, 0.3f, 10), new(34, 16, 30, 85, 20, 0.012f, 6, 18, 16, 80, 12, 3, 0.07f, 0.25f, 10), Stance: true),
    ];

    public static IReadOnlyList<(string Id, string Name)> Gaits { get; } = Sets.Where(g => !g.Stance).Select(g => (g.Id, g.Name)).ToList();

    public static bool Known(string? gait) => gait == null || Sets.Any(g => g.Id == gait);

    readonly Gait _gait;
    const float FlyLean = 62f, FlySway = 3f, FlyCurl = 14f, FlyLegTrail = 6f, FlyKick = 5f, FlyTuck = 38f, FlyKnee = 10f, FlyToes = 28f, FlyArmBack = 30f, FlyArmOut = 16f,
        FlyElbow = 14f, FlyBob = 0.012f;
    const float IdleElbow = 9f, IdleKnee = 2.5f, IdleBreath = 1.6f, IdleShoulder = 1.2f, IdleHead = 5f;

    readonly Rig _rig;
    readonly Vector3[] _restPoints;
    readonly Quaternion[] _restTurns;
    readonly Vector3[] _restLocalPoints;
    readonly Quaternion[] _restLocalTurns;
    readonly Vector3 _up, _across, _ahead, _right, _origin;
    readonly float _height, _tall, _legShare, _hipShare, _pace;
    readonly int _hips;
    readonly Leg[] _legs;
    readonly Dictionary<int, Quaternion> _curls;
    readonly Dictionary<string, Quaternion> _relax = new();
    readonly RigClip? _walkClip, _idleClip;
    readonly Paces _walking, _running;
    readonly Vector3[] _points, _blendPoints;
    readonly Quaternion[] _turns, _blendTurns, _moves;

    public bool Walks { get; }
    public bool Moves => Walks || _walkClip != null;
    public int Bones => _rig.Bones.Count;
    public Vector3[] Points => _blendPoints;
    public Quaternion[] Turns => _blendTurns;

    public RigMotion(Rig rig, string? gait = null)
    {
        _rig = rig;
        _gait = Sets.FirstOrDefault(g => g.Id == gait) ?? Sets[0];
        bool own = gait == null;
        (_restPoints, _restTurns, _) = rig.Rest();
        (_restLocalPoints, _restLocalTurns) = rig.RestLocal();
        int n = rig.Bones.Count;
        (_points, _blendPoints, _turns, _blendTurns, _moves) = (new Vector3[n], new Vector3[n], new Quaternion[n], new Quaternion[n], new Quaternion[n]);
        _walkClip = own ? rig.Clip("walk") : null;
        _idleClip = own ? rig.Clip("idle") : null;
        Walks = H.Walks(rig.Humanoid);
        _up = Vector3.Normalize(rig.Root.Back(Vector3.UnitY));
        var forward = Forward();
        _across = Vector3.Normalize(Vector3.Cross(forward, _up));
        _ahead = forward;
        _height = Height();
        _tall = _height / Math.Max(_rig.Root.Scale, 1e-6f);
        (_right, _legShare, _hipShare) = Sides();
        _pace = MathF.Sqrt(LegShare / MathF.Max(_legShare, ShortestLeg));
        _hips = Bone(H.Hips) ?? -1;
        _legs = Legs();
        _origin = _legs.Length == 2 ? (_restPoints[_legs[0].Ankle] + _restPoints[_legs[1].Ankle]) / 2 : Vector3.Zero;
        _curls = Curls();
        foreach (var side in SideNames)
            if (Bone(Of(side, H.UpperArm)) is { } upper && Bone(Of(side, H.LowerArm)) is { } lower)
            {
                var along = _restPoints[lower] - _restPoints[upper];
                if (along.LengthSquared() < 1e-12f) continue;
                along = Vector3.Normalize(along);
                var outward = along - Vector3.Dot(along, _up) * _up;
                outward = outward.LengthSquared() > 1e-8f ? Vector3.Normalize(outward) : Vector3.Zero;
                var hang = Vector3.Normalize(-_up * MathF.Cos(Rad(_gait.ArmOut)) + outward * MathF.Sin(Rad(_gait.ArmOut)) + forward * MathF.Sin(Rad(ArmForward)));
                _relax[Of(side, H.UpperArm)] = FromTo(along, hang);
            }
        (_walking, _running) = Resolve(own);
    }

    public void Pose(RigMove m)
    {
        Array.Clear(_blendPoints);
        Array.Clear(_blendTurns);
        float total = 0;
        (_squat, _seconds, _grade, _tilt, _base) = (m.Squat, m.Seconds, m.Grade, m.Tilt, m.Base * _tall);
        var (whole, arm) = Parts(m.Act);
        _hold = m.Hold;
        _whole = Walks ? Rows.FirstOrDefault(a => a.Whole && a.Id == whole) : null;
        _arm = Walks ? Rows.FirstOrDefault(a => !a.Whole && a.Id == arm) : null;
        (_wholePhase, _armPhase) = (m.ActSeconds * (_whole?.Rate ?? 0), m.ActSeconds * (_arm?.Rate ?? 0));
        float stretch = StretchAt(m.Speed, m.Walk + m.Run > 1e-4f ? m.Run / (m.Walk + m.Run) : 0);
        if (m.Act != null && ActClip(m.Act) is { Length: > 0 } own)
        {
            (_whole, _arm) = (null, null);
            Add(1, own, m.ActSeconds / own.Length % 1, null);
        }
        else if (_whole != null) Add(1, null, 0, null);
        else
        {
            Add(m.Idle, _idleClip, _idleClip is { Length: > 0 } idle ? Wrap(m.Seconds / idle.Length) : m.IdlePhase, null);
            Stepped(m.Walk, _walking, _gait.Walk, m.WalkPhase);
            Stepped(m.Run, _running, _gait.Run, m.RunPhase);
            if (Walks) Add(m.Fly, null, 0, null, (m.Glide, m.Pulse));
            if (Walks) Add(m.Air, null, 0, null, air: m.Rise);
        }
        if (total <= 0)
        {
            Procedural(0, null, 0);
            Array.Copy(_points, _blendPoints, _points.Length);
            Array.Copy(_turns, _blendTurns, _turns.Length);
            return;
        }
        for (int i = 0; i < _blendTurns.Length; i++)
        {
            _blendPoints[i] /= total;
            _blendTurns[i] = Quaternion.Normalize(_blendTurns[i]);
        }

        void Stepped(float weight, Paces paces, Stride stride, float phase)
        {
            if (weight <= 1e-4f) return;
            if (paces.Built)
            {
                Add(weight, null, phase, stride, way: Toward(m.Ahead, m.Aside, paces, stride, stretch));
                return;
            }
            var (ahead, back, right, left) = Shares(m.Ahead, m.Aside);
            Play(ahead, paces.Ahead);
            Play(back, paces.Back);
            Play(right, paces.Right);
            Play(left, paces.Left);

            void Play(float share, Source s) => Add(weight * share, s.Clip, s.Clip == null ? phase : Wrap(s.From + (s.Reversed ? -phase : phase)), stride,
                way: s.Clip == null ? Toward(s.Ahead, s.Aside, paces, stride, stretch) : null);
        }

        void Add(float weight, RigClip? clip, float phase, Stride? stride, (float Glide, float Pulse)? aloft = null, Way? way = null, float? air = null)
        {
            if (weight <= 1e-4f) return;
            if (clip != null && clip.Length > 0) Sampled(clip, phase);
            else Procedural(phase, stride, m.IdlePhase, aloft: aloft, way: way, air: air);
            for (int i = 0; i < _turns.Length; i++)
            {
                var q = _turns[i];
                if (Quaternion.Dot(q, _blendTurns[i]) < 0) q = -q;
                _blendTurns[i] += q * weight;
                _blendPoints[i] += _points[i] * weight;
            }
            total += weight;
        }
    }

    public (Vector3[] Points, Vector3[] Normals) RelaxedShape()
    {
        (_whole, _arm, _hold) = (null, null, 0);
        _squat = 0;
        Procedural(0, null, 0, plain: true);
        return _rig.Posed(_points, _turns);
    }

    void Procedural(float phase, Stride? s, float breath, bool plain = false, (float Glide, float Pulse)? aloft = null, Way? way = null, float? air = null)
    {
        Array.Copy(_restLocalPoints, _points, _points.Length);
        Array.Copy(_restLocalTurns, _turns, _turns.Length);
        if (!Walks && _relax.Count == 0) return;
        var own = _own;
        own.Clear();
        var Turn = _turnOwn;
        foreach (var (role, q) in _relax) Turn(role, q);
        float bob = 0;
        var slide = Vector3.Zero;
        if (_whole is { } whole) bob = Acted(whole, _wholePhase, own, Turn);
        else if (air is { } rising) Leap(rising, Turn);
        else if (aloft is var (glide, pulse))
        {
            float drift = MathF.Sin(breath * Tau), sway = MathF.Sin(breath * Tau * 0.5f);
            float lean = FlyLean * glide;
            Turn(H.Hips, Swing(-lean) * Roll(FlySway * sway));
            Turn(H.Spine, Swing(lean * 0.12f + FlyCurl * pulse));
            Turn(H.Neck, Swing(lean * 0.3f));
            Turn(H.Head, Swing(lean * 0.35f));
            foreach (var (side, sign) in SidesSigned)
            {
                Turn(Of(side, H.UpperLeg), Swing(lean * 0.1f + FlyLegTrail * (1 - glide) + FlyKick * drift * sign * (1 - pulse) - FlyTuck * pulse));
                Turn(Of(side, H.LowerLeg), Swing(-(FlyKnee + FlyKick * MathF.Max(0, drift * sign) + FlyTuck * 1.4f * pulse)));
                Turn(Of(side, H.Foot), Swing(-FlyToes));
                Turn(Of(side, H.UpperArm), Swing(FlyArmBack * glide - FlyTuck * 0.5f * pulse) * Roll(FlyArmOut * (1 - 0.6f * glide) * sign));
                Turn(Of(side, H.LowerArm), Swing(FlyElbow + FlyTuck * pulse));
            }
            bob = FlyBob * drift * _height;
        }
        else if (s != null && _legs.Length == 2) bob = Strode(phase, s, way, Turn);
        else if (!plain && _legs.Length == 2) (bob, slide) = Stood(breath, Turn);
        if (_arm is { } arms)
        {
            Acted(arms, _armPhase, own, Turn);
            HeldThrough(arms, own, Turn);
        }
        else if (_whole?.Id == Brace && _hold == CrowdHolds.Spear) Lock(SpearBrace, own, Turn);
        else if (_whole == null && Holding.TryGetValue(_hold, out var held)) Lock(held, own, Turn);
        Apply(own, bob, slide);
    }

    void Leap(float rising, Action<string, Quaternion> turn)
    {
        float t = (Math.Clamp(rising, -1, 1) + 1) / 2;
        turn(H.Hips, Swing(-AirLean));
        turn(H.Spine, Swing(AirLean * 0.5f + AirCurl * t));
        turn(H.Head, Swing(AirLean * 0.5f));
        foreach (var (side, sign) in SidesSigned)
        {
            bool lead = sign > 0;
            float thigh = Lerp(lead ? FallLead : FallTrail, lead ? RiseLead : RiseTrail, t), knee = Lerp(lead ? FallLeadKnee : FallTrailKnee, lead ? RiseLeadKnee : RiseTrailKnee, t);
            turn(Of(side, H.UpperLeg), Swing(thigh) * Roll(AirLegOut * sign));
            turn(Of(side, H.LowerLeg), Swing(-knee));
            turn(Of(side, H.Foot), Swing(knee - thigh - AirToes * (1 - t)));
            turn(Of(side, H.UpperArm), Swing(Lerp(FallArm, RiseArm, t)) * Roll(Lerp(FallArmOut, RiseArmOut, t) * sign));
            turn(Of(side, H.LowerArm), Swing(AirElbow));
        }
    }

    float Strode(float phase, Stride s, Way? way, Action<string, Quaternion> turn)
    {
        float phi = phase * Tau, sin = MathF.Sin(phi), cos = MathF.Cos(phi);
        var w = way ?? new Way(1, 0, 0, 0, 1);
        float fa = w.Fore * w.Stretch, twist = s.Twist * sin * fa, stanced = 0;
        bool running = ReferenceEquals(s, _gait.Run);
        float rest = s.KneeRest + s.Crouch * CrouchKnee + _squat * LandKnee;
        var hip = Spin(-w.Turn);
        var pelvis = Spin(twist) * Roll(s.Roll * cos) * hip;
        var plans = _stepPlans;
        for (int i = 0; i < 2; i++)
        {
            float sign = i == 0 ? 1 : -1, arm = MathF.Sin(phi - s.ArmLag) * sign * fa;
            plans[i] = Step(_legs[i], sign, phase + i * 0.5f, s, w, fa, running, rest, hip, out float planted);
            stanced = MathF.Max(stanced, planted);
            string side = _legs[i].Side;
            turn(Of(side, H.Shoulder), Swing(-s.Arm * 0.18f * arm));
            turn(Of(side, H.UpperArm), Swing(-s.Arm * arm));
            turn(Of(side, H.LowerArm), Swing(s.ElbowRest + s.ElbowSwing * MathF.Max(0, -arm)));
            turn(Of(side, H.Hand), Swing(s.Wrist * MathF.Max(0, arm)));
        }
        turn(H.Hips, pelvis);
        turn(H.Spine, Swing(-s.Lean) * Spin(-twist * 0.9f + w.Turn * SpineUnturn) * Roll(-s.Roll * 0.7f * cos));
        turn(H.Chest, Spin(-twist * 0.6f + w.Turn * ChestUnturn) * Roll(-s.Roll * 0.3f * cos));
        turn(H.Neck, Spin(twist * 0.25f + w.Turn * (1 - SpineUnturn - ChestUnturn)));
        turn(H.Head, Spin(twist * 0.25f) * Swing(s.Lean * 0.6f));
        float drop = Stand(plans[0], plans[1], pelvis, Vector3.Zero, turn);
        float lift = running ? s.Bob * w.Stretch * _tall * (1 - stanced) * (1 - stanced) : 0;
        return (lift - drop) * _rig.Root.Scale;
    }

    void Apply(Dictionary<int, Quaternion> own, float bob, Vector3 slide = default)
    {
        int n = _rig.Bones.Count;
        if (_posed.Length != n) _posed = new Quaternion[n];
        var posedTurns = _posed;
        for (int i = 0; i < n; i++)
        {
            int p = _rig.Bones[i].Parent;
            var parentMove = p >= 0 ? _moves[p] : Quaternion.Identity;
            _moves[i] = own.TryGetValue(i, out var q) ? Quaternion.Normalize(q * parentMove) : parentMove;
            if (_curls.TryGetValue(i, out var curl)) _moves[i] = Quaternion.Normalize(_moves[i] * curl);
            posedTurns[i] = Quaternion.Normalize(_moves[i] * _restTurns[i]);
            _turns[i] = p >= 0 ? Quaternion.Normalize(Quaternion.Inverse(posedTurns[p]) * posedTurns[i]) : posedTurns[i];
        }
        if ((bob != 0 || slide != Vector3.Zero) && Bone(H.Hips) is { } hips)
        {
            int p = _rig.Bones[hips].Parent;
            var lift = _up * (bob / Math.Max(_rig.Root.Scale, 1e-6f)) + slide;
            _points[hips] = _restLocalPoints[hips] + (p >= 0 ? Vector3.Transform(lift, Quaternion.Inverse(posedTurns[p])) / Math.Max(Scale(p), 1e-6f) : lift);
        }
    }

    float Scale(int bone)
    {
        float s = 1;
        for (int b = bone; b >= 0; b = _rig.Bones[b].Parent) s *= _rig.Bones[b].Scale;
        return s;
    }

    int? Bone(string role) => _rig.Humanoid.TryGetValue(role, out var b) ? b : null;

    static readonly string[] SideNames = [H.Left, H.Right];
    static readonly (string Side, float Sign)[] SidesSigned = [(H.Left, 1f), (H.Right, -1f)];
    static System.Collections.Concurrent.ConcurrentDictionary<(string, string), string>? _roles;
    readonly Dictionary<int, Quaternion> _own = new();
    readonly Footing[] _stepPlans = new Footing[2], _standPlans = new Footing[2];
    readonly Vector3[] _hipsAt = new Vector3[2];
    Quaternion[] _posed = [];
    Action<string, Quaternion>? _turning;

    Action<string, Quaternion> _turnOwn => _turning ??= TurnOwn;

    static string Of(string side, string part) =>
        System.Threading.LazyInitializer.EnsureInitialized(ref _roles).GetOrAdd((side, part), static k => k.Item1 + k.Item2);

    void TurnOwn(string role, Quaternion q)
    {
        if (Bone(role) is { } b) _own[b] = _own.TryGetValue(b, out var had) ? q * had : q;
    }

    Quaternion Swing(float degrees) => Quaternion.CreateFromAxisAngle(_across, Rad(degrees));

    Quaternion Spin(float degrees) => Quaternion.CreateFromAxisAngle(_up, Rad(degrees));

    Quaternion Roll(float degrees) => Quaternion.CreateFromAxisAngle(_ahead, Rad(degrees));

    static float Rad(float degrees) => degrees * MathF.PI / 180;

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static float Deg(float radians) => radians * 180 / MathF.PI;

    (Vector3 Right, float Leg, float Hips) Sides()
    {
        var right = _across;
        float leg = LegShare, hips = HipShare;
        if (Bone(Of(H.Left, H.UpperLeg)) is { } lh && Bone(Of(H.Right, H.UpperLeg)) is { } rh && Bone(Of(H.Left, H.Foot)) is { } lf && Bone(Of(H.Right, H.Foot)) is { } rf)
        {
            float span = Vector3.Dot(_restPoints[rh] - _restPoints[lh], _across);
            if (span < 0) right = -_across;
            hips = MathF.Abs(span) / _tall;
            float measured = (Vector3.Distance(_restPoints[lh], _restPoints[lf]) + Vector3.Distance(_restPoints[rh], _restPoints[rf])) / 2 / _tall;
            if (measured > ShortestLeg) leg = measured;
        }
        return (right, leg, hips);
    }

    Vector3 Forward()
    {
        Vector3 Flat(Vector3 v) => v - Vector3.Dot(v, _up) * _up;
        var sum = Vector3.Zero;
        foreach (var side in SideNames)
        {
            if (Bone(Of(side, H.Foot)) is not { } foot) continue;
            if (Bone(Of(side, H.Toes)) is { } toes) sum += Flat(_restPoints[toes] - _restPoints[foot]);
            else
            {
                var centre = Vector3.Zero;
                int count = 0;
                for (int v = 0; v < _rig.Points.Length; v++)
                    if (_rig.Joints[v * Rig.Influences] == foot && _rig.Weights[v * Rig.Influences] > 0.5f) { centre += _rig.Points[v]; count++; }
                if (count > 0) sum += Flat(centre / count - _restPoints[foot]);
            }
        }
        if (sum.LengthSquared() > 1e-10f) return Vector3.Normalize(sum);
        var fallback = Flat(_rig.Root.Back(Vector3.UnitZ));
        return fallback.LengthSquared() > 1e-10f ? Vector3.Normalize(fallback) : Vector3.UnitZ;
    }

    float Height()
    {
        if (_rig.Points.Length == 0) return 1;
        float lo = float.MaxValue, hi = float.MinValue;
        foreach (var p in _rig.Points)
        {
            float y = _rig.Root.Point(p).Y;
            (lo, hi) = (Math.Min(lo, y), Math.Max(hi, y));
        }
        return hi > lo ? hi - lo : 1;
    }

    public Vector3 FacingInModel()
    {
        var f = _rig.Root.Direction(Forward());
        return new Vector3(f.X, 0, f.Z) is var flat && flat.LengthSquared() > 1e-10f ? Vector3.Normalize(flat) : Vector3.UnitZ;
    }

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 1 - 1e-6f) return Quaternion.Identity;
        if (d < -1 + 1e-6f)
        {
            var axis = Vector3.Cross(Vector3.UnitX, a);
            if (axis.LengthSquared() < 1e-6f) axis = Vector3.Cross(Vector3.UnitY, a);
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.PI);
        }
        var c = Vector3.Cross(a, b);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
    }
}
