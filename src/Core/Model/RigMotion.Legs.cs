using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    const float ToeOut = 6f, HeelStrikeShare = 0.7f, PushOffShare = 1.6f, ClearShare = 0.55f, ClearPeak = 0.75f, LoadShare = 0.45f, HeelOff = 0.55f,
        StrikeEnd = 0.12f, PlantSlack = 0.05f, ToeKeep = 0.15f, Straight = 0.999f, FootShare = 0.07f, ContactFade = 0.15f, Reachless = 0.3f;
    const float ContraRoll = 3f, ContraShift = 0.022f, FreeKnee = 9f, FreeHeel = 7f, ShiftSeconds = 11f, ShiftSharp = 2.5f;

    sealed record Leg(string Side, int Hip, int Knee, int Ankle, int Toes, float Thigh, float Shin, float Foot, float Sole, Vector3 ThighDir, Vector3 ShinDir, Vector3 Hinge,
        Vector3 FootAhead)
    {
        public float Length => Thigh + Shin;

        public float Raised(float degrees) => Foot * MathF.Sin(degrees * MathF.PI / 180) - Sole * (1 - MathF.Cos(degrees * MathF.PI / 180));
    }

    readonly record struct Footing(Vector3 Target, float Knee, float Weight, float Pitch, float ToeFlat, Quaternion Yaw, float Contact = 1);

    Leg[] Legs()
    {
        if (!Walks) return [];
        var legs = new List<Leg>();
        foreach (var side in new[] { H.Left, H.Right })
        {
            if (Bone(side + H.UpperLeg) is not { } hip || Bone(side + H.LowerLeg) is not { } knee || Bone(side + H.Foot) is not { } ankle) return [];
            int toes = Bone(side + H.Toes) ?? -1;
            var ahead = toes >= 0 ? Flat(_restPoints[toes] - _restPoints[ankle]) : Vector3.Zero;
            float foot = ahead.Length();
            (ahead, foot) = foot > 1e-6f ? (ahead / foot, foot) : (_ahead, FootShare * _tall);
            var reach = Vector3.Normalize(_restPoints[ankle] - _restPoints[hip]);
            var bend = _ahead - Vector3.Dot(_ahead, reach) * reach;
            float sole = toes >= 0 ? Vector3.Dot(_restPoints[ankle] - _restPoints[toes], _up) : 0;
            legs.Add(new Leg(side, hip, knee, ankle, toes, Vector3.Distance(_restPoints[hip], _restPoints[knee]), Vector3.Distance(_restPoints[knee], _restPoints[ankle]),
                foot, sole, Vector3.Normalize(_restPoints[knee] - _restPoints[hip]), Vector3.Normalize(_restPoints[ankle] - _restPoints[knee]),
                Vector3.Normalize(Vector3.Cross(reach, Vector3.Normalize(bend))), ahead));
        }
        return legs.ToArray();
    }

    Vector3 Flat(Vector3 v) => v - Vector3.Dot(v, _up) * _up;

    float GroundAt(Vector3 p) => _base + _grade * Vector3.Dot(p - _origin, _ahead) + _tilt * Vector3.Dot(p - _origin, _right);

    Vector3 GroundNormal => Vector3.Normalize(_up - _grade * _ahead - _tilt * _right);

    static (float Reach, float Planted, float Progress, bool Stance) Stepping(float phase, float duty)
    {
        float t = Wrap(phase - LeadPhase);
        if (t < duty) return (1 - 2 * t / duty, MathF.Sin(MathF.PI * t / duty), t / duty, true);
        float s = (t - duty) / (1 - duty), m = -2 * (1 - duty) / duty;
        return ((((2 * m - 4) * s + 6 - 3 * m) * s + m) * s - 1, 0, s, false);
    }

    Footing Step(Leg leg, float sign, float phase, Stride s, Way w, float fa, bool running, float rest, Quaternion hip, out float planted)
    {
        var (u, plant, progress, stance) = Stepping(phase, running ? RunDuty : WalkDuty);
        planted = plant;
        var middle = _restPoints[_hips];
        var neutral = middle + Vector3.Transform(_restPoints[leg.Ankle] - middle, hip);
        var (fore, right) = (Vector3.Transform(_ahead, hip), Vector3.Transform(_right, hip));
        float reach = leg.Length * MathF.Sin(Rad(s.Thigh * fa)), wide = leg.Length * MathF.Sin(Rad(w.Lateral)), spread = leg.Length * MathF.Sin(Rad(w.Spread));
        var flat = neutral + fore * (u * reach) + right * (u * wide - sign * spread);
        float strike = s.Foot * HeelStrikeShare, push = s.Foot * PushOffShare, pitch, raise, knee, toes, contact = 1;
        if (stance)
        {
            pitch = progress < StrikeEnd ? strike * (1 - progress / StrikeEnd) : progress > HeelOff ? -push * MathF.Pow((progress - HeelOff) / (1 - HeelOff), 1.5f) : 0;
            raise = leg.Raised(MathF.Max(0, -pitch));
            knee = rest + (running ? RunGive : WalkGive) * w.Stretch * MathF.Sin(MathF.PI * (running ? progress : MathF.Min(1, progress / LoadShare)));
            toes = 1;
        }
        else
        {
            float clear = leg.Shin * (1 - MathF.Cos(Rad(s.KneeSwing))) * ClearShare * (1 - SideKneeDrop * w.Across) * (KneeKept + (1 - KneeKept) * w.Stretch);
            pitch = Lerp(-push, strike, progress * progress * (3 - 2 * progress));
            raise = clear * MathF.Sin(MathF.PI * MathF.Pow(progress, ClearPeak)) + leg.Raised(push) * (1 - progress) * (1 - progress);
            knee = rest;
            toes = MathF.Max(0, 1 - progress / ToeKeep);
            contact = MathF.Max(0, MathF.Max(1 - progress / ContactFade, (progress - 1 + ContactFade) / ContactFade));
        }
        return new Footing(flat + _up * (GroundAt(flat) + raise), knee, planted, pitch, toes, Spin(ToeOut * sign) * hip, contact);
    }

    float Stand(Footing left, Footing right, Quaternion pelvis, Vector3 slide, Action<string, Quaternion> turn)
    {
        var plans = new[] { left, right };
        var middle = _restPoints[_hips];
        var hips = new Vector3[2];
        float sum = 0, weight = 0, deepest = float.MinValue;
        for (int i = 0; i < 2; i++)
        {
            var leg = _legs[i];
            hips[i] = middle + slide + Vector3.Transform(_restPoints[leg.Hip] - middle, pelvis);
            var gap = plans[i].Target - hips[i];
            float down = -Vector3.Dot(gap, _up), level = MathF.Max(gap.LengthSquared() - down * down, 0), k = Rad(plans[i].Knee);
            float reach = leg.Thigh * leg.Thigh + leg.Shin * leg.Shin + 2 * leg.Thigh * leg.Shin * MathF.Cos(k);
            float need = down - MathF.Sqrt(MathF.Max(reach - level, 1e-8f)), w = plans[i].Weight + PlantSlack;
            (sum, weight) = (sum + need * w, weight + w);
            deepest = MathF.Max(deepest, need - (1 - plans[i].Contact) * Reachless * leg.Length);
        }
        float drop = MathF.Max(sum / weight, deepest);
        for (int i = 0; i < 2; i++) Reached(_legs[i], plans[i], hips[i] - _up * drop, pelvis, turn);
        return drop;
    }

    void Reached(Leg leg, Footing f, Vector3 hip, Quaternion pelvis, Action<string, Quaternion> turn)
    {
        var gap = f.Target - hip;
        float c = Math.Clamp(gap.Length(), MathF.Abs(leg.Thigh - leg.Shin) + 1e-4f, leg.Length * Straight);
        var toward = gap.LengthSquared() > 1e-12f ? Vector3.Normalize(gap) : -_up;
        var pole = Vector3.Transform(_ahead, f.Yaw);
        var bend = pole - Vector3.Dot(pole, toward) * toward;
        bend = bend.LengthSquared() > 1e-10f ? Vector3.Normalize(bend) : Vector3.Normalize(Vector3.Cross(_across, toward));
        float cos = Math.Clamp((leg.Thigh * leg.Thigh + c * c - leg.Shin * leg.Shin) / (2 * leg.Thigh * c), -1, 1);
        var thigh = toward * cos + bend * MathF.Sqrt(1 - cos * cos);
        var shin = Vector3.Normalize(hip + toward * c - (hip + thigh * leg.Thigh));
        var hinge = Vector3.Normalize(Vector3.Cross(toward, bend));
        var rt = Framed(leg.ThighDir, leg.Hinge, thigh, hinge);
        var rs = Quaternion.CreateFromAxisAngle(hinge, Signed(Vector3.Transform(leg.ShinDir, rt), shin, hinge));
        var n = GroundNormal;
        var heading = Vector3.Normalize(pole - Vector3.Dot(pole, n) * n);
        var tip = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(heading, n)), Rad(f.Pitch));
        var rf = Framed(leg.FootAhead, _up, Vector3.Transform(heading, tip), Vector3.Transform(n, tip));
        turn(leg.Side + H.UpperLeg, rt * Quaternion.Conjugate(pelvis));
        turn(leg.Side + H.LowerLeg, rs);
        turn(leg.Side + H.Foot, rf * Quaternion.Conjugate(rs * rt));
        if (leg.Toes >= 0) turn(leg.Side + H.Toes, Quaternion.Slerp(rf, Framed(leg.FootAhead, _up, heading, n), f.ToeFlat) * Quaternion.Conjugate(rf));
    }

    (float Bob, Vector3 Slide) Stood(float breath, Action<string, Quaternion> turn)
    {
        var stance = _gait.Walk;
        float b = MathF.Sin(breath * Tau), lean = MathF.Tanh(ShiftSharp * MathF.Sin(Tau * _seconds / ShiftSeconds)) / MathF.Tanh(ShiftSharp);
        float rest = IdleKnee + (stance.KneeRest + stance.Crouch * CrouchKnee + _squat * LandKnee) * 0.5f;
        var pelvis = Roll(ContraRoll * lean);
        var slide = -_right * (ContraShift * _tall * lean);
        turn(H.Hips, pelvis);
        turn(H.Spine, Roll(-ContraRoll * 0.6f * lean) * Swing(-IdleBreath * 0.4f * b - stance.Lean * 0.7f));
        turn(H.Chest, Roll(-ContraRoll * 0.3f * lean) * Swing(-IdleBreath * b));
        turn(H.Head, Spin(IdleHead * MathF.Sin(breath * Tau * 0.5f + 0.9f)) * Swing(0.8f * b) * Roll(-ContraRoll * 0.1f * lean));
        var plans = new Footing[2];
        for (int i = 0; i < 2; i++)
        {
            var leg = _legs[i];
            float sign = i == 0 ? 1 : -1, bear = (1 + sign * lean) / 2, free = 1 - bear;
            turn(leg.Side + H.Shoulder, Roll(IdleShoulder * b * sign));
            turn(leg.Side + H.LowerArm, Swing(IdleElbow + stance.ElbowRest * 0.5f + 1.5f * b));
            var flat = _restPoints[leg.Ankle];
            float heel = FreeHeel * free;
            plans[i] = new Footing(flat + _up * (GroundAt(flat) + leg.Raised(heel)), rest + FreeKnee * free, bear, -heel, 1, Spin(ToeOut * sign));
        }
        return (-Stand(plans[0], plans[1], pelvis, slide, turn) * _rig.Root.Scale, slide);
    }

    static Quaternion Framed(Vector3 fromA, Vector3 fromB, Vector3 toA, Vector3 toB)
    {
        var a = Vector3.Normalize(toA);
        var first = FromTo(Vector3.Normalize(fromA), a);
        var moved = Vector3.Transform(fromB, first);
        var x = moved - Vector3.Dot(moved, a) * a;
        var y = toB - Vector3.Dot(toB, a) * a;
        if (x.LengthSquared() < 1e-10f || y.LengthSquared() < 1e-10f) return first;
        return Quaternion.Normalize(Quaternion.CreateFromAxisAngle(a, Signed(x, y, a)) * first);
    }

    static float Signed(Vector3 from, Vector3 to, Vector3 axis) => MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));
}
