using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public readonly record struct RigMove(float Walk, float WalkPhase, float Run, float RunPhase, float Idle, float IdlePhase)
{
    public static readonly RigMove Still = new(0, 0, 0, 0, 1, 0);
}

public sealed class RigMotion
{
    const float ArmOut = 9f, ArmForward = 4f;
    const float Tau = MathF.PI * 2;

    sealed record Stride(float Thigh, float ThighBias, float KneeRest, float KneeSwing, float Foot, float Bob, float Twist, float Lean, float Arm, float ElbowRest, float ElbowSwing);

    static readonly Stride Walking = new(24, 0, 4, 55, 10, 0.010f, 5, 2, 16, 12, 14);
    static readonly Stride Running = new(40, 8, 14, 95, 15, 0.022f, 8, 10, 38, 75, 20);

    readonly Rig _rig;
    readonly Vector3[] _restPoints;
    readonly Quaternion[] _restTurns;
    readonly Vector3[] _restLocalPoints;
    readonly Quaternion[] _restLocalTurns;
    readonly Vector3 _up, _across;
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

    public RigMotion(Rig rig)
    {
        _rig = rig;
        (_restPoints, _restTurns, _) = rig.Rest();
        (_restLocalPoints, _restLocalTurns) = rig.RestLocal();
        int n = rig.Bones.Count;
        (_points, _blendPoints, _turns, _blendTurns, _moves) = (new Vector3[n], new Vector3[n], new Quaternion[n], new Quaternion[n], new Quaternion[n]);
        _walkClip = rig.Clip("walk");
        _runClip = rig.Clip("run");
        _idleClip = rig.Clip("idle");
        Walks = H.Walks(rig.Humanoid);
        _up = Vector3.Normalize(rig.Root.Back(Vector3.UnitY));
        var forward = Forward();
        _across = Vector3.Normalize(Vector3.Cross(forward, _up));
        _height = Height();
        foreach (var side in new[] { H.Left, H.Right })
            if (Bone(side + H.UpperArm) is { } upper && Bone(side + H.LowerArm) is { } lower)
            {
                var along = _restPoints[lower] - _restPoints[upper];
                if (along.LengthSquared() < 1e-12f) continue;
                along = Vector3.Normalize(along);
                var outward = along - Vector3.Dot(along, _up) * _up;
                outward = outward.LengthSquared() > 1e-8f ? Vector3.Normalize(outward) : Vector3.Zero;
                var hang = Vector3.Normalize(-_up * MathF.Cos(Rad(ArmOut)) + outward * MathF.Sin(Rad(ArmOut)) + forward * MathF.Sin(Rad(ArmForward)));
                _relax[side + H.UpperArm] = FromTo(along, hang);
            }
    }

    public void Pose(RigMove m)
    {
        Array.Clear(_blendPoints);
        Array.Clear(_blendTurns);
        float total = 0;
        Add(m.Idle, _idleClip, m.IdlePhase, null);
        Add(m.Walk, _walkClip, m.WalkPhase, Walking);
        Add(m.Run, _runClip ?? (_walkClip != null && !Walks ? _walkClip : null), m.RunPhase, Running);
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

        void Add(float weight, RigClip? clip, float phase, Stride? stride)
        {
            if (weight <= 1e-4f) return;
            if (clip != null && clip.Length > 0)
            {
                Array.Copy(_restLocalPoints, _points, _points.Length);
                Array.Copy(_restLocalTurns, _turns, _turns.Length);
                _rig.Sample(clip, phase * clip.Length, _points, _turns);
            }
            else Procedural(phase, stride, m.IdlePhase);
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
        Procedural(0, null, 0);
        return _rig.Posed(_points, _turns);
    }

    void Procedural(float phase, Stride? s, float breath)
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
        if (s != null)
        {
            foreach (var (side, sign) in new[] { (H.Left, 1f), (H.Right, -1f) })
            {
                float sn = sin * sign, cs = cos * sign;
                Turn(side + H.UpperLeg, Swing(s.Thigh * sn + s.ThighBias));
                Turn(side + H.LowerLeg, Swing(-(s.KneeRest + s.KneeSwing * MathF.Pow(MathF.Max(0, cs), 1.5f))));
                Turn(side + H.Foot, Swing(s.Foot * sn));
                Turn(side + H.UpperArm, Swing(-s.Arm * sn));
                Turn(side + H.LowerArm, Swing(s.ElbowRest + s.ElbowSwing * MathF.Max(0, -sn)));
            }
            Turn(H.Hips, Spin(s.Twist * sin));
            Turn(H.Spine, Swing(-s.Lean) * Spin(-s.Twist * 0.8f * sin));
            bob = s.Bob * MathF.Cos(2 * phi) * _height;
        }
        else
        {
            float b = MathF.Sin(breath * Tau);
            Turn(H.Chest, Swing(-1.2f * b));
            Turn(H.Head, Spin(4f * MathF.Sin(breath * Tau * 0.5f)) * Swing(0.8f * b));
        }
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
