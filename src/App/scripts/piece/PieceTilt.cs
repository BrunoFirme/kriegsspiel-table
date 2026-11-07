using Godot;
using KTable.Core.Model;

namespace KTable.App;

public partial class PieceNode
{
    float _tiltHalf;

    Basis OwnTilt => Piece.HasTilt ? Basis.FromEuler(new Vector3(Mathf.DegToRad((float)Piece.TiltX), 0, -Mathf.DegToRad((float)Piece.TiltZ))) : Basis.Identity;

    Basis PoseBasis(float yaw, float flip) => Basis.FromEuler(new Vector3(0, yaw, 0)) * OwnTilt * Basis.FromEuler(new Vector3(0, 0, flip));

    Basis PoseBasis(Vector3 pose) => PoseBasis(pose.Y, pose.Z);

    public Quaternion PoseTurn(float yaw) => new(PoseBasis(PoseFromYaw(yaw)).Orthonormalized());

    void Turn(Basis b) => Quaternion = new Quaternion(b.Orthonormalized());

    Vector3 SlabMiddle => new(0, _thickness / 2, 0);

    internal static float HalfRise(Basis b, Vector3 size)
    {
        float most = 0;
        foreach (var x in new[] { -size.X / 2, size.X / 2 })
            foreach (var y in new[] { -size.Y / 2, size.Y / 2 })
                foreach (var z in new[] { -size.Z / 2, size.Z / 2 })
                    most = Mathf.Max(most, Mathf.Abs((b * new Vector3(x, y, z)).Y));
        return most;
    }

    internal static Vector2 HalfShadow(Basis b, Vector3 size)
    {
        float hx = 0, hz = 0;
        foreach (var x in new[] { -size.X / 2, size.X / 2 })
            foreach (var y in new[] { -size.Y / 2, size.Y / 2 })
                foreach (var z in new[] { -size.Z / 2, size.Z / 2 })
                {
                    var p = b * new Vector3(x, y, z);
                    hx = Mathf.Max(hx, Mathf.Abs(p.X));
                    hz = Mathf.Max(hz, Mathf.Abs(p.Z));
                }
        return new Vector2(hx, hz);
    }

    Vector3 Slab => new(Footprint.X, _thickness, Footprint.Y);

    Transform3D TiltedRest(Basis ground, Vec2 pos, float baseY)
    {
        var b = ground * PoseBasis(_poseYaw, _flipA);
        _tiltHalf = HalfRise(b, Slab);
        return new Transform3D(b, Coords.ToWorld(pos, baseY + _tiltHalf) - b * SlabMiddle);
    }

    public Transform3D RestBox => Piece.HasTilt ? new Transform3D(Basis.Orthonormalized(), Transform * SlabMiddle) : new Transform3D(new Basis(Vector3.Up, Heading), SlabCentre);

    float TiltedTop => (Transform * SlabMiddle).Y + _tiltHalf;

    bool InsideTilted(Vector2 local)
    {
        var half = HalfShadow(OwnTilt * Basis.FromEuler(new Vector3(0, 0, _flipA)), Slab);
        return Mathf.Abs(local.X) <= half.X && Mathf.Abs(local.Y) <= half.Y;
    }
}
