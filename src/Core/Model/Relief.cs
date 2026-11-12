using System.Security.Cryptography;

namespace KTable.Core.Model;

public sealed class ReliefIndex
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public List<string> Heights { get; set; } = new();
    public List<string> Ground { get; set; } = new();
    public List<string>? Seeds { get; set; }
    public List<string>? Details { get; set; }
}

public static class GroundKinds
{
    public const byte None = 0, Marsh = 4, Water = 5, Road = 6, Bridge = 11, Town = 12;

    public static readonly IReadOnlyList<(string Name, string Color)> All =
    [
        ("none", "#00000000"),
        ("grass", "#C9CDAA"),
        ("field", "#EDE7DA"),
        ("wood", "#A1A98A"),
        ("marsh", "#C2CBB0"),
        ("water", "#B4C4CC"),
        ("road", "#D8C3A8"),
        ("earth", "#E6DBC6"),
        ("rock", "#E3D5CB"),
        ("sand", "#EBDFC6"),
        ("snow", "#F4F2EC"),
        ("bridge", "#D8C3A8"),
        ("town", "#E7DFD4"),
    ];

    public static byte? Of(string name)
    {
        for (int i = 0; i < All.Count; i++)
            if (string.Equals(All[i].Name, name?.Trim(), StringComparison.OrdinalIgnoreCase)) return (byte)i;
        return null;
    }

    public static string Name(byte kind) => kind < All.Count ? All[kind].Name : "none";

    public static IEnumerable<byte> Brushes => Enumerable.Range(1, All.Count - 1).Select(i => (byte)i).Where(k => k is not (Road or Bridge or Town));

    public static byte Laid(byte under, byte brush) => brush switch
    {
        Road when under is Water or Bridge => Bridge,
        Town when under is Road or Bridge or Water => under,
        _ => brush,
    };

    public static byte BrushOf(byte kind) => kind == Bridge ? Road : kind;

    public const string NeedsScale = "Set the map's scale first";
    public const int LevelSteps = 15;

    static readonly GroundParameter Plots = new("Plots", "Big open fields in strips, or small plots bounded by hedges, fences and walls", 50);
    static readonly GroundParameter Buildings = new("Buildings", "None, lone farms, or hamlets", 25);
    static readonly GroundParameter Trees = new("Trees", "Open wood or closed forest", 75);
    static readonly GroundParameter Houses = new("Houses", "Scattered houses with gardens, or packed streets", 50);
    static readonly GroundParameter Walls = new("Walls", "None, a wall with towers, or a bastioned fort", 0);
    static readonly GroundParameter RoadClass = new("Road", "A track, a country road, or a chaussée; a bridge keeps its road's", 50);

    public static IReadOnlyList<GroundParameter> Parameters(byte kind) => Name(kind) switch
    {
        "wood" => [Trees],
        "town" => [Houses, Walls],
        "road" or "bridge" => [RoadClass],
        _ => [],
    };

