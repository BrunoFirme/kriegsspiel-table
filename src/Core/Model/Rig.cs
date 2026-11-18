using System.Numerics;

namespace KTable.Core.Model;

public sealed record RigBone(string Name, int Parent, Vector3 Position, Quaternion Rotation, float Scale);

public enum RigChannel : byte { Position, Rotation }

public sealed record RigTrack(int Bone, RigChannel Channel, float[] Times, float[] Values)
{
    public int Width => Channel == RigChannel.Rotation ? 4 : 3;
}

public sealed record RigClip(string Name, float Length, List<RigTrack> Tracks);

public readonly record struct RigPlace(Vector3 Offset, Quaternion Turn, float Scale)
{
    public static readonly RigPlace Same = new(Vector3.Zero, Quaternion.Identity, 1);
    public Vector3 Point(Vector3 v) => Offset + Vector3.Transform(v * Scale, Turn);
    public Vector3 Direction(Vector3 v) => Vector3.Transform(v, Turn);
    public Vector3 Back(Vector3 v) => Vector3.Transform(v, Quaternion.Inverse(Turn));
    public RigPlace Then(RigPlace outer) => new(outer.Point(Offset), Quaternion.Normalize(outer.Turn * Turn), outer.Scale * Scale);
}

public sealed class Rig
{
    public const int Influences = 4;

    public List<RigBone> Bones { get; init; } = new();
    public RigPlace Root { get; set; } = RigPlace.Same;
    public Vector3[] Points { get; init; } = [];
    public Vector3[] Normals { get; init; } = [];
    public Vector2[] Coords { get; init; } = [];
    public ushort[] Joints { get; init; } = [];
    public float[] Weights { get; init; } = [];
    public int[] Triangles { get; init; } = [];
    public Dictionary<string, int> Humanoid { get; init; } = new();
    public List<RigClip> Clips { get; init; } = new();

    public (Vector3[] Points, Quaternion[] Turns, float[] Scales) Rest()
    {
        int n = Bones.Count;
        var points = new Vector3[n];
        var turns = new Quaternion[n];
        var scales = new float[n];
        for (int i = 0; i < n; i++)
        {
            var b = Bones[i];
            if (b.Parent < 0) (points[i], turns[i], scales[i]) = (b.Position, b.Rotation, b.Scale);
            else
            {
                int p = b.Parent;
                points[i] = points[p] + Vector3.Transform(b.Position * scales[p], turns[p]);
                turns[i] = Quaternion.Normalize(turns[p] * b.Rotation);
                scales[i] = scales[p] * b.Scale;
            }
        }
        return (points, turns, scales);
    }

    public (Vector3[] Points, Quaternion[] Turns) Placed(Vector3[] localPoints, Quaternion[] localTurns)
    {
        int n = Bones.Count;
        var points = new Vector3[n];
        var turns = new Quaternion[n];
        var scales = new float[n];
        for (int i = 0; i < n; i++)
        {
            var b = Bones[i];
            if (b.Parent < 0) (points[i], turns[i], scales[i]) = (localPoints[i], localTurns[i], b.Scale);
            else
            {
                int p = b.Parent;
                points[i] = points[p] + Vector3.Transform(localPoints[i] * scales[p], turns[p]);
                turns[i] = Quaternion.Normalize(turns[p] * localTurns[i]);
                scales[i] = scales[p] * b.Scale;
            }
        }
        return (points, turns);
    }

