using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public readonly record struct RigMove(float Walk, float WalkPhase, float Run, float RunPhase, float Idle, float IdlePhase, float Fly = 0, float Glide = 0, float Pulse = 0,
    string? Act = null, float ActSeconds = 0)
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
    const float IdleElbow = 9f, IdleKnee = 2.5f, IdleShift = 2.2f, IdleBreath = 1.6f, IdleShoulder = 1.2f, IdleHead = 5f;

    readonly Rig _rig;
    readonly Vector3[] _restPoints;
    readonly Quaternion[] _restTurns;
    readonly Vector3[] _restLocalPoints;
    readonly Quaternion[] _restLocalTurns;
    readonly Vector3 _up, _across, _ahead;
    readonly float _height;
    readonly Dictionary<string, Quaternion> _relax = new();
    readonly RigClip? _walkClip, _runClip, _idleClip;
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
        _runClip = own ? rig.Clip("run") : null;
        _idleClip = own ? rig.Clip("idle") : null;
        Walks = H.Walks(rig.Humanoid);
        _up = Vector3.Normalize(rig.Root.Back(Vector3.UnitY));
        var forward = Forward();
        _across = Vector3.Normalize(Vector3.Cross(forward, _up));
        _ahead = forward;
        _height = Height();
        foreach (var side in new[] { H.Left, H.Right })
            if (Bone(side + H.UpperArm) is { } upper && Bone(side + H.LowerArm) is { } lower)
            {
                var along = _restPoints[lower] - _restPoints[upper];
                if (along.LengthSquared() < 1e-12f) continue;
                along = Vector3.Normalize(along);
                var outward = along - Vector3.Dot(along, _up) * _up;
                outward = outward.LengthSquared() > 1e-8f ? Vector3.Normalize(outward) : Vector3.Zero;
                var hang = Vector3.Normalize(-_up * MathF.Cos(Rad(_gait.ArmOut)) + outward * MathF.Sin(Rad(_gait.ArmOut)) + forward * MathF.Sin(Rad(ArmForward)));
                _relax[side + H.UpperArm] = FromTo(along, hang);
            }
    }

    public void Pose(RigMove m)
    {
        Array.Clear(_blendPoints);
        Array.Clear(_blendTurns);
        float total = 0;
        _act = Walks ? Rows.FirstOrDefault(a => a.Id == m.Act) : null;
        _actPhase = _act == null ? 0 : m.ActSeconds * _act.Rate;
        if (m.Act != null && ActClip(m.Act) is { Length: > 0 } own)
        {
            _act = null;
            Add(1, own, m.ActSeconds / own.Length % 1, null);
        }
        else if (_act is { Whole: true }) Add(1, null, 0, null);
        else
        {
            Add(m.Idle, _idleClip, m.IdlePhase, null);
            Add(m.Walk, _walkClip, m.WalkPhase, _gait.Walk);
            Add(m.Run, _runClip ?? (_walkClip != null && !Walks ? _walkClip : null), m.RunPhase, _gait.Run);
            if (Walks) Add(m.Fly, null, 0, null, (m.Glide, m.Pulse));
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

        void Add(float weight, RigClip? clip, float phase, Stride? stride, (float Glide, float Pulse)? aloft = null)
        {
            if (weight <= 1e-4f) return;
            if (clip != null && clip.Length > 0)
            {
                Array.Copy(_restLocalPoints, _points, _points.Length);
                Array.Copy(_restLocalTurns, _turns, _turns.Length);
                _rig.Sample(clip, phase * clip.Length, _points, _turns);
            }
            else Procedural(phase, stride, m.IdlePhase, aloft: aloft);
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
        _act = null;
        Procedural(0, null, 0, plain: true);
        return _rig.Posed(_points, _turns);
    }

    void Procedural(float phase, Stride? s, float breath, bool plain = false, (float Glide, float Pulse)? aloft = null)
    {
        Array.Copy(_restLocalPoints, _points, _points.Length);
        Array.Copy(_restLocalTurns, _turns, _turns.Length);
        if (!Walks && _relax.Count == 0) return;
        var own = new Dictionary<int, Quaternion>();
        void Turn(string role, Quaternion q)
        {
            if (Bone(role) is { } b) own[b] = own.TryGetValue(b, out var had) ? q * had : q;
        }
        foreach (var (role, q) in _relax) Turn(role, q);
        float phi = phase * Tau, sin = MathF.Sin(phi), cos = MathF.Cos(phi);
        float bob = 0;
        if (_act is { Whole: true } whole) bob = Acted(whole, own, Turn);
        else if (aloft is var (glide, pulse))
        {
            float drift = MathF.Sin(breath * Tau), sway = MathF.Sin(breath * Tau * 0.5f);
            float lean = FlyLean * glide;
            Turn(H.Hips, Swing(-lean) * Roll(FlySway * sway));
            Turn(H.Spine, Swing(lean * 0.12f + FlyCurl * pulse));
            Turn(H.Neck, Swing(lean * 0.3f));
            Turn(H.Head, Swing(lean * 0.35f));
            foreach (var (side, sign) in new[] { (H.Left, 1f), (H.Right, -1f) })
            {
                Turn(side + H.UpperLeg, Swing(lean * 0.1f + FlyLegTrail * (1 - glide) + FlyKick * drift * sign * (1 - pulse) - FlyTuck * pulse));
                Turn(side + H.LowerLeg, Swing(-(FlyKnee + FlyKick * MathF.Max(0, drift * sign) + FlyTuck * 1.4f * pulse)));
                Turn(side + H.Foot, Swing(-FlyToes));
                Turn(side + H.UpperArm, Swing(FlyArmBack * glide - FlyTuck * 0.5f * pulse) * Roll(FlyArmOut * (1 - 0.6f * glide) * sign));
                Turn(side + H.LowerArm, Swing(FlyElbow + FlyTuck * pulse));
            }
            bob = FlyBob * drift * _height;
        }
        else if (s != null)
        {
            foreach (var (side, sign) in new[] { (H.Left, 1f), (H.Right, -1f) })
            {
                float sn = sin * sign, cs = cos * sign;
                float lifted = MathF.Pow(MathF.Max(0, cs), 1.5f), planted = MathF.Max(0, -cs);
                float arm = MathF.Sin(phi - s.ArmLag) * sign;
                Turn(side + H.UpperLeg, Swing(s.Thigh * sn + s.ThighBias) * Roll(-s.Roll * 0.5f * planted * sign));
                Turn(side + H.LowerLeg, Swing(-(s.KneeRest + s.KneeSwing * lifted)));
                Turn(side + H.Foot, Swing(s.Foot * sn - s.Foot * 0.6f * lifted));
                Turn(side + H.Toes, Swing(-s.Foot * 0.8f * MathF.Max(0, -sn) * planted));
                Turn(side + H.Shoulder, Swing(-s.Arm * 0.18f * arm));
                Turn(side + H.UpperArm, Swing(-s.Arm * arm));
                Turn(side + H.LowerArm, Swing(s.ElbowRest + s.ElbowSwing * MathF.Max(0, -arm)));
                Turn(side + H.Hand, Swing(s.Wrist * MathF.Max(0, arm)));
            }
            Turn(H.Hips, Spin(s.Twist * sin) * Roll(s.Roll * cos));
            Turn(H.Spine, Swing(-s.Lean) * Spin(-s.Twist * 0.9f * sin) * Roll(-s.Roll * 0.7f * cos));
            Turn(H.Chest, Spin(-s.Twist * 0.6f * sin) * Roll(-s.Roll * 0.3f * cos));
            Turn(H.Neck, Spin(s.Twist * 0.25f * sin));
            Turn(H.Head, Spin(s.Twist * 0.25f * sin) * Swing(s.Lean * 0.6f));
            bob = (s.Bob * MathF.Cos(2 * phi) - s.Crouch) * _height;
        }
        else if (!plain)
        {
            float b = MathF.Sin(breath * Tau), shift = MathF.Sin(breath * Tau * 0.5f);
            var stance = _gait.Walk;
            bob = -stance.Crouch * _height;
            Turn(H.Hips, Roll(IdleShift * shift));
            Turn(H.Spine, Roll(-IdleShift * 0.8f * shift) * Swing(-IdleBreath * 0.4f * b - stance.Lean * 0.7f));
            Turn(H.Chest, Swing(-IdleBreath * b));
            Turn(H.Head, Spin(IdleHead * MathF.Sin(breath * Tau * 0.5f + 0.9f)) * Swing(0.8f * b));
            foreach (var (side, sign) in new[] { (H.Left, 1f), (H.Right, -1f) })
            {
                Turn(side + H.Shoulder, Roll(IdleShoulder * b * sign));
                float knee = IdleKnee + stance.KneeRest * 0.5f;
                Turn(side + H.LowerArm, Swing(IdleElbow + stance.ElbowRest * 0.5f + 1.5f * b));
                Turn(side + H.UpperLeg, Swing(knee) * Roll(-IdleShift * 0.5f * shift));
                Turn(side + H.LowerLeg, Swing(-2 * knee));
                Turn(side + H.Foot, Swing(knee));
            }
        }
        if (_act is { Whole: false } arms) Acted(arms, own, Turn);
        Apply(own, bob);
    }

    void Apply(Dictionary<int, Quaternion> own, float bob)
    {
        int n = _rig.Bones.Count;
        var posedTurns = new Quaternion[n];
        for (int i = 0; i < n; i++)
        {
            int p = _rig.Bones[i].Parent;
            var parentMove = p >= 0 ? _moves[p] : Quaternion.Identity;
            _moves[i] = own.TryGetValue(i, out var q) ? Quaternion.Normalize(q * parentMove) : parentMove;
            posedTurns[i] = Quaternion.Normalize(_moves[i] * _restTurns[i]);
            _turns[i] = p >= 0 ? Quaternion.Normalize(Quaternion.Inverse(posedTurns[p]) * posedTurns[i]) : posedTurns[i];
        }
        if (bob != 0 && Bone(H.Hips) is { } hips)
        {
            int p = _rig.Bones[hips].Parent;
            var lift = _up * (bob / Math.Max(_rig.Root.Scale, 1e-6f));
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

    Quaternion Swing(float degrees) => Quaternion.CreateFromAxisAngle(_across, Rad(degrees));

    Quaternion Spin(float degrees) => Quaternion.CreateFromAxisAngle(_up, Rad(degrees));

    Quaternion Roll(float degrees) => Quaternion.CreateFromAxisAngle(_ahead, Rad(degrees));

    static float Rad(float degrees) => degrees * MathF.PI / 180;

    Vector3 Forward()
    {
        Vector3 Flat(Vector3 v) => v - Vector3.Dot(v, _up) * _up;
        var sum = Vector3.Zero;
        foreach (var side in new[] { H.Left, H.Right })
        {
            if (Bone(side + H.Foot) is not { } foot) continue;
            if (Bone(side + H.Toes) is { } toes) sum += Flat(_restPoints[toes] - _restPoints[foot]);
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