    public static GroundParameter? Parameter(byte kind, string name) =>
        Parameters(kind).FirstOrDefault(p => string.Equals(p.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public static byte Levels(int first, int second) => (byte)(Nibble(first) | Nibble(second) << 4);

    public static byte DefaultLevels(byte kind)
    {
        var p = Parameters(kind);
        return Levels(p.Count > 0 ? p[0].Default : 0, p.Count > 1 ? p[1].Default : 0);
    }

    public static (int First, int Second) LevelsOf(byte detail) => (Percent(detail & LevelSteps), Percent(detail >> 4));

    static int Nibble(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * LevelSteps);

    static int Percent(int nibble) => (int)Math.Round(nibble * 100.0 / LevelSteps);

    static readonly string[] Ancient =
        ["#00000000", "#D6BE96", "#D6BE96", "#8E9A6A", "#C4C4A0", "#9DB5AC", "#9A4A2C", "#C9AE86", "#C9A07E", "#DCCBA4", "#F2EDE0", "#9A4A2C", "#A0563C"];

    static readonly string[] Modern =
        ["#00000000", "#F3EFE0", "#F3EFE0", "#CFD9C4", "#E6EBE4", "#D6E0EA", "#F3EFE0", "#EFE4D2", "#E9E6DC", "#F0E6D2", "#FBFAF6", "#F3EFE0", "#D9D8D0"];

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

public sealed record GroundParameter(string Name, string Tip, int Default);

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
    public readonly byte[] Salt;
    public readonly byte[] Detail;

    public ReliefGrid(int cols, int rows)
    {
        if (cols < 2 || rows < 2 || cols > MaxSide || rows > MaxSide) throw new InvalidDataException($"a relief grid of {cols} × {rows} is out of range");
        Cols = cols;
        Rows = rows;
        H = new float[cols * rows];
        Kind = new byte[cols * rows];
        Salt = new byte[cols * rows];
        Detail = new byte[cols * rows];
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
        Array.Copy(Salt, c.Salt, Salt.Length);
        Array.Copy(Detail, c.Detail, Detail.Length);
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

    public byte KindAt(double u, double v) => Kind[CellAt(u, v)];

    public byte DetailAt(double u, double v) => Detail[CellAt(u, v)];

    int CellAt(double u, double v)
    {
        int x = (int)Math.Round(Math.Clamp(u, 0, 1) * (Cols - 1)), y = (int)Math.Round(Math.Clamp(v, 0, 1) * (Rows - 1));
        return y * Cols + x;
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
        new[] { HeaderRel(sha) }.Concat(Chunks(index).Where(c => c.Length > 0).Distinct().Select(ChunkRel));

    static IEnumerable<string> Chunks(ReliefIndex index) => index.Heights.Concat(index.Ground).Concat(index.Seeds ?? new()).Concat(index.Details ?? new());

    public static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    public static (string sha, List<string> written) Save(string packageDir, ReliefGrid g)
    {
        var written = new List<string>();
        var index = new ReliefIndex { Cols = g.Cols, Rows = g.Rows };
        var seeds = new List<string>();
        var details = new List<string>();
        for (int cy = 0; cy < g.ChunksDown; cy++)
            for (int cx = 0; cx < g.ChunksAcross; cx++)
            {
                index.Heights.Add(Put(packageDir, HeightChunk(g, cx, cy), written));
                index.Ground.Add(Put(packageDir, ByteChunk(g, g.Kind, cx, cy), written));
                seeds.Add(Put(packageDir, ByteChunk(g, g.Salt, cx, cy), written));
                details.Add(Put(packageDir, ByteChunk(g, g.Detail, cx, cy), written));
            }
        if (seeds.Any(c => c.Length > 0)) index.Seeds = seeds;
        if (details.Any(c => c.Length > 0)) index.Details = details;
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

    static byte[]? ByteChunk(ReliefGrid g, byte[] layer, int cx, int cy)
    {
        var bytes = new byte[ReliefGrid.Chunk * ReliefGrid.Chunk];
        bool any = false;
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                byte k = layer[gy * g.Cols + gx];
                if (k != 0) any = true;
                bytes[y * ReliefGrid.Chunk + x] = k;
            }
        return any ? bytes : null;
    }

    public static ReliefIndex LoadIndex(string headerPath)
    {
        var index = Json.Load<ReliefIndex>(headerPath);
        int chunks = ((index.Cols + ReliefGrid.Chunk - 1) / ReliefGrid.Chunk) * ((index.Rows + ReliefGrid.Chunk - 1) / ReliefGrid.Chunk);
        if (index.Heights.Count != chunks || index.Ground.Count != chunks || (index.Seeds?.Count ?? chunks) != chunks || (index.Details?.Count ?? chunks) != chunks)
            throw new InvalidDataException("the relief's chunk list does not match its size");
        if (Chunks(index).Any(c => c.Length > 0 && !System.Text.RegularExpressions.Regex.IsMatch(c, "^[0-9a-f]{64}$"))) throw new InvalidDataException("the relief names a chunk wrongly");
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
                if (index.Ground[i].Length > 0) ReadBytes(g, g.Kind, cx, cy, Chunk(index.Ground[i], chunkPath, ReliefGrid.Chunk * ReliefGrid.Chunk), (byte)(GroundKinds.All.Count - 1));
                if (index.Seeds is { } seeds && seeds[i].Length > 0) ReadBytes(g, g.Salt, cx, cy, Chunk(seeds[i], chunkPath, ReliefGrid.Chunk * ReliefGrid.Chunk), byte.MaxValue);
                if (index.Details is { } details && details[i].Length > 0) ReadBytes(g, g.Detail, cx, cy, Chunk(details[i], chunkPath, ReliefGrid.Chunk * ReliefGrid.Chunk), byte.MaxValue);
            }
        if (index.Details == null)
            for (int c = 0; c < g.Detail.Length; c++) g.Detail[c] = GroundKinds.DefaultLevels(g.Kind[c]);
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

    static void ReadBytes(ReliefGrid g, byte[] layer, int cx, int cy, byte[] b, byte most)
    {
        for (int y = 0; y < ReliefGrid.Chunk; y++)
            for (int x = 0; x < ReliefGrid.Chunk; x++)
            {
                int gx = cx * ReliefGrid.Chunk + x, gy = cy * ReliefGrid.Chunk + y;
                if (gx >= g.Cols || gy >= g.Rows) continue;
                byte k = b[y * ReliefGrid.Chunk + x];
                layer[gy * g.Cols + gx] = k <= most ? k : (byte)0;
            }
    }
}
