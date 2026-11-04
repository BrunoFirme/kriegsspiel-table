using System.Security.Cryptography;

namespace KTable.Core.Model;

public sealed class ReliefIndex
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public List<string> Heights { get; set; } = new();
    public List<string> Ground { get; set; } = new();
}

public static class GroundKinds
{
    public const byte None = 0;

    public static readonly IReadOnlyList<(string Name, string Color)> All =
    [
        ("none", "#00000000"),
        ("grass", "#A9BE94"),
        ("field", "#E3CDB2"),
        ("wood", "#93AB76"),
        ("marsh", "#B3C3A7"),
        ("water", "#9DB9C9"),
        ("road", "#D8B57A"),
        ("earth", "#CBAE88"),
        ("rock", "#B9AE9C"),
        ("sand", "#E6D3A1"),
        ("snow", "#F1F0EA"),
    ];

    public static byte? Of(string name)
    {
        for (int i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) return (byte)i;
        return null;
    }

    public static string Name(byte kind) => kind < All.Count ? All[kind].Name : "none";

    static readonly string[] Ancient =
        ["#00000000", "#C9C08A", "#D9B062", "#8C9A5A", "#A9AE80", "#7FA393", "#B5442E", "#B98352", "#A58A63", "#E3C680", "#EFE7D4"];

    static readonly string[] Modern =
        ["#00000000", "#DDE8C0", "#F1E8B8", "#9AC57A", "#E2ECDF", "#A9D2EC", "#E8643C", "#E4D3B2", "#D2CEC6", "#F1E3B0", "#FAFBFC"];

    public static string Color(byte kind, string? style)
    {
        if (kind >= All.Count) return All[None].Color;
        return MapStyles.Of(style) switch
        {
            MapStyles.Ancient => Ancient[kind],
            MapStyles.Modern => Modern[kind],
            _ => All[kind].Color,
        };
    }
}

public static class MapStyles
{
    public const string Survey = "", Ancient = "ancient", Modern = "modern";

    public static readonly IReadOnlyList<(string Id, string Name)> All =
    [
        (Ancient, "Ancient"),
        (Survey, "18th-century survey"),
        (Modern, "Modern topographic"),
    ];

    public static readonly IReadOnlyList<string> ShaderOrder = [Survey, Ancient, Modern];

    public static string Of(string? style) => All.Any(s => s.Id == style?.Trim().ToLowerInvariant()) ? style!.Trim().ToLowerInvariant() : Survey;

    public static int Index(string? style) => ShaderOrder.ToList().IndexOf(Of(style));
}

public sealed class ReliefGrid
{
    public const int Chunk = 64;
    public const int MaxSide = 2049;
    public const int PaintedSide = 513;

    public readonly int Cols, Rows;
    public readonly float[] H;
    public readonly byte[] Kind;

    public ReliefGrid(int cols, int rows)
    {
        if (cols < 2 || rows < 2 || cols > MaxSide || rows > MaxSide) throw new InvalidDataException($"a relief grid of {cols} × {rows} is out of range");
        Cols = cols;
        Rows = rows;
        H = new float[cols * rows];
        Kind = new byte[cols * rows];
    }

    public static ReliefGrid Flat(double width, double height)
    {
        double longest = Math.Max(width, height);
        int cols = Math.Max(2, (int)Math.Round((PaintedSide - 1) * width / longest) + 1);
        int rows = Math.Max(2, (int)Math.Round((PaintedSide - 1) * height / longest) + 1);
        return new ReliefGrid(cols, rows);
    }

    public ReliefGrid Copy()
    {
        var c = new ReliefGrid(Cols, Rows);
        Array.Copy(H, c.H, H.Length);
        Array.Copy(Kind, c.Kind, Kind.Length);
        return c;
    }

    public int ChunksAcross => (Cols + Chunk - 1) / Chunk;
    public int ChunksDown => (Rows + Chunk - 1) / Chunk;

    public float HeightAt(double u, double v)
    {
        double x = Math.Clamp(u, 0, 1) * (Cols - 1), y = Math.Clamp(v, 0, 1) * (Rows - 1);
        int x0 = Math.Min((int)x, Cols - 2), y0 = Math.Min((int)y, Rows - 2);
        float fx = (float)(x - x0), fy = (float)(y - y0);
        int i = y0 * Cols + x0;
        float top = H[i] + (H[i + 1] - H[i]) * fx;
        float bottom = H[i + Cols] + (H[i + Cols + 1] - H[i + Cols]) * fx;
        return top + (bottom - top) * fy;
    }

    public byte KindAt(double u, double v)
    {
        int x = (int)Math.Round(Math.Clamp(u, 0, 1) * (Cols - 1)), y = (int)Math.Round(Math.Clamp(v, 0, 1) * (Rows - 1));
        return Kind[y * Cols + x];
    }

    public float Highest => H.Length == 0 ? 0 : H.Max();
    public bool Painted => Kind.Any(k => k != GroundKinds.None);
}

public static class ReliefFiles
{
    public const string Folder = "relief";
    const string HeaderExt = ".json", ChunkExt = ".bin";

