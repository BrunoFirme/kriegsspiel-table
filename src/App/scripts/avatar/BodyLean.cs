using Godot;
using MotionPose = KTable.Client.Hand.Pose;

namespace KTable.App;

sealed class BodyLean
{
    const float LeanShare = 0.35f, LeanMost = 0.08f, LeanEase = 4f, TiltShare = 0.2f, TiltMost = 0.14f, PushEase = 6f, PaceEase = 20f;

    Vector3 _from;
    float _lean;
    Vector2 _velocity, _push;

    public Vector2 Step(Vector3 feet, float h, float delta, float yaw)
    {
        var moved = new Vector2(feet.X - _from.X, feet.Z - _from.Z);
        float flat = moved.Length();
        float grade = flat > h * 0.001f ? (feet.Y - _from.Y) / flat : 0, pace = delta > 0 ? flat / delta / h : 0;
        _from = feet;
        float want = flat < h ? Mathf.Clamp(grade * LeanShare, -LeanMost, LeanMost) * Mathf.Clamp(pace / Walker.RunHeights, 0, 1) : 0;
        _lean = Mathf.Lerp(_lean, want, 1 - Mathf.Exp(-delta * LeanEase));
        if (delta > 0 && flat < h)
        {
            var velocity = _velocity.Lerp(moved / delta / h, 1 - Mathf.Exp(-delta * PaceEase));
            _push = _push.Lerp((velocity - _velocity) / delta, 1 - Mathf.Exp(-delta * PushEase));
            _velocity = velocity;
        }
        else (_velocity, _push) = (Vector2.Zero, Vector2.Zero);
        float tilt = Mathf.Min(Mathf.Atan(_push.Length() / Walker.FallHeights) * TiltShare, TiltMost);
        return new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * _lean + (_push.LengthSquared() > 1e-8f ? _push.Normalized() * tilt : Vector2.Zero);
    }

    public static MotionPose Leaned(MotionPose pose, Vector2 lean)
    {
        if (lean.LengthSquared() < 1e-8f) return pose;
        var tilt = new Quaternion(Vector3.Up.Cross(new Vector3(lean.X, 0, lean.Y)).Normalized(), lean.Length());
        var turned = tilt * new Quaternion(pose.Turn.X, pose.Turn.Y, pose.Turn.Z, pose.Turn.W);
        return pose with { Turn = new System.Numerics.Quaternion(turned.X, turned.Y, turned.Z, turned.W) };
    }
}
