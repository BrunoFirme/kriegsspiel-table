using KTable.Server;

namespace KTable.Client.Hand;

public static class CarryWire
{
    public const int MaxPieces = 500;
    const int FloatsPerPiece = 4;

    public static (string[] Layers, string[] Ids, float[] Poses) Pack(IReadOnlyList<CarriedPiece> pieces)
    {
        var poses = new float[pieces.Count * FloatsPerPiece];
        for (var i = 0; i < pieces.Count; i++)
        {
            var p = pieces[i];
            poses[i * FloatsPerPiece] = p.Dx;
            poses[i * FloatsPerPiece + 1] = p.Dz;
            poses[i * FloatsPerPiece + 2] = p.RotDeg;
            poses[i * FloatsPerPiece + 3] = p.Lift;
        }
        return (pieces.Select(p => p.Layer).ToArray(), pieces.Select(p => p.Id).ToArray(), poses);
    }

    public static List<CarriedPiece>? Unpack(string[]? layers, string[]? ids, float[]? poses)
    {
        if (layers == null || ids == null || poses == null) return null;
        if (ids.Length > MaxPieces || layers.Length != ids.Length || poses.Length != ids.Length * FloatsPerPiece || !poses.All(float.IsFinite)) return null;
        return Enumerable.Range(0, ids.Length)
            .Select(i => new CarriedPiece(layers[i], ids[i], poses[i * FloatsPerPiece], poses[i * FloatsPerPiece + 1], poses[i * FloatsPerPiece + 2], poses[i * FloatsPerPiece + 3]))
            .ToList();
    }

    const int FloatsPerSample = 7;

    public static (string[] Layers, string[] Ids, float[] Poses) PackSamples(IReadOnlyList<MotionSample> samples)
    {
        var poses = new float[samples.Count * FloatsPerSample];
        for (var i = 0; i < samples.Count; i++)
        {
            var (at, turn) = samples[i].Pose;
            var o = i * FloatsPerSample;
            poses[o] = at.X; poses[o + 1] = at.Y; poses[o + 2] = at.Z;
            poses[o + 3] = turn.X; poses[o + 4] = turn.Y; poses[o + 5] = turn.Z; poses[o + 6] = turn.W;
        }
        return (samples.Select(m => m.Piece.Layer).ToArray(), samples.Select(m => m.Piece.Id).ToArray(), poses);
    }

    public static List<MotionSample>? UnpackSamples(string[]? layers, string[]? ids, float[]? poses)
    {
        if (layers == null || ids == null || poses == null) return null;
        if (ids.Length > MaxPieces || layers.Length != ids.Length || poses.Length != ids.Length * FloatsPerSample || !poses.All(float.IsFinite)) return null;
        var samples = new List<MotionSample>(ids.Length);
        for (var i = 0; i < ids.Length; i++)
        {
            var o = i * FloatsPerSample;
            var turn = new System.Numerics.Quaternion(poses[o + 3], poses[o + 4], poses[o + 5], poses[o + 6]);
            if (turn.LengthSquared() < 0.5f) return null;
            samples.Add(new MotionSample(new PieceKey(layers[i], ids[i]), new Pose(new System.Numerics.Vector3(poses[o], poses[o + 1], poses[o + 2]), System.Numerics.Quaternion.Normalize(turn))));
        }
        return samples;
    }

    public static bool IsFinite(HandAt at) => float.IsFinite(at.X) && float.IsFinite(at.Z) && float.IsFinite(at.Y);
}