    public static string HeaderRel(string sha) => Folder + "/" + sha + HeaderExt;
    public static string ChunkRel(string sha) => Folder + "/" + sha + ChunkExt;

    public static bool IsReliefPath(string path) =>
        System.Text.RegularExpressions.Regex.IsMatch(path, @"^relief/[0-9a-f]{64}\.(json|bin)$");

    public static IEnumerable<string> Files(string sha, ReliefIndex index) =>
        new[] { HeaderRel(sha) }.Concat(index.Heights.Concat(index.Ground).Where(c => c.Length > 0).Distinct().Select(ChunkRel));

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static (string sha, List<string> written) Save(string packageDir, ReliefGrid g)
    {
        var written = new List<string>();
        var index = new ReliefIndex { Cols = g.Cols, Rows = g.Rows };
        for (int cy = 0; cy < g.ChunksDown; cy++)
            for (int cx = 0; cx < g.ChunksAcross; cx++)
            {
                index.Heights.Add(Put(packageDir, HeightChunk(g, cx, cy), written));
                index.Ground.Add(Put(packageDir, KindChunk(g, cx, cy), written));
            }
        var header = Json.SerializeBytes(index);
        var sha = Sha(header);
        var path = Path.Combine(packageDir, Folder, sha + HeaderExt);
        if (!File.Exists(path)) { Json.WriteAtomic(path, header); written.Add(HeaderRel(sha)); }
        return (sha, written);
    }

    static string Put(string packageDir, byte[]? plain, List<string> written)
    {
        if (plain == null) return "";
        var bytes = Json.Packed(plain);
        var sha = Sha(bytes);
        var path = Path.Combine(packageDir, Folder, sha + ChunkExt);
        if (!File.Exists(path)) { Json.WriteAtomic(path, bytes); written.Add(ChunkRel(sha)); }
        return sha;
    }

    static byte[]? HeightChunk(ReliefGrid g, int cx, int cy)
    {
        var bytes = new byte[ReliefGrid.Chunk * ReliefGrid.Chunk * sizeof(float)];
        bool any = false;
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                float h = g.H[gy * g.Cols + gx];
                if (h != 0) any = true;
                BitConverter.TryWriteBytes(bytes.AsSpan((y * ReliefGrid.Chunk + x) * sizeof(float)), h);
            }
        return any ? bytes : null;
    }

    static byte[]? KindChunk(ReliefGrid g, int cx, int cy)
    {
        var bytes = new byte[ReliefGrid.Chunk * ReliefGrid.Chunk];
        bool any = false;
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                byte k = g.Kind[gy * g.Cols + gx];
                if (k != 0) any = true;
                bytes[y * ReliefGrid.Chunk + x] = k;
            }
        return any ? bytes : null;
    }

    public static ReliefIndex LoadIndex(string headerPath)
    {
        var index = Json.Load<ReliefIndex>(headerPath);
        int chunks = ((index.Cols + ReliefGrid.Chunk - 1) / ReliefGrid.Chunk) * ((index.Rows + ReliefGrid.Chunk - 1) / ReliefGrid.Chunk);
        if (index.Heights.Count != chunks || index.Ground.Count != chunks) throw new InvalidDataException("the relief's chunk list does not match its size");
        if (index.Heights.Concat(index.Ground).Any(c => c.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(c, "^[0-9a-f]{64}$"))) throw new InvalidDataException("the relief names a chunk wrongly");
        return index;
    }

    public static ReliefGrid Load(ReliefIndex index, Func<string, string?> chunkPath)
    {
        var g = new ReliefGrid(index.Cols, index.Rows);
        int i = 0;
        for (int cy = 0; cy < g.ChunksDown; cy++)
            for (int cx = 0; cx < g.ChunksAcross; cx++, i++)
            {
                if (index.Heights[i].Length > 0) ReadHeights(g, cx, cy, Chunk(index.Heights[i], chunkPath, ReliefGrid.Chunk * ReliefGrid.Chunk * sizeof(float)));
                if (index.Ground[i].Length > 0) ReadKinds(g, cx, cy, Chunk(index.Ground[i], chunkPath, ReliefGrid.Chunk * ReliefGrid.Chunk));
            }
        return g;
    }

    static byte[] Chunk(string sha, Func<string, string?> chunkPath, int size)
    {
        var path = chunkPath(ChunkRel(sha)) ?? throw new FileNotFoundException("a relief chunk is missing", sha);
        var bytes = Json.Unpacked(File.ReadAllBytes(path));
        if (bytes.Length != size) throw new InvalidDataException("a relief chunk has the wrong size");
        return bytes;
    }

    static void ReadHeights(ReliefGrid g, int cx, int cy, byte[] b)
    {
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                float h = BitConverter.ToSingle(b, (y * ReliefGrid.Chunk + x) * sizeof(float));
                g.H[gy * g.Cols + gx] = float.IsFinite(h) ? h : 0;
            }
    }

    static void ReadKinds(ReliefGrid g, int cx, int cy, byte[] b)
    {
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                byte k = b[y * ReliefGrid.Chunk + x];
                g.Kind[gy * g.Cols + gx] = k < GroundKinds.All.Count ? k : GroundKinds.None;
            }
    }
}
