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
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] public List<LayerChunks>? Layers { get; set; }
}

public sealed class LayerChunks
{
    public string Id { get; set; } = "";
    public List<string> Ground { get; set; } = new();
    public List<string> Seeds { get; set; } = new();
    public List<string> Details { get; set; } = new();
}

public sealed class GroundCells
{
    public readonly byte[] Kind, Salt, Detail;

    public GroundCells(int cells)
    {
        Kind = new byte[cells];
        Salt = new byte[cells];
        Detail = new byte[cells];
    }

    public GroundCells(byte[] kind, byte[] salt, byte[] detail)
    {
        Kind = kind;
        Salt = salt;
        Detail = detail;
    }

    public GroundCells Copy() => new((byte[])Kind.Clone(), (byte[])Salt.Clone(), (byte[])Detail.Clone());

    public bool Bare => Kind.AsSpan().IndexOfAnyExcept(GroundKinds.None) < 0;
}

public static class GroundKinds
{
    public const byte None = 0, Grass = 1, Field = 2, Wood = 3, Marsh = 4, Water = 5, Road = 6, Earth = 7, Rock = 8, Sand = 9, Snow = 10, Bridge = 11, Town = 12;

    public const string SeaName = "sea";
    const float SeaDeeper = 0.2f;

    public static bool IsSea(byte detail) => (detail & LevelSteps) > 0;

    public const int FlowSteps = 15;

    public static byte Flowing(byte detail, int step) => (byte)(detail & LevelSteps | 1 + step % FlowSteps << 4);

    public static int FlowOf(byte detail) => detail >> 4;

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
        ("grass", "#55653A"),
        ("field", "#B9A160"),
        ("wood", "#29461F"),
        ("marsh", "#4A6143"),
        ("water", "#B2C8D2"),
        ("road", "#4A4A42"),
        ("earth", "#625E3B"),
        ("rock", "#76786A"),
        ("sand", "#8A875C"),
        ("snow", "#E2E6DA"),
        ("bridge", "#4A4A42"),
        ("town", "#525A3B"),
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

    public const int Regional = 0, Broadleaf = 1, Conifer = 2, Mixed = 3, Birch = 4, PalmAndOlive = 5;

    public static readonly IReadOnlyList<(string Id, string Name)> Forests =
        [("regional", "Regional"), ("broadleaf", "Broadleaf"), ("conifer", "Conifer"), ("mixed", "Mixed"), ("birch", "Birch"), ("palm", "Palm & olive")];

    public static string ForestIds => string.Join(", ", Forests.Select(f => f.Id));

    public static int ForestOf(byte detail) => Math.Min(detail >> 4, Forests.Count - 1);

    public static byte WithForest(byte detail, int forest) => (byte)(detail & LevelSteps | Math.Clamp(forest, 0, Forests.Count - 1) << 4);

    public static int ForestNamed(string? id) => Math.Max(0, Forests.ToList().FindIndex(f => string.Equals(f.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)));

    public static byte DefaultLevels(byte kind)
    {
        var p = Parameters(kind);
        return Levels(p.Count > 0 ? p[0].Default : 0, p.Count > 1 ? p[1].Default : 0);
    }

    public static (int First, int Second) LevelsOf(byte detail) => (Percent(detail & LevelSteps), Percent(detail >> 4));

    static int Nibble(int percent) => (int)Math.Round(Math.Clamp(percent, 0, 100) / 100.0 * LevelSteps);

    static int Percent(int nibble) => (int)Math.Round(nibble * 100.0 / LevelSteps);

    static readonly string[] Ancient =
        ["#00000000", "#AB965C", "#BC8E46", "#465A34", "#768466", "#3B6876", "#73301C", "#8E6A40", "#985F49", "#CBAE68", "#E4DAC2", "#73301C", "#783826"];

