using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    public const float WalkStride = 0.62f, RunStride = 0.93f;
    const float SideWalk = 0.34f, SideRun = 0.6f, BackShare = 0.85f;
    const float WalkCadence = 0.95f, RunCadence = 1.45f, StretchLeast = 0.55f, StretchMost = 1.15f;
    const float ShortestCycle = 0.15f, LongestCycle = 2.6f, LeadPhase = 0.25f, Overshoot = 1.2f, WalkTurn = 12f, RunTurn = 45f, Square = 90f;
    const int Samples = 48;
    const string Back = "back", Left = "left", Right = "right";

    readonly record struct Way(float Fore, float Lateral, float Spread, float Across, float Stretch, float Turn = 0);

    sealed record Source(RigClip? Clip, float Length, float From, bool Reversed, float Ahead, float Aside);

    sealed record Paces(Source Ahead, Source Back, Source Right, Source Left)
    {
        public bool Built => Ahead.Clip == null && Back.Clip == null && Right.Clip == null && Left.Clip == null;
    }

    readonly record struct Gauged(float Length, float Lead, float Trail);

    static readonly (RigClip? Back, RigClip? Left, RigClip? Right) NoWays = default;

    public float CycleLength(float run, float ahead, float aside, float speed)
    {
        float stretch = StretchAt(speed, run);
        return Along(_walking, ahead, aside, stretch, false) * (1 - run) + Along(_running, ahead, aside, stretch, true) * run;
    }

    float StretchAt(float speed, float run)
    {
        if (speed <= 0) return 1;
        float natural = (_walking.Ahead.Length * WalkCadence * (1 - run) + _running.Ahead.Length * RunCadence * run) * _pace;
        return Math.Clamp(MathF.Sqrt(speed / Math.Max(natural, 1e-4f)), StretchLeast, StretchMost);
    }

    static float Along(Paces p, float ahead, float aside, float stretch, bool running)
    {
        if (p.Built)
        {
            float n = MathF.Sqrt(ahead * ahead + aside * aside);
            if (n < 1e-4f) return p.Ahead.Length * stretch;
            var (fa, la) = Unturned(ahead / n, aside / n, HipTurn(ahead, aside, running));
            float lf = fa >= 0 ? p.Ahead.Length : p.Back.Length, ll = la >= 0 ? p.Right.Length : p.Left.Length;
            return (lf * fa * fa + ll * la * la) * stretch;
        }
        var (a, b, r, l) = Shares(ahead, aside);
        float Of(Source s) => s.Length * (s.Clip == null ? stretch : 1);
        return a * Of(p.Ahead) + b * Of(p.Back) + r * Of(p.Right) + l * Of(p.Left);
    }

    static (float Ahead, float Back, float Right, float Left) Shares(float ahead, float aside)
    {
        float a = MathF.Max(0, ahead), b = MathF.Max(0, -ahead), r = MathF.Max(0, aside), l = MathF.Max(0, -aside), sum = a + b + r + l;
        return sum < 1e-4f ? (1, 0, 0, 0) : (a / sum, b / sum, r / sum, l / sum);
    }

    static float Wrap(float phase) => phase - MathF.Floor(phase);

    static float HipTurn(float ahead, float aside, bool running)
    {
        float most = running ? RunTurn : WalkTurn, toward = Deg(MathF.Atan2(aside, ahead));
        return MathF.Abs(toward) <= Square ? Math.Clamp(toward, -most, most) : MathF.Sign(toward) * most * (2 * Square - MathF.Abs(toward)) / Square;
    }

    static (float Ahead, float Aside) Unturned(float ahead, float aside, float degrees)
    {
        float c = MathF.Cos(Rad(degrees)), s = MathF.Sin(Rad(degrees));
        return (ahead * c + aside * s, aside * c - ahead * s);
    }

    Way Toward(float ahead, float aside, Paces? paces, Stride stride, float stretch)
    {
        float n = MathF.Sqrt(ahead * ahead + aside * aside), m = MathF.Min(1, n);
        if (n < 1e-4f) return new Way(0, 0, 0, 0, stretch);
        bool running = ReferenceEquals(stride, _gait.Run);
        float turn = paces is { Built: true } ? HipTurn(ahead, aside, running) : 0;
        var (ua, ul) = Unturned(ahead / n, aside / n, turn);
        float duty = running ? RunDuty : WalkDuty, planned = running ? SideRun : SideWalk;
        float forward = paces?.Ahead.Length ?? WalkStride, back = paces?.Back.Length ?? forward * BackShare;
        float side = (ul >= 0 ? paces?.Right.Length : paces?.Left.Length) ?? planned;
        float cycle = (ua >= 0 ? forward : back) * ua * ua + side * ul * ul;
        float amp = MathF.Abs(cycle * ul) * planned / side * stretch * duty / 2;
        float reach = Deg(MathF.Asin(MathF.Min(ReachMost, amp / _legShare))) * MathF.Sign(ul);
        float spread = Deg(MathF.Asin(MathF.Min(ReachMost, MathF.Max(0, (FootGap + 2 * amp * Overshoot - _hipShare) / 2) / _legShare)));
        return new Way(cycle * ua / forward * m, reach * m, spread * m, MathF.Abs(ul) * m, stretch, turn * m);
    }

    (Paces Walk, Paces Run) Resolve(bool own)
    {
        var (walkClip, runClip) = own ? (_rig.Clip("walk"), _rig.Clip("run")) : (null, null);
        var (walkWays, runWays) = own ? (WaysOf("walk"), WaysOf("run")) : (NoWays, NoWays);
        var walking = Resolved(walkClip, walkWays, runWays, _gait.Walk, WalkStride);
        if (runClip == null && walkClip != null && !Walks) return (walking, walking);
        return (walking, Resolved(runClip, runWays, walkWays, _gait.Run, RunStride));
    }

    (RigClip? Back, RigClip? Left, RigClip? Right) WaysOf(string motion) => (_rig.Toward(motion, Back), _rig.Toward(motion, Left), _rig.Toward(motion, Right));

    Paces Resolved(RigClip? clip, (RigClip? Back, RigClip? Left, RigClip? Right) ways, (RigClip? Back, RigClip? Left, RigClip? Right) other, Stride stride,
        float plain)
    {
        Source Built(float a, float l) => new(null, Gauge(p => Procedural(p, stride, 0, way: Toward(a, l, null, stride, 1)), a, l)?.Length ?? plain, 0, false, a, l);
        if (clip == null) return new Paces(Built(1, 0), Built(-1, 0), Built(0, 1), Built(0, -1));
        var gauged = Gauge(p => Sampled(clip, p), 1, 0);
        var ahead = new Source(clip, gauged?.Length ?? plain, Wrap((gauged?.Lead ?? LeadPhase) - LeadPhase), false, 1, 0);
        var back = Played(ways.Back, -1, 0) ?? new Source(clip, ahead.Length, Wrap((gauged?.Trail ?? LeadPhase + 0.5f) + LeadPhase), true, -1, 0);
        Source Sideways(RigClip? mine, RigClip? borrowed, float toward) =>
            Played(mine, 0, toward) ?? Played(borrowed, 0, toward) ?? (Walks ? Built(0, toward) : ahead);
        return new Paces(ahead, back, Sideways(ways.Right, other.Right, 1), Sideways(ways.Left, other.Left, -1));

        Source? Played(RigClip? own, float a, float l)
        {
            if (own == null) return null;
            var g = Gauge(p => Sampled(own, p), a, l);
            return new Source(own, g?.Length ?? plain, Wrap((g?.Lead ?? LeadPhase) - LeadPhase), false, a, l);
        }
    }

    void Sampled(RigClip clip, float phase)
    {
        Array.Copy(_restLocalPoints, _points, _points.Length);
        Array.Copy(_restLocalTurns, _turns, _turns.Length);
        _rig.Sample(clip, phase * clip.Length, _points, _turns);
    }

    Gauged? Gauge(Action<float> pose, float ahead, float aside)
    {
        if (H.Feet(_rig.Bones, _rig.Humanoid) is not var (left, right)) return null;
        var axis = _ahead * ahead + _right * aside;
        var along = new float[2, Samples];
        for (int i = 0; i < Samples; i++)
        {
            pose(i / (float)Samples);
            var (points, _) = _rig.Placed(_points, _turns);
            along[0, i] = Vector3.Dot(points[left], axis);
            along[1, i] = Vector3.Dot(points[right], axis);
        }
        float travel = 0;
        for (int f = 0; f < 2; f++)
        {
            int longest = 0, end = 0, count = 0;
            for (int i = 0; i < Samples * 2; i++)
            {
                count = along[f, i % Samples] > along[f, (i + 1) % Samples] ? count + 1 : 0;
                if (count > longest && count < Samples) (longest, end) = (count, i);
            }
            if (longest == 0) return null;
            var drops = Enumerable.Range(end - longest + 1, longest).Select(i => along[f, i % Samples] - along[f, (i + 1) % Samples]).Order().ToList();
            travel += drops[drops.Count / 2] * Samples / 2;
        }
        float length = travel / _tall;
        if (length is < ShortestCycle or > LongestCycle) return null;
        int lead = 0, trail = 0;
        for (int i = 1; i < Samples; i++)
        {
            if (along[0, i] > along[0, lead]) lead = i;
            if (along[0, i] < along[0, trail]) trail = i;
        }
        return new Gauged(length, lead / (float)Samples, trail / (float)Samples);
    }
}
