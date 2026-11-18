using System.Numerics;
using F = KTable.Core.Model.CrowdFigure;

namespace KTable.Core.Model;

public sealed record GunGrip(Matrix4x4 Fire, Matrix4x4 Brace, Vector3 FireFore, Vector3 FireLock, Vector3 BraceFore, Vector3 BraceLock);

public static class CrowdGrip
{
    const float LeftOut = 0.35f, RightOut = 1, RightDown = 0.35f, Slack = 1e-4f, FireTwist = -0.5f, BraceTwist = -0.3f;

    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Rig, RigPlace[]> Rests = new();
    [ThreadStatic] static RigPlace[]? _posed;

    public static void Hold(Rig rig, Vector3[] points, Quaternion[] turns, GunGrip? gun, string? act, float scale)
    {
        if (gun == null || rig.Bones.Count <= F.Gun) return;
        var (whole, arm) = RigMotion.Parts(act);
        bool fire = arm == RigMotion.Ranged, brace = !fire && whole == RigMotion.Brace;
        var rest = Rests.GetValue(rig, RestOf);
        var posed = Posed(rig, points, turns);
        if (!fire && !brace)
        {
            Set(points, turns, Delta(posed[F.LFore], rest[F.LFore]));
            return;
        }
        var chest = Delta(posed[F.Upper], rest[F.Upper]);
        var pose = fire ? gun.Fire : gun.Brace;
        Set(points, turns, new RigPlace(pose.Translation * scale, Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(pose)), 1).Then(chest));
        turns[F.Spine] = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(Vector3.UnitY, fire ? FireTwist : BraceTwist) * turns[F.Spine]);
        posed = Posed(rig, points, turns);
        Reach(rig, posed, turns, F.LArm, F.LFore, F.LHand, chest.Point((fire ? gun.FireFore : gun.BraceFore) * scale), chest.Direction(new Vector3(LeftOut, -1, 0)));
        Reach(rig, posed, turns, F.RArm, F.RFore, F.RHand, chest.Point((fire ? gun.FireLock : gun.BraceLock) * scale), chest.Direction(new Vector3(-RightOut, -RightDown, 0)));
    }

    static void Set(Vector3[] points, Quaternion[] turns, RigPlace place) => (points[F.Gun], turns[F.Gun]) = (place.Offset, place.Turn);

    static RigPlace[] RestOf(Rig rig)
    {
        var global = new RigPlace[rig.Bones.Count];
        for (int i = 0; i < global.Length; i++)
        {
            var local = new RigPlace(rig.Bones[i].Position, rig.Bones[i].Rotation, 1);
            global[i] = rig.Bones[i].Parent is var p and >= 0 ? local.Then(global[p]) : local;
        }
        return global;
    }

    static RigPlace[] Posed(Rig rig, Vector3[] points, Quaternion[] turns)
    {
        var global = _posed is { } kept && kept.Length == rig.Bones.Count ? kept : _posed = new RigPlace[rig.Bones.Count];
        for (int i = 0; i < global.Length; i++)
        {
            var local = new RigPlace(points[i], turns[i], 1);
            global[i] = rig.Bones[i].Parent is var p and >= 0 ? local.Then(global[p]) : local;
        }
        return global;
    }

    static RigPlace Delta(RigPlace posed, RigPlace rest)
    {
        var back = Quaternion.Inverse(rest.Turn);
        return new RigPlace(-Vector3.Transform(rest.Offset, back), back, 1).Then(posed);
    }

    static void Reach(Rig rig, RigPlace[] posed, Quaternion[] turns, int upper, int lower, int hand, Vector3 target, Vector3 pole)
    {
        Vector3 a = posed[upper].Offset, b = posed[lower].Offset, c = posed[hand].Offset;
        float l1 = (b - a).Length(), l2 = (c - b).Length();
        var to = target - a;
        if (l1 < Slack || l2 < Slack || to.LengthSquared() < Slack * Slack) return;
        float d = Math.Clamp(to.Length(), MathF.Abs(l1 - l2) + Slack, l1 + l2 - Slack);
        var dir = Vector3.Normalize(to);
        var side = pole - dir * Vector3.Dot(pole, dir);
        side = side.LengthSquared() > Slack ? Vector3.Normalize(side) : Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitX));
        float cos = (l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), sin = MathF.Sqrt(MathF.Max(0, 1 - cos * cos));
        var elbow = a + dir * (l1 * cos) + side * (l1 * sin);
        var q1 = FromTo(Vector3.Normalize(b - a), Vector3.Normalize(elbow - a));
        var upperTurn = Quaternion.Normalize(q1 * posed[upper].Turn);
        var wrist = elbow + Vector3.Transform(c - b, q1);
        var q2 = FromTo(Vector3.Normalize(wrist - elbow), Vector3.Normalize(a + dir * d - elbow));
        var lowerTurn = Quaternion.Normalize(q2 * q1 * posed[lower].Turn);
        int parent = rig.Bones[upper].Parent;
        turns[upper] = Quaternion.Normalize(Quaternion.Inverse(parent >= 0 ? posed[parent].Turn : Quaternion.Identity) * upperTurn);
        turns[lower] = Quaternion.Normalize(Quaternion.Inverse(upperTurn) * lowerTurn);
    }

    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        float d = Vector3.Dot(a, b);
        if (d > 1 - 1e-6f) return Quaternion.Identity;
        var axis = Vector3.Cross(a, b);
        if (axis.LengthSquared() < 1e-10f) axis = MathF.Abs(a.X) < 0.9f ? Vector3.Cross(a, Vector3.UnitX) : Vector3.Cross(a, Vector3.UnitY);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), MathF.Acos(Math.Clamp(d, -1, 1)));
    }
}