    static readonly string[] Modern =
        ["#00000000", "#C3BB97", "#CAC1A0", "#738C5E", "#93AC9C", "#6485AE", "#CAC1A0", "#B09668", "#A39D8C", "#C9B57A", "#ECEAE0", "#CAC1A0", "#A09C8C"];

    static readonly string[] Staff =
        ["#00000000", "#B2AF9A", "#C5C1AB", "#807F72", "#A09F92", "#9A9F98", "#C5C1AB", "#B0AB95", "#98968A", "#BBB69F", "#E6E5DC", "#C5C1AB", "#9F9D91"];

    static readonly string[] Medieval =
        ["#00000000", "#2B4929", "#755A31", "#0D2917", "#1E4238", "#003845", "#9E8E79", "#5A3E29", "#6C6760", "#866E4A", "#B3B0A9", "#9E8E79", "#4A372C"];

    public static string Color(byte kind, string? style)
    {
        if (kind >= All.Count) return All[None].Color;
        return MapStyles.Of(style) switch
        {
            MapStyles.Ancient => Ancient[kind],
            MapStyles.Modern => Modern[kind],
            MapStyles.Staff => Staff[kind],
            MapStyles.Medieval => Medieval[kind],
            _ => All[kind].Color,
        };
    }
}

public sealed record GroundParameter(string Name, string Tip, int Default);

public static class MapStyles
{
    public const string Survey = "", Ancient = "ancient", Modern = "modern", Staff = "staff", Medieval = "medieval";

    public static readonly IReadOnlyList<(string Id, string Name)> All =
    [
        (Ancient, "Ancient"),
        (Medieval, "Medieval"),
        (Survey, "18th-century survey"),
        (Modern, "Modern topographic"),
        (Staff, "Staff sheet, one ink"),
    ];

    public static readonly IReadOnlyList<string> ShaderOrder = [Survey, Ancient, Modern, Staff, Medieval];

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

    public Dictionary<string, GroundCells>? Layers { get; private set; }

    public IReadOnlyList<string> Order { get; private set; } = [GroundLayerDef.BaseId];

