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
        ("grass", "#7B8463"),
        ("field", "#B9A77C"),
        ("wood", "#46553D"),
        ("marsh", "#6E8272"),
        ("water", "#58758A"),
        ("road", "#BDAA85"),
        ("earth", "#98805F"),
        ("rock", "#979085"),
        ("sand", "#C6B48A"),
        ("snow", "#E9E7DE"),
        ("bridge", "#BDAA85"),
        ("town", "#B5A58F"),
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
        ["#00000000", "#9A9468", "#B3975C", "#586A44", "#85937A", "#4A7480", "#7E3822", "#9C7A50", "#A56E58", "#C2A468", "#E8DFC9", "#7E3822", "#84432F"];

    static readonly string[] Modern =
        ["#00000000", "#B4BE9C", "#D2CAAE", "#86A078", "#AFC4B8", "#8AA0BE", "#D2CAAE", "#C2AE88", "#B8B2A2", "#D2C08A", "#F2F1EA", "#D2CAAE", "#AEAA9A"];

    static readonly string[] Staff =
        ["#00000000", "#BEBBA6", "#CFCBB6", "#8E8D80", "#AEADA0", "#A9ADA6", "#CFCBB6", "#BDB8A2", "#A6A498", "#C7C2AB", "#E9E8DF", "#CFCBB6", "#ADAB9F"];

    public static string Color(byte kind, string? style)
    {
        if (kind >= All.Count) return All[None].Color;
        return MapStyles.Of(style) switch
        {
            MapStyles.Ancient => Ancient[kind],
            MapStyles.Modern => Modern[kind],
            MapStyles.Staff => Staff[kind],
            _ => All[kind].Color,
        };
    }
}

public sealed record GroundParameter(string Name, string Tip, int Default);

public static class MapStyles
{
    public const string Survey = "", Ancient = "ancient", Modern = "modern", Staff = "staff";

    public static readonly IReadOnlyList<(string Id, string Name)> All =
    [
        (Ancient, "Ancient"),
        (Survey, "18th-century survey"),
        (Modern, "Modern topographic"),
        (Staff, "Staff sheet, one ink"),
    ];

    public static readonly IReadOnlyList<string> ShaderOrder = [Survey, Ancient, Modern, Staff];

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

    public static ReliefGrid Flat(double width, double height, int finer = 1)
    {
        double longest = Math.Max(width, height);
        int across = Math.Min(MaxSide - 1, (PaintedSide - 1) * Math.Max(1, finer));
        int cols = Math.Max(2, (int)Math.Round(across * width / longest) + 1);
        int rows = Math.Max(2, (int)Math.Round(across * height / longest) + 1);
        return new ReliefGrid(cols, rows);
    }

    public bool CanBeFiner(int times = 2) => (Cols - 1) * times + 1 <= MaxSide && (Rows - 1) * times + 1 <= MaxSide;

    public ReliefGrid Finer(int times = 2)
    {
        var fine = new ReliefGrid((Cols - 1) * times + 1, (Rows - 1) * times + 1);
        for (int y = 0; y < fine.Rows; y++)
            for (int x = 0; x < fine.Cols; x++)
            {
                int i = y * fine.Cols + x, from = Math.Min(Rows - 1, (y + times / 2) / times) * Cols + Math.Min(Cols - 1, (x + times / 2) / times);
                fine.H[i] = HeightAt(x / (double)(fine.Cols - 1), y / (double)(fine.Rows - 1));
                fine.Kind[i] = Kind[from];
                fine.Salt[i] = Salt[from];
                fine.Detail[i] = Detail[from];
            }
        return fine;
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
