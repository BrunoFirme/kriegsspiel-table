using System.Linq;
using Godot;

namespace KTable.App;

readonly record struct LodView(Plane[] Planes, Vector3 Inside, Vector3 Eye, Vector3 Forward, float Near, float PixelsAtUnitDistance, float? FlatPixelsPerUnit, float Boost)
{
    public static LodView Of(Camera3D cam, float viewportHeight, float boost)
    {
        var xf = cam.GlobalTransform;
        var forward = -xf.Basis.Z;
        float? flat = cam.Projection == Camera3D.ProjectionType.Orthogonal ? viewportHeight / Mathf.Max(0.0001f, cam.Size) : null;
        return new LodView(
            cam.GetFrustum().ToArray(),
            xf.Origin + forward * (cam.Near + cam.Far) * 0.5f,
            xf.Origin, forward, cam.Near,
            viewportHeight / (2f * Mathf.Tan(Mathf.DegToRad(cam.Fov) / 2f)),
            flat, boost);
    }

    public float PixelsPerUnitAt(Vector3 world) => (FlatPixelsPerUnit ?? PixelsAtUnitDistance / Mathf.Max(Near, (world - Eye).Dot(Forward))) * Boost;

    public bool Sees(Aabb box)
    {
        foreach (var p in Planes)
        {
            var n = p.Normal;
            bool outward = p.DistanceTo(Inside) < 0;
            var corner = box.Position + new Vector3(
                n.X > 0 == outward ? 0 : box.Size.X,
                n.Y > 0 == outward ? 0 : box.Size.Y,
                n.Z > 0 == outward ? 0 : box.Size.Z);
            float d = p.DistanceTo(corner);
            if (outward ? d > 0 : d < 0) return false;
        }
        return true;
    }
}
