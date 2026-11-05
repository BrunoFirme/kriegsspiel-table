using AssetsTools.NET;

namespace KTable.Converter.Bundles;

public sealed record BoneInfluence(double[] Weights, int[] Bones);

public sealed class UnityMesh
{
    const int StreamAlignment = 16;
    const int DimensionMask = 0xF;
    const int PositionChannel = 0;
    const int ColourChannel = 2;
    const int ColourFormat = 2;
    const int ColourDimension = 4;
    const int UvChannelBefore2018 = 3;
    const int UvChannelFrom2018 = 4;
    const int TrianglesTopology = 0;
    const int SixteenBitIndices = 0;
    const int Uncompressed = 0;

    static readonly int[] SizesBefore2017 = { 4, 2, 1, 1, 4 };
    static readonly int[] SizesBefore2019 = { 4, 2, 1, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };
    static readonly int[] SizesFrom2019 = { 4, 2, 1, 1, 2, 2, 1, 1, 2, 2, 4, 4 };
    static readonly bool[] HalfBefore2017 = { false, true, false, false, false };
    static readonly bool[] HalfOtherwise = { false, true, false, false, false, false, false, false, false, false, false, false, false };

    public int VertexCount { get; private init; }
    public double[] Positions { get; private init; } = Array.Empty<double>();
    public double[] Uvs { get; private init; } = Array.Empty<double>();
    public List<int[]> SubMeshes { get; private init; } = new();
    public BoneInfluence[] Skin { get; private init; } = Array.Empty<BoneInfluence>();
    public Mat4[] BindPose { get; private init; } = Array.Empty<Mat4>();

    public static UnityMesh? Read(AssetTypeValueField mesh, int unityMajor, Func<AssetTypeValueField, byte[]?> sideFile)
    {
        if (mesh["m_MeshCompression"].AsInt != Uncompressed) return null;
        var data = mesh["m_VertexData"];
        int count = data["m_VertexCount"].AsInt;
        if (count <= 0) return null;
        var bytes = data["m_DataSize"].AsByteArray;
        if (bytes.Length == 0) bytes = sideFile(mesh["m_StreamData"]) ?? Array.Empty<byte>();
        if (bytes.Length == 0) return null;

        var channels = data["m_Channels.Array"].Children;
        var strides = Strides(channels, unityMajor);
        var offsets = StreamOffsets(strides, count);

        int uvChannel = unityMajor >= 2018 ? UvChannelFrom2018 : UvChannelBefore2018;
        var positions = Channel(channels, PositionChannel, bytes, strides, offsets, count, unityMajor, 3);
        if (positions == null) return null;
        var uvs = Channel(channels, uvChannel, bytes, strides, offsets, count, unityMajor, 2) ?? new double[count * 2];

        return new UnityMesh
        {
            VertexCount = count,
            Positions = positions,
            Uvs = uvs,
            SubMeshes = Triangles(mesh),
            Skin = Influences(mesh),
            BindPose = Poses(mesh),
        };
    }

    static int[] Sizes(int unityMajor) =>
        unityMajor < 2017 ? SizesBefore2017 : unityMajor < 2019 ? SizesBefore2019 : SizesFrom2019;

    static bool IsHalf(int unityMajor, int format) =>
        (unityMajor < 2017 ? HalfBefore2017 : HalfOtherwise) is { } table && format >= 0 && format < table.Length && table[format];

    static int ComponentSize(int unityMajor, int format)
    {
        var sizes = Sizes(unityMajor);
        return format >= 0 && format < sizes.Length ? sizes[format] : 0;
    }

    static int Dimension(AssetTypeValueField channel, int index, int unityMajor)
    {
        int dimension = channel["dimension"].AsInt & DimensionMask;
        bool oldColour = unityMajor < 2018 && index == ColourChannel && channel["format"].AsInt == ColourFormat;
        return oldColour ? ColourDimension : dimension;
    }

    static int[] Strides(List<AssetTypeValueField> channels, int unityMajor)
    {
        int streams = 1 + channels.Max(c => c["stream"].AsInt);
        var strides = new int[streams];
        for (int index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            if ((channel["dimension"].AsInt & DimensionMask) == 0) continue;
            strides[channel["stream"].AsInt] += Dimension(channel, index, unityMajor) * ComponentSize(unityMajor, channel["format"].AsInt);
        }
        return strides;
    }

