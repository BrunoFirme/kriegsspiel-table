using System.Collections.Generic;
using System.Linq;
using Godot;

namespace KTable.App;

static class RaisedGroves
{
    public const int Budget = 16000;
    internal const int PerInstance = 16;
    const float HalfDiagonal = 0.71f;

    internal readonly record struct Grove(Vector3 Origin, TreeKind Kind, float[] Data);

    public static int Span(IReadOnlyDictionary<(int, int), RaisedChunk> chunks)
    {
        int span = 1;
        while (Batches(chunks, span) > Budget) span *= 2;
        return span;
    }

    public static int Batches(IReadOnlyDictionary<(int, int), RaisedChunk> chunks, int span)
    {
        var seen = new HashSet<((int, int), TreeKind)>();
        foreach (var (key, chunk) in chunks)
            foreach (var kind in chunk.Planted.Keys) seen.Add((Group(key, span), kind));
        return seen.Count;
    }

    public static IEnumerable<RaisedChunk.Drawn?> Nodes(IReadOnlyDictionary<(int, int), RaisedChunk> chunks, float chunkM, float upm, Material trees, int span = 0)
    {
        if (span <= 0) span = Span(chunks);
        var groves = new List<Grove>();
        foreach (var _ in Pack(chunks, chunkM * upm, span, groves)) yield return null;
        float overlapM = (span - 1) * chunkM * HalfDiagonal;
        foreach (bool near in new[] { true, false })
            foreach (var grove in groves)
            {
                var many = new MultiMesh
                {
                    TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = RaisedTrees.Mesh(grove.Kind, near), InstanceCount = grove.Data.Length / PerInstance,
                };
                many.Buffer = grove.Data;
                var node = new MultiMeshInstance3D
                {
                    Multimesh = many, Position = grove.Origin, MaterialOverride = trees, Name = "Trees",
                    CastShadow = near ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                };
                yield return near
                    ? new RaisedChunk.Drawn(node, 0, RaisedChunk.TreeNearM + overlapM)
                    : new RaisedChunk.Drawn(node, Mathf.Max(0, RaisedChunk.TreeNearM - overlapM), RaisedChunk.TreeFarM);
            }
    }

    internal static List<Grove> Packed(IReadOnlyDictionary<(int, int), RaisedChunk> chunks, float chunk, int span)
    {
        var groves = new List<Grove>();
        foreach (var _ in Pack(chunks, chunk, span, groves)) { }
        return groves;
    }

    static IEnumerable<bool> Pack(IReadOnlyDictionary<(int, int), RaisedChunk> chunks, float chunk, int span, List<Grove> into)
    {
        var counts = new Dictionary<((int, int), TreeKind), int>();
        foreach (var (key, piece) in chunks)
            foreach (var (kind, list) in piece.Planted)
            {
                var at = (Group(key, span), kind);
                counts[at] = counts.GetValueOrDefault(at) + list.Count;
            }
        var data = counts.ToDictionary(c => c.Key, c => new float[c.Value * PerInstance]);
        var filled = new Dictionary<((int, int), TreeKind), int>();
        foreach (var (key, piece) in chunks)
        {
            var group = Group(key, span);
            var shift = piece.Origin - Middle(group, span, chunk);
            foreach (var (kind, list) in piece.Planted)
            {
                var at = (group, kind);
                int from = filled.GetValueOrDefault(at);
                Pack(list, shift, data[at], from);
                filled[at] = from + list.Count;
            }
            yield return true;
        }
        into.AddRange(data.Select(d => new Grove(Middle(d.Key.Item1, span, chunk), d.Key.Item2, d.Value)));
    }

    static (int, int) Group((int X, int Z) key, int span) => ((int)System.Math.Floor(key.X / (double)span), (int)System.Math.Floor(key.Z / (double)span));

    static Vector3 Middle((int X, int Z) group, int span, float chunk) => new((group.X + 0.5f) * span * chunk, 0, (group.Z + 0.5f) * span * chunk);

    static void Pack(List<(Transform3D At, Color Tint)> list, Vector3 shift, float[] data, int from)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var (t, c) = list[i];
            var o = t.Origin + shift;
            int k = (from + i) * PerInstance;
            data[k] = t.Basis.X.X;
            data[k + 1] = t.Basis.Y.X;
            data[k + 2] = t.Basis.Z.X;
            data[k + 3] = o.X;
            data[k + 4] = t.Basis.X.Y;
            data[k + 5] = t.Basis.Y.Y;
            data[k + 6] = t.Basis.Z.Y;
            data[k + 7] = o.Y;
            data[k + 8] = t.Basis.X.Z;
            data[k + 9] = t.Basis.Y.Z;
            data[k + 10] = t.Basis.Z.Z;
            data[k + 11] = o.Z;
            data[k + 12] = c.R;
            data[k + 13] = c.G;
            data[k + 14] = c.B;
            data[k + 15] = c.A;
        }
    }
}