    public (Vector3[] Points, Vector3[] Normals) Posed(Vector3[] localPoints, Quaternion[] localTurns)
    {
        var (restPoints, restTurns, _) = Rest();
        int n = Bones.Count;
        var (points, turns) = Placed(localPoints, localTurns);
        var moves = new Quaternion[n];
        for (int i = 0; i < n; i++) moves[i] = turns[i] * Quaternion.Inverse(restTurns[i]);
        var skinned = new Vector3[Points.Length];
        var normals = new Vector3[Points.Length];
        for (int v = 0; v < Points.Length; v++)
        {
            Vector3 sum = Vector3.Zero, facing = Vector3.Zero;
            float total = 0;
            for (int k = 0; k < Influences; k++)
            {
                float w = Weights[v * Influences + k];
                if (w <= 0) continue;
                int j = Joints[v * Influences + k];
                sum += w * (points[j] + Vector3.Transform(Points[v] - restPoints[j], moves[j]));
                facing += w * Vector3.Transform(Normals[v], moves[j]);
                total += w;
            }
            skinned[v] = Root.Point(total > 0 ? sum / total : Points[v]);
            var turned = Root.Direction(total > 0 ? facing : Normals[v]);
            normals[v] = turned.LengthSquared() > 1e-12f ? Vector3.Normalize(turned) : Vector3.UnitY;
        }
        return (skinned, normals);
    }

    public (Vector3[] Points, Quaternion[] Turns) RestLocal() =>
        (Bones.Select(b => b.Position).ToArray(), Bones.Select(b => b.Rotation).ToArray());

    static readonly string[] Aside = ["back", "strafe", "left", "right", "jump", "sit", "lie", "melee", "ranged", "kneel", "cower", "brace", "throw", "shove", "cheer", "yield", "guard", "carry"];

    public RigClip? Clip(string motion, bool aside = true) => Clips
        .Select(c => (Clip: c, Name: c.Name[(c.Name.LastIndexOf('|') + 1)..]))
        .Where(c => c.Name.Contains(motion, StringComparison.OrdinalIgnoreCase) && !(aside && Aside.Any(a => c.Name.Contains(a, StringComparison.OrdinalIgnoreCase))))
        .OrderBy(c => c.Name.Equals(motion, StringComparison.OrdinalIgnoreCase) ? 0 : c.Name.StartsWith(motion, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
        .ThenBy(c => c.Name.Length)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .Select(c => c.Clip)
        .FirstOrDefault();

    const string Strafe = "strafe";

    public RigClip? Toward(string motion, string way) => Clips
        .Select(c => (Clip: c, Name: c.Name[(c.Name.LastIndexOf('|') + 1)..]))
        .Where(c => c.Name.Contains(motion, StringComparison.OrdinalIgnoreCase) && c.Name.Contains(way, StringComparison.OrdinalIgnoreCase)
            && !Aside.Any(a => a != way && a != Strafe && c.Name.Contains(a, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(c => c.Name.Length)
        .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
        .Select(c => c.Clip)
        .FirstOrDefault();

    public void Sample(RigClip clip, float time, Vector3[] points, Quaternion[] turns)
    {
        foreach (var t in clip.Tracks)
        {
            if (t.Bone < 0 || t.Bone >= Bones.Count || t.Times.Length == 0) continue;
            int hi = Array.BinarySearch(t.Times, time);
            if (hi < 0) hi = ~hi;
            int lo = Math.Max(0, hi - 1);
            hi = Math.Min(hi, t.Times.Length - 1);
            float span = t.Times[hi] - t.Times[lo], f = span > 0 ? Math.Clamp((time - t.Times[lo]) / span, 0, 1) : 0;
            if (t.Channel == RigChannel.Rotation)
            {
                var a = new Quaternion(t.Values[lo * 4], t.Values[lo * 4 + 1], t.Values[lo * 4 + 2], t.Values[lo * 4 + 3]);
                var b = new Quaternion(t.Values[hi * 4], t.Values[hi * 4 + 1], t.Values[hi * 4 + 2], t.Values[hi * 4 + 3]);
                turns[t.Bone] = Quaternion.Normalize(Quaternion.Slerp(a, b, f));
            }
            else
            {
                var a = new Vector3(t.Values[lo * 3], t.Values[lo * 3 + 1], t.Values[lo * 3 + 2]);
                var b = new Vector3(t.Values[hi * 3], t.Values[hi * 3 + 1], t.Values[hi * 3 + 2]);
                points[t.Bone] = Vector3.Lerp(a, b, f);
            }
        }
    }
}
