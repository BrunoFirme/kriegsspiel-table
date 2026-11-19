using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    const float ClearShare = 0.55f, LoadShare = 0.45f, StrikeEnd = 0.14f, PlantSlack = 0.05f, ToeKeep = 0.15f, Straight = 0.999f, FootShare = 0.07f,
        Retract = 4f, Pawing = 8f, Settle = 0.2f;
    const float ContraRoll = 3f, ContraShift = 0.022f, FreeKnee = 9f, FreeHeel = 7f, ShiftSharp = 2.5f, NaturalSway = 0.011f, LeastShift = 0.4f, MostShift = 1.5f, Fidget = 0.3f, FidgetSeconds = 4.3f,
        GlanceSeconds = 7.7f, GlanceAgain = 3.1f, ArmBreath = 1.2f;

    sealed record Leg(string Side, int Hip, int Knee, int Ankle, int Toes, float Thigh, float Shin, float Foot, float Sole, Vector3 ThighDir, Vector3 ShinDir, Vector3 Hinge,
        Vector3 FootAhead)
    {
        public float Length => Thigh + Shin;

        public float Raised(float degrees) => Foot * MathF.Sin(degrees * MathF.PI / 180) - Sole * (1 - MathF.Cos(degrees * MathF.PI / 180));
    }

    readonly record struct Footing(Vector3 Target, float Knee, float Weight, float Pitch, float ToeFlat, Quaternion Yaw, float Landing = 0, Vector3 Lands = default,
        float LandKnee = 0, float Lifted = 0, float Held = 0);

    Leg[] Legs()
    {
        if (!Walks) return [];
        var legs = new List<Leg>();
        foreach (var side in SideNames)
        {
            if (Bone(Of(side, H.UpperLeg)) is not { } hip || Bone(Of(side, H.LowerLeg)) is not { } knee || Bone(Of(side, H.Foot)) is not { } ankle) return [];
            int toes = Bone(Of(side, H.Toes)) ?? -1;
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
        float s = (t - duty) / (1 - duty), m = -2 * (1 - duty) / duty, back = 1 - s;
        return (2 * Eased(s) - 1 + m * (s * MathF.Pow(back, Retract) - back * MathF.Pow(s, Pawing)), 0, s, false);
    }

    Footing Step(Leg leg, float sign, float phase, Stride s, Way w, float fa, bool running, float rest, Quaternion hip, out float planted)
    {
        var (u, plant, progress, stance) = Stepping(phase, s.Duty);
        planted = plant;
        var middle = _restPoints[_hips];
        var neutral = middle + Vector3.Transform(_restPoints[leg.Ankle] - middle, hip);
        var (fore, right) = (Vector3.Transform(_ahead, hip), Vector3.Transform(_right, hip));
        float reach = leg.Length * MathF.Sin(Rad(s.Reach * fa)), wide = leg.Length * MathF.Sin(Rad(w.Lateral)), spread = leg.Length * MathF.Sin(Rad(w.Spread));
        float narrow = (1 - s.Width) * (1 - w.Across) * Vector3.Dot(neutral - middle, right);
        var flat = neutral + fore * ((u - s.Behind) * reach) + right * (u * wide - sign * spread - narrow);
        float strike = s.Heel, push = s.Push, pitch, raise, knee, toes, landing = 0, lifted = 0, held = 0;
        var lands = Vector3.Zero;
        if (stance)
        {
            pitch = progress < StrikeEnd ? strike * (1 - Eased(progress / StrikeEnd))
                : progress > s.HeelOff ? -push * MathF.Pow((progress - s.HeelOff) / (1 - s.HeelOff), 1.5f) : 0;
            raise = leg.Raised(MathF.Max(0, -pitch));
            float load = running ? MathF.Sin(MathF.PI * progress) : MathF.Pow(MathF.Sin(MathF.PI * MathF.Min(1, progress / LoadShare)), 2);
            knee = rest * (running ? 1 - progress * progress : 1) + s.Give * w.Stretch * load;
            toes = 1;
            held = Eased(1 - MathF.Min(1, progress * s.Duty / Settle));
        }
        else
        {
            float clear = leg.Shin * (1 - MathF.Cos(Rad(s.KneeSwing))) * ClearShare * (1 - SideKneeDrop * w.Across) * (KneeKept + (1 - KneeKept) * w.Stretch);
            pitch = Lerp(-push, strike, progress * progress * (3 - 2 * progress));
            raise = clear * MathF.Sin(MathF.PI * MathF.Pow(progress, s.Tuck)) + leg.Raised(MathF.Max(0, -pitch));
            knee = rest;
            toes = MathF.Max(0, 1 - progress / ToeKeep);
            float from = MathF.Max(0, 1 - Settle / (1 - s.Duty));
            lifted = Eased(MathF.Min(1, progress * (1 - s.Duty) / Settle));
            landing = Eased(Math.Clamp((progress - from) / (1 - from), 0, 1));
            knee = rest * (1 - lifted * (1 - landing));
        }
        lands = neutral + fore * ((1 - s.Behind) * reach) + right * (wide - sign * spread - narrow);
        lands += _up * (GroundAt(lands) + leg.Raised(MathF.Max(0, -strike)));
        return new Footing(flat + _up * (GroundAt(flat) + raise), knee, planted, pitch, toes, Spin(s.ToeOut * sign) * hip, landing, lands, rest, lifted, held);
    }

    readonly record struct Ride(float Shape, float Lift, float Squash);

    float Stand(Footing left, Footing right, Quaternion pelvis, Vector3 slide, Action<string, Quaternion> turn, float soft = 0, Ride? ride = null)
    {
        var plans = _standPlans;
        (plans[0], plans[1]) = (left, right);
        var middle = _restPoints[_hips];
        var hips = _hipsAt;
        float sum = 0, weight = 0, span = 0;
        var deep = _deep;
        for (int i = 0; i < 2; i++)
        {
            var leg = _legs[i];
            hips[i] = middle + slide + Vector3.Transform(_restPoints[leg.Hip] - middle, pelvis);
            float need = Need(leg, plans[i].Target - hips[i], plans[i].Knee), w = plans[i].Weight + PlantSlack;
            (sum, weight) = (sum + need * w, weight + w);
            deep[i] = need;
            span += leg.Length / 2;
        }
        for (int i = 0; i < 2; i++)
        {
            var leg = _legs[i];
            if (plans[i].Held > 0) deep[i] = MathF.Max(deep[i], Lerp(deep[i], Need(leg, plans[i].Lands - hips[i], plans[i].LandKnee), plans[i].Held));
            if (plans[i].Lifted <= 0 && plans[i].Landing <= 0) continue;
            var under = hips[i] - _up * Vector3.Dot(hips[i] - _restPoints[leg.Ankle], _up);
            float carried = Need(leg, under + _up * GroundAt(under) - hips[i], plans[i].LandKnee);
            deep[i] = Lerp(deep[i], carried, plans[i].Lifted);
            if (plans[i].Landing > 0) deep[i] = MathF.Max(deep[i], Lerp(carried, Need(leg, plans[i].Lands - hips[i], plans[i].LandKnee), plans[i].Landing));
        }
        float give = soft * span, drop = ride is { } r ? Bounced(r, give) : SoftMost(SoftMost(deep[0], deep[1], give), sum / weight, give);
        for (int i = 0; i < 2; i++) Reached(_legs[i], plans[i], hips[i] - _up * drop, pelvis, turn);
        return drop;

        float Bounced(Ride r, float give)
        {
            float touch = float.MinValue, squash = 0, held = float.MinValue;
            for (int i = 0; i < 2; i++)
            {
                var leg = _legs[i];
                var under = hips[i] - _up * Vector3.Dot(hips[i] - _restPoints[leg.Ankle], _up);
                under += _up * GroundAt(under);
                touch = MathF.Max(touch, Need(leg, plans[i].Lands - hips[i], plans[i].LandKnee));
                squash += (Need(leg, under - hips[i], plans[i].LandKnee + r.Squash) - Need(leg, under - hips[i], plans[i].LandKnee)) / 2;
            }
            float curve = touch + r.Shape * (r.Shape > 0 ? squash : r.Lift);
            for (int i = 0; i < 2; i++)
            {
                var leg = _legs[i];
                float straight = Need(leg, plans[i].Target - hips[i], 0);
                if (plans[i].Lifted > 0) straight = Lerp(straight, curve, plans[i].Lifted);
                if (plans[i].Landing > 0) straight = Lerp(curve, Need(leg, plans[i].Lands - hips[i], 0), plans[i].Landing);
                held = MathF.Max(held, straight);
            }
            return SoftMost(curve, held, give);
        }
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
        turn(Of(leg.Side, H.UpperLeg), rt * Quaternion.Conjugate(pelvis));
        turn(Of(leg.Side, H.LowerLeg), rs);
        turn(Of(leg.Side, H.Foot), rf * Quaternion.Conjugate(rs * rt));
        if (leg.Toes >= 0) turn(Of(leg.Side, H.Toes), Quaternion.Slerp(rf, Framed(leg.FootAhead, _up, heading, n), f.ToeFlat) * Quaternion.Conjugate(rf));
    }

    readonly float[] _deep = new float[2];

    float Need(Leg leg, Vector3 gap, float knee)
    {
        float down = -Vector3.Dot(gap, _up), level = MathF.Max(gap.LengthSquared() - down * down, 0), k = Rad(knee);
        float reach = leg.Thigh * leg.Thigh + leg.Shin * leg.Shin + 2 * leg.Thigh * leg.Shin * MathF.Cos(k);
        return down - MathF.Sqrt(MathF.Max(reach - level, 1e-8f));
    }

    static float SoftMost(float a, float b, float k)
    {
        if (k <= 0) return MathF.Max(a, b);
        float h = MathF.Max(k - MathF.Abs(a - b), 0) / k;
        return MathF.Max(a, b) + h * h * k / 4;
    }

    (float Bob, Vector3 Slide) Stood(float breath, Action<string, Quaternion> turn)
    {
        var stance = _gait.Walk;
        float t = _seconds, b = MathF.Sin(breath * Tau), shift = MathF.Sin(Tau * t / stance.Shift) + Fidget * MathF.Sin(Tau * t / FidgetSeconds + 1.3f);
        float lean = MathF.Tanh(ShiftSharp * shift) / MathF.Tanh(ShiftSharp * (1 + Fidget));
        float glance = MathF.Sin(Tau * t / GlanceSeconds + 0.9f) * 0.7f + MathF.Sin(Tau * t / GlanceAgain + 2.1f) * 0.3f;
        float rest = IdleKnee + (stance.KneeRest + stance.Crouch * CrouchKnee + _squat * LandKnee) * 0.5f;
        var pelvis = Roll(ContraRoll * lean);
        var slide = -_right * (ContraShift * Math.Clamp(stance.Sway / NaturalSway, LeastShift, MostShift) * _tall * lean);
        turn(H.Hips, pelvis);
        turn(H.Spine, Roll(-ContraRoll * 0.6f * lean) * Swing(-IdleBreath * 0.4f * b - stance.Lean * 0.7f));
        turn(H.Chest, Roll(-ContraRoll * 0.3f * lean) * Swing(-IdleBreath * b));
        turn(H.Head, Spin(IdleHead * glance) * Swing(0.8f * b + stance.Chin * 0.7f) * Roll(-ContraRoll * 0.1f * lean));
        var plans = _stepPlans;
        for (int i = 0; i < 2; i++)
        {
            var leg = _legs[i];
            float sign = i == 0 ? 1 : -1, bear = (1 + sign * lean) / 2, free = 1 - bear;
            turn(Of(leg.Side, H.Shoulder), Roll(IdleShoulder * b * sign));
            turn(Of(leg.Side, H.UpperArm), Swing(stance.ArmAhead * 0.8f + ArmBreath * b * sign));
            turn(Of(leg.Side, H.LowerArm), Swing(IdleElbow + stance.ElbowRest * 0.5f + 1.5f * b));
            var flat = _restPoints[leg.Ankle];
            float heel = FreeHeel * free;
            plans[i] = new Footing(flat + _up * (GroundAt(flat) + leg.Raised(heel)), rest + FreeKnee * free, bear, -heel, 1, Spin(stance.ToeOut * sign));
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