    static long[] StreamOffsets(int[] strides, int count)
    {
        var offsets = new long[strides.Length];
        long at = 0;
        for (int s = 0; s < strides.Length; s++)
        {
            offsets[s] = at;
            at += (long)count * strides[s];
            at = (at + StreamAlignment - 1) & ~((long)StreamAlignment - 1);
        }
        return offsets;
    }

    static double[]? Channel(List<AssetTypeValueField> channels, int index, byte[] bytes, int[] strides, long[] offsets, int count, int unityMajor, int want)
    {
        if (index >= channels.Count) return null;
        var channel = channels[index];
        int dimension = Dimension(channel, index, unityMajor);
        if (dimension == 0) return null;
        int format = channel["format"].AsInt;
        int size = ComponentSize(unityMajor, format);
        if (size == 0) return null;
        int stream = channel["stream"].AsInt;
        if (stream >= strides.Length || strides[stream] == 0) return null;
        bool half = IsHalf(unityMajor, format);
        if (size != sizeof(float) && !half) return null;

        var values = new double[(long)count * want <= int.MaxValue ? count * want : 0];
        if (values.Length == 0) return null;
        long start = offsets[stream] + channel["offset"].AsInt;
        for (int vertex = 0; vertex < count; vertex++)
        {
            long at = start + (long)vertex * strides[stream];
            for (int part = 0; part < want; part++)
            {
                if (part >= dimension) break;
                long here = at + (long)part * size;
                if (here + size > bytes.Length) return null;
                values[vertex * want + part] = half
                    ? (double)BitConverter.ToHalf(bytes, (int)here)
                    : BitConverter.ToSingle(bytes, (int)here);
            }
        }
        return values;
    }

    static List<int[]> Triangles(AssetTypeValueField mesh)
    {
        var indices = mesh["m_IndexBuffer.Array"].AsByteArray;
        bool sixteen = mesh["m_IndexFormat"].IsDummy || mesh["m_IndexFormat"].AsInt == SixteenBitIndices;
        int step = sixteen ? sizeof(ushort) : sizeof(uint);
        var all = new List<int[]>();
        foreach (var sub in mesh["m_SubMeshes.Array"].Children)
        {
            int first = sub["firstByte"].AsInt / sizeof(ushort);
            if (!sixteen) first /= sizeof(ushort);
            int many = sub["indexCount"].AsInt;
            if (sub["topology"].AsInt != TrianglesTopology || many < 3) { all.Add(Array.Empty<int>()); continue; }
            var triangles = new List<int>(many);
            for (int n = 0; n + 2 < many; n += 3)
            {
                long at = (long)(first + n) * step;
                if (at + 3L * step > indices.Length) break;
                for (int corner = 0; corner < 3; corner++)
                {
                    long here = at + (long)corner * step;
                    triangles.Add(sixteen ? BitConverter.ToUInt16(indices, (int)here) : (int)BitConverter.ToUInt32(indices, (int)here));
                }
            }
            all.Add(triangles.ToArray());
        }
        return all;
    }

    static BoneInfluence[] Influences(AssetTypeValueField mesh) =>
        mesh["m_Skin.Array"].Children.Select(s => new BoneInfluence(
            new[] { s["weight[0]"].AsDouble, s["weight[1]"].AsDouble, s["weight[2]"].AsDouble, s["weight[3]"].AsDouble },
            new[] { s["boneIndex[0]"].AsInt, s["boneIndex[1]"].AsInt, s["boneIndex[2]"].AsInt, s["boneIndex[3]"].AsInt })).ToArray();

    static Mat4[] Poses(AssetTypeValueField mesh) =>
        mesh["m_BindPose.Array"].Children.Select(m => Mat4.Rows(new[]
        {
            m["e00"].AsDouble, m["e01"].AsDouble, m["e02"].AsDouble, m["e03"].AsDouble,
            m["e10"].AsDouble, m["e11"].AsDouble, m["e12"].AsDouble, m["e13"].AsDouble,
            m["e20"].AsDouble, m["e21"].AsDouble, m["e22"].AsDouble, m["e23"].AsDouble,
            m["e30"].AsDouble, m["e31"].AsDouble, m["e32"].AsDouble, m["e33"].AsDouble,
        })).ToArray();
}