    public GroundCells? Painting { get; private set; }

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
        if (Layers != null)
        {
            fine.Layered();
            fine.Order = Order;
        }
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * fine.Cols + x, from = Math.Min(Rows - 1, (y + times / 2) / times) * Cols + Math.Min(Cols - 1, (x + times / 2) / times);
                fine.H[i] = times == 1 ? H[from] : HeightAt(x / (double)(fine.Cols - 1), y / (double)(fine.Rows - 1));
                fine.Kind[i] = Kind[from];
                fine.Salt[i] = Salt[from];
                fine.Detail[i] = Detail[from];
                if (Layers == null) continue;
                foreach (var (id, cells) in Layers)
                {
                    var into = fine.Cells(id);
                    into.Kind[i] = cells.Kind[from];
                    into.Salt[i] = cells.Salt[from];
                    into.Detail[i] = cells.Detail[from];
                }
            }
    }

    public ReliefGrid Copy()
    {
        var c = new ReliefGrid(Cols, Rows);
        Array.Copy(H, c.H, H.Length);
        Array.Copy(Kind, c.Kind, Kind.Length);
        Array.Copy(Salt, c.Salt, Salt.Length);
        Array.Copy(Detail, c.Detail, Detail.Length);
        c.Order = Order;
        if (Layers != null) c.Layers = Layers.ToDictionary(l => l.Key, l => l.Value.Copy());
        return c;
    }

    public void Take(ReliefGrid from, CellBox box)
    {
        if (from.Cols != Cols || from.Rows != Rows) throw new ArgumentException($"a {from.Cols} × {from.Rows} grid cannot fill a {Cols} × {Rows} one");
        var b = box.Clip(Cols, Rows);
        if (b.Empty) return;
        if (from.Layers != null) Layered();
        var theirs = from.Layers ?? new Dictionary<string, GroundCells> { [GroundLayerDef.BaseId] = new(from.Kind, from.Salt, from.Detail) };
        for (int y = b.Y0; y <= b.Y1; y++)
        {
            int at = y * Cols + b.X0;
            Array.Copy(from.H, at, H, at, b.Width);
            Array.Copy(from.Kind, at, Kind, at, b.Width);
            Array.Copy(from.Salt, at, Salt, at, b.Width);
            Array.Copy(from.Detail, at, Detail, at, b.Width);
            if (Layers == null) continue;
            foreach (var (id, mine) in Layers)
                if (!theirs.ContainsKey(id)) Array.Clear(mine.Kind, at, b.Width);
            foreach (var (id, cells) in theirs)
            {
                var into = Cells(id);
                Array.Copy(cells.Kind, at, into.Kind, at, b.Width);
                Array.Copy(cells.Salt, at, into.Salt, at, b.Width);
                Array.Copy(cells.Detail, at, into.Detail, at, b.Width);
            }
        }
    }

    public GroundCells Cells(string layer)
    {
        Layered();
        if (!Layers!.TryGetValue(layer, out var cells)) Layers[layer] = cells = new GroundCells(Kind.Length);
        return cells;
    }

    public void Layered()
    {
        if (Layers != null) return;
        Layers = new() { [GroundLayerDef.BaseId] = new GroundCells((byte[])Kind.Clone(), (byte[])Salt.Clone(), (byte[])Detail.Clone()) };
    }

    public void Aim(string layer, IReadOnlyList<string> order)
    {
        Fold(layer);
        Order = order;
        Painting = Layers == null && layer == GroundLayerDef.BaseId && order.Contains(layer) ? null : Cells(layer);
        if (Painting != null) Compose(order);
    }

    public void Ordered(IReadOnlyList<string> order) => Order = order;

    public void Compose(IReadOnlyList<string> order)
    {
        Order = order;
        if (Layers == null)
        {
            if (order.Contains(GroundLayerDef.BaseId)) return;
            Layered();
        }
        for (int i = 0; i < Kind.Length; i++) ComposeCell(i);
    }

    public void ComposeCell(int i)
    {
        if (Showing(i) is { } cells)
        {
            Kind[i] = cells.Kind[i];
            Salt[i] = cells.Salt[i];
            Detail[i] = cells.Detail[i];
            return;
        }
        Kind[i] = GroundKinds.None;
        Salt[i] = 0;
        Detail[i] = 0;
    }

    public void Fold(string into)
    {
        if (Layers == null) return;
        for (int i = 0; i < Kind.Length; i++)
        {
            byte kind = Kind[i], salt = Salt[i], detail = Detail[i];
            var shows = Showing(i);
            if (shows == null ? kind == GroundKinds.None : shows.Kind[i] == kind && shows.Salt[i] == salt && shows.Detail[i] == detail) continue;
            if (kind == GroundKinds.None)
            {
                foreach (var cells in Layers.Values) cells.Kind[i] = GroundKinds.None;
                continue;
            }
            var target = shows ?? Cells(into);
            target.Kind[i] = kind;
            target.Salt[i] = salt;
            target.Detail[i] = detail;
        }
    }

    GroundCells? Showing(int i)
    {
        foreach (var id in Order)
            if (Layers!.TryGetValue(id, out var cells) && cells.Kind[i] != GroundKinds.None) return cells;
        return null;
    }

    public void Settle(IReadOnlyList<string> order, string into)
    {
        Painting = null;
        Fold(into);
        if (Layers == null)
        {
            Order = order;
            if (!order.Contains(GroundLayerDef.BaseId)) Bared();
            return;
        }
        foreach (var gone in Layers.Keys.Where(id => !order.Contains(id)).ToList()) Layers.Remove(gone);
        foreach (var bare in Layers.Where(l => l.Key != GroundLayerDef.BaseId && l.Value.Bare).Select(l => l.Key).ToList()) Layers.Remove(bare);
        Compose(order);
        if (Layers.Count == 1 && Layers.ContainsKey(GroundLayerDef.BaseId) && order.Contains(GroundLayerDef.BaseId)) Layers = null;
    }

    void Bared()
    {
        Array.Clear(Kind);
        Array.Clear(Salt);
        Array.Clear(Detail);
    }

    public bool PaintedOn(string layer) => Layers is { } layers ? layers.TryGetValue(layer, out var cells) && !cells.Bare : layer == GroundLayerDef.BaseId && Painted;

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

    static IEnumerable<string> Chunks(ReliefIndex index) =>
        index.Heights.Concat(index.Ground).Concat(index.Seeds ?? new()).Concat(index.Details ?? new()).Concat((index.Layers ?? []).SelectMany(l => l.Ground.Concat(l.Seeds).Concat(l.Details)));

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
        if (g.Layers is { } layers)
            index.Layers = g.Order.Where(layers.ContainsKey).Concat(layers.Keys.Where(id => !g.Order.Contains(id)).Order(StringComparer.Ordinal))
                .Select(id => LayerOf(packageDir, g, id, layers[id], written)).ToList();
        var header = Json.SerializeBytes(index);
        var sha = Sha(header);
        var path = Path.Combine(packageDir, Folder, sha + HeaderExt);
        if (!File.Exists(path)) { Json.WriteAtomic(path, header); written.Add(HeaderRel(sha)); }
        return (sha, written);
    }

    static LayerChunks LayerOf(string packageDir, ReliefGrid g, string id, GroundCells cells, List<string> written)
    {
        var chunks = new LayerChunks { Id = id };
        for (int cy = 0; cy < g.ChunksDown; cy++)
            for (int cx = 0; cx < g.ChunksAcross; cx++)
            {
                chunks.Ground.Add(Put(packageDir, ByteChunk(g, cells.Kind, cx, cy), written));
                chunks.Seeds.Add(Put(packageDir, ByteChunk(g, cells.Salt, cx, cy), written));
                chunks.Details.Add(Put(packageDir, ByteChunk(g, cells.Detail, cx, cy), written));
            }
        return chunks;
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
        if (index.Layers is { } layers && (layers.Any(l => l.Ground.Count != chunks || l.Seeds.Count != chunks || l.Details.Count != chunks || !GroundLayerDef.FitId(l.Id))
            || layers.Select(l => l.Id).Distinct().Count() != layers.Count))
            throw new InvalidDataException("the relief's ground layers do not match its size");
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
        if (index.Layers is { } layers) LoadLayers(g, layers, chunkPath);
        return g;
    }

    static void LoadLayers(ReliefGrid g, List<LayerChunks> layers, Func<string, string?> chunkPath)
    {
        const int Size = ReliefGrid.Chunk * ReliefGrid.Chunk;
        foreach (var layer in layers)
        {
            var cells = g.Cells(layer.Id);
            Array.Clear(cells.Kind);
            Array.Clear(cells.Salt);
            Array.Clear(cells.Detail);
            int i = 0;
            for (int cy = 0; cy < g.ChunksDown; cy++)
                for (int cx = 0; cx < g.ChunksAcross; cx++, i++)
                {
                    if (layer.Ground[i].Length > 0) ReadBytes(g, cells.Kind, cx, cy, Chunk(layer.Ground[i], chunkPath, Size), (byte)(GroundKinds.All.Count - 1));
                    if (layer.Seeds[i].Length > 0) ReadBytes(g, cells.Salt, cx, cy, Chunk(layer.Seeds[i], chunkPath, Size), byte.MaxValue);
                    if (layer.Details[i].Length > 0) ReadBytes(g, cells.Detail, cx, cy, Chunk(layer.Details[i], chunkPath, Size), byte.MaxValue);
                }
        }
        if (layers.All(l => l.Id != GroundLayerDef.BaseId)) g.Layers!.Remove(GroundLayerDef.BaseId);
        g.Ordered(layers.Select(l => l.Id).ToList());
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
