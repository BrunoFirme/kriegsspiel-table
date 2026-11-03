using Godot;
using KTable.Core.Model;
using MotionPose = KTable.Client.Hand.Pose;

namespace KTable.App;

public partial class PieceNode
{
    public MotionPose ShownPose => IsDie
        ? ToPose(new Transform3D(ModelWorldBasis, ModelWorldCenter))
        : ToPose(new Transform3D(Basis.Orthonormalized(), Position));

    public MotionPose HandPose(Vec2 pos, double rotDeg, float y)
    {
        var held = new Transform3D(Basis.FromEuler(Pose(rotDeg)).Scaled(Scale), Coords.ToWorld(pos, y + LiftY));
        if (IsDie && _model != null) held *= new Transform3D(new Basis(_modelRest) * Basis.FromScale(_model.Scale), _modelRestPos);
        return ToPose(held);
    }

    public void ShowPose(MotionPose p)
    {
        var basis = new Basis(new Quaternion(p.Turn.X, p.Turn.Y, p.Turn.Z, p.Turn.W));
        var at = new Vector3(p.At.X, p.At.Y, p.At.Z);
        if (IsDie) { ShowTumble(basis, at); return; }
        Transform = new Transform3D(basis.Scaled(Scale), at);
    }

    static MotionPose ToPose(Transform3D t)
    {
        var q = t.Basis.Orthonormalized().GetRotationQuaternion();
        return new MotionPose(new System.Numerics.Vector3(t.Origin.X, t.Origin.Y, t.Origin.Z), new System.Numerics.Quaternion(q.X, q.Y, q.Z, q.W));
    }
}
