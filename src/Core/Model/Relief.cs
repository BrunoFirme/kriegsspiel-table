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
    public const byte None = 0, Grass = 1, Field = 2, Wood = 3, Marsh = 4, Water = 5, Road = 6, Earth = 7, Rock = 8, Sand = 9, Snow = 10, Bridge = 11, Town = 12;

    public const string SeaName = "sea";
    const float SeaDeeper = 0.2f;

    public static bool IsSea(byte detail) => (detail & LevelSteps) > 0;

    public static byte SeaLevels => Levels(100, 0);

    public static byte DetailFor(string? name, byte otherwise) => IsSeaName(name) ? SeaLevels : Of(name ?? "") == Water ? (byte)0 : otherwise;

    public static bool IsSeaName(string? name) => string.Equals(name?.Trim(), SeaName, StringComparison.OrdinalIgnoreCase);

    public static string Painted(byte kind, byte detail) => kind == Water && IsSea(detail) ? SeaName : Name(kind);

    public static IEnumerable<string> Paints => Brushes.SelectMany(k => k == Water ? new[] { Name(k), SeaName } : [Name(k)]);

    public static string PaintColor(string name, string? style)
    {
        string water = Color(Of(name) ?? None, style);
        if (!IsSeaName(name) || water.Length < 7) return water;
        return "#" + string.Concat(Enumerable.Range(0, 3).Select(i => ((int)(Convert.ToInt32(water.Substring(1 + 2 * i, 2), 16) * (1 - SeaDeeper))).ToString("X2")));
    }

    public static readonly IReadOnlyList<(string Name, string Color)> All =
    [
        ("none", "#00000000"),
        ("grass", "#6C7A50"),
        ("field", "#AF9A67"),
        ("wood", "#3A4B30"),
        ("marsh", "#5C7566"),
        ("water", "#456884"),
        ("road", "#B39C72"),
        ("earth", "#8A6F4D"),
        ("rock", "#8B8478"),
        ("sand", "#BEA775"),
        ("snow", "#E6E3D8"),
        ("bridge", "#B39C72"),
        ("town", "#A8967E"),
    ];

    public static byte? Of(string name)
    {
        if (IsSeaName(name)) return Water;
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
        ["#00000000", "#8A8854", "#A8884A", "#4A5D36", "#718468", "#3B6876", "#73301C", "#8E6A40", "#985F49", "#B79656", "#E4DAC2", "#73301C", "#783826"];

    static readonly string[] Modern =
        ["#00000000", "#A1AE83", "#CAC1A0", "#71906A", "#99B3A5", "#7490B5", "#CAC1A0", "#B6A077", "#AAA493", "#C6B277", "#EFEEE6", "#CAC1A0", "#A09C8C"];

    static readonly string[] Staff =
        ["#00000000", "#B2AF9A", "#C5C1AB", "#807F72", "#A09F92", "#9A9F98", "#C5C1AB", "#B0AB95", "#98968A", "#BBB69F", "#E6E5DC", "#C5C1AB", "#9F9D91"];

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

    public bool Worked { get; set; }

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
        FinerInto(fine, times, new CellBox(0, 0, fine.Cols - 1, fine.Rows - 1));
        return fine;
    }

    public void FinerInto(ReliefGrid fine, int times, CellBox box)
    {
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * fine.Cols + x, from = Math.Min(Rows - 1, (y + times / 2) / times) * Cols + Math.Min(Cols - 1, (x + times / 2) / times);
                fine.H[i] = times == 1 ? H[from] : HeightAt(x / (double)(fine.Cols - 1), y / (double)(fine.Rows - 1));
                fine.Kind[i] = Kind[from];
                fine.Salt[i] = Salt[from];
                fine.Detail[i] = Detail[from];
            }
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
