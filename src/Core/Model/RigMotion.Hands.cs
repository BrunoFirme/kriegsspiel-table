using System.Numerics;
using H = KTable.Core.Model.RigHumanoid;

namespace KTable.Core.Model;

public sealed partial class RigMotion
{
    const float ThumbCurl = 8f, FingerCurl = 11f, FingerStep = 5f, PalmDown = 0.3f;

    Dictionary<int, Quaternion> Curls()
    {
        var curls = new Dictionary<int, Quaternion>();
        var digits = H.Digits(_rig.Bones, _rig.Humanoid);
        foreach (var side in new[] { -1, 1 })
        {
            var mine = digits.Where(d => d.Side == side).ToList();
            if (mine.Count == 0) continue;
            var palm = Palm(mine);
            foreach (var d in mine)
            {
                var axis = Vector3.Cross(Along(d.Bone), palm);
                if (axis.LengthSquared() < 1e-8f) continue;
                curls[d.Bone] = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), Rad(d.Thumb ? ThumbCurl : FingerCurl + FingerStep * d.Depth));
            }
        }
        return curls;
    }

    Vector3 Along(int bone)
    {
        int child = -1;
        for (int i = 0; i < _rig.Bones.Count && child < 0; i++)
            if (_rig.Bones[i].Parent == bone) child = i;
        int parent = _rig.Bones[bone].Parent;
        var dir = child >= 0 ? _restPoints[child] - _restPoints[bone] : parent >= 0 ? _restPoints[bone] - _restPoints[parent] : Vector3.Zero;
        return dir.LengthSquared() > 1e-12f ? Vector3.Normalize(dir) : Vector3.Zero;
    }

    Vector3 Palm(List<H.Digit> mine)
    {
        var roots = mine.Where(d => d.Depth == 0 && !d.Thumb).Select(d => d.Bone).ToList();
        if (roots.Count < 2 || _rig.Bones[roots[0]].Parent is not (>= 0 and var hand)) return -_up;
        var points = roots.Select(r => _restPoints[r]).ToList();
        var (a, b) = (points[0], points[0]);
        float widest = 0;
        foreach (var p in points)
            foreach (var q in points)
                if (Vector3.DistanceSquared(p, q) > widest) (widest, a, b) = (Vector3.DistanceSquared(p, q), p, q);
        var reach = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Count - _restPoints[hand];
        var n = Vector3.Cross(reach, b - a);
        if (n.LengthSquared() < 1e-12f) return -_up;
        n = Vector3.Normalize(n);
        if (Vector3.Dot(n, _up) > 0) n = -n;
        return -Vector3.Dot(n, _up) > PalmDown ? n : -_up;
    }
}
