using System.Reflection;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed partial class DefFactory
{
    public const double CardWidth = 2.1437, CardHeight = 3.0625, CardThickness = 0.0272;
    public const double CheckerAcross = 1.3428, CheckerThickness = 0.2505;
    public const double DieAcross = 0.6604;
    const int RoundedRectangleCard = 0;
    const double FaceMatch = 0.9;
    const int DiceType = 2, CoinType = 3;

    public static bool RollsOnItsValues(ModuleObj o) => IsModel(o) && o.MeshType is DiceType or CoinType && o.RotationValues.Count > 1;

    static void FillModelFaces(PieceDef def, ModuleObj b)
    {
        static double Dot(double[] a, double[] c) => a[0] * c[0] + a[1] * c[1] + a[2] * c[2];
        foreach (var f in b.RotationValues)
        {
            var up = UpOf(f).Select(x => Math.Round(x, 4)).ToArray();
            if (def.Faces.Any(s => Dot(s.Up, up) > FaceMatch)) continue;
            def.Faces.Add(new DieFace { Value = f.Value.Length > 0 ? f.Value : (def.Faces.Count + 1).ToString(), Up = up });
        }
        def.Tags = def.Tags.Concat(new[] { "die", "dice" }).Distinct().ToList();
    }

    static readonly Dictionary<string, (string Name, string Colour)> Checkers = new()
    {
        ["Checker_white"] = ("White checker", "#FFFFFF"),
        ["Checker_red"] = ("Red checker", "#B3261E"),
        ["Checker_black"] = ("Black checker", "#26221F"),
    };

    public const double BagAcross = 2.4573, BagThickness = 2.5823, PawnAcross = 0.6916, PawnHeight = 1.1839;
    const string BagCanvas = "#8B7355", PawnIvory = "#E8E2D0";
    const string BoxesMissing = "Firme's Effects and Props is not installed here, so bags come over as round pieces; open the app once and convert again for boxes, chests and crates.";
    bool _boxesMissingNoted;

    public static bool IsTablePiece(ModuleObj o) => (o.IsCard && o.Sheet != null) || o.IsChecker || o.IsPictureDie || o.Name is "Bag" or "PlayerPawn" or TriangleBlock or Figurine;

    public const string StockTilesetPrefix = "Tileset_";

    public static string? NotTranslated(ModuleObj o) =>
        o.TransformNotANumber ? o.Name + " (its position, turn or scale is not a number, so the digital tabletop does not load it either)"
        : o.Garbage ? o.Name + " (it sits at the table's centre with no size at all, so the digital tabletop does not load it either)"
        : o.IsCard && o.Sheet == null ? o.Name + " (the digital tabletop's own playing cards, which have no picture in the module)"
        : o.Name == "Custom_Dice" && !o.IsPictureDie && o.StockSides == null ? o.Name + " (a die of a kind the app has not got)"
        : o.Name.StartsWith(StockTilesetPrefix, StringComparison.Ordinal) && o.Name != TilesetTree
            ? o.Name + " (one of the digital tabletop's own props: its model ships with the game and is not ours to copy, so paint the ground there or put a piece of your own on it)"
        : null;

    static string? TableVisualKey(ModuleObj b) =>
        b.IsCard && b.Sheet is { } s ? $"|card|{s.FaceUrl}#{b.SheetCell}|{s.BackUrl}|{s.Across}x{s.Down}|{s.UniqueBack}"
        : b.IsChecker ? $"|{Rgb(b.Color)}"
        : b.Name == "Bag" ? $"|bag|{Rgb(b.Color)}|{b.Nickname}"
        : b.Name == "PlayerPawn" ? $"|pawn|{Rgb(b.Color)}"
        : b.IsPictureDie && b.DieSides != PictureD6 ? $"|die{b.DieSides}"
        : b.Name == Figurine ? (b.MirroredBack ? "|mirrored" : "") + string.Create(System.Globalization.CultureInfo.InvariantCulture, $"|{Rgb(b.Color)}|{b.ImageScalar:0.###}")
        : null;

    const int RgbHex = 7;

    static string? Rgb(string? colour) => colour is { Length: > RgbHex } ? colour[..RgbHex] : colour;

    void FillTablePiece(PieceDef def, ModuleObj b, PackOut pack, string where)
    {
        if (b.IsCard) FillCard(def, b, pack, where);
        else if (b.IsChecker) FillChecker(def, b, pack);
        else if (b.Name == "Bag")
        {
            FillRound(def, b, pack, "bag", "Bag", BagCanvas);
            if (!_boxesMissingNoted) _rep.Note(BoxesMissing);
            _boxesMissingNoted = true;
        }
        else if (b.Name == "PlayerPawn") FillRound(def, b, pack, "pawn", "Pawn", PawnIvory);
        else if (b.Name == TriangleBlock) FillPrism(def, pack);
        else if (b.Name == Figurine) FillFigurine(def, b, pack, where);
        else FillPictureDie(def, b, pack, where);
    }

    void FillRound(PieceDef def, ModuleObj b, PackOut pack, string tag, string name, string plain)
    {
        def.Image = WhiteSquare(pack);
        def.Shape = "circle";
        def.TintsPictures = true;
        def.Tint = b.Color ?? plain;
        def.Tags = def.Tags.Where(t => t != "block").Append(tag).Distinct().ToList();
        if (def.Name == "Piece") def.Name = name;
    }

    public const string TriangleBlock = "BlockTriangle";
    public const double TriangleAcross = 1.4142, TriangleRise = 0.7071;

    void FillPrism(PieceDef def, PackOut pack)
    {
        def.Kind = "model";
        def.Shape = "box";
        def.Mesh = PrismMesh(pack);
        def.Texture = WhiteSquare(pack);
        def.Image = null;
        if (def.Name == "Piece") def.Name = "Triangle block";
    }

    string? _prismSha;

    string PrismMesh(PackOut pack)
    {
        var path = Path.Combine(_cfg.Cache, "prism.obj");
        if (_prismSha == null)
        {
            if (!File.Exists(path)) File.WriteAllText(path, PrismObj);
            _prismSha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        Put(pack, _prismSha, "obj", path, "mesh", 0, 0);
        return _prismSha;
    }

    static readonly string PrismObj = string.Join('\n',
        "v -0.7071 0 -0.5", "v 0.7071 0 -0.5", "v 0 0.7071 -0.5", "v -0.7071 0 0.5", "v 0.7071 0 0.5", "v 0 0.7071 0.5",
        "vt 0 0", "vt 1 0", "vt 0.5 1", "vt 1 1", "vt 0 1",
        "vn 0 -1 0", "vn 0 0 -1", "vn 0 0 1", "vn -0.7071 0.7071 0", "vn 0.7071 0.7071 0",
        "f 1/1/1 2/2/1 5/4/1 4/5/1", "f 1/1/2 3/3/2 2/2/2", "f 4/1/3 5/2/3 6/3/3", "f 1/1/4 4/2/4 6/4/4 3/5/4", "f 2/1/5 3/5/5 6/4/5 5/2/5") + "\n";

    void FillCard(PieceDef def, ModuleObj b, PackOut pack, string where)
    {
        var s = b.Sheet!;
        def.Image = CardCell(pack, s.FaceUrl, s, b.SheetCell, where) ?? Placeholder(pack);
        def.Back = s.BackUrl == null ? null : s.UniqueBack ? CardCell(pack, s.BackUrl, s, b.SheetCell, where) : Asset(pack, s.BackUrl, "image", where);
        def.Shape = "rounded";
        if (s.Type != RoundedRectangleCard) _rep.Note($"The card '{b.Nickname}' ({b.Guid}) has card shape {s.Type}, drawn here as a rounded rectangle.");
        def.Tags = def.Tags.Where(t => t != "block").Append("card").Distinct().ToList();
        if (def.Name == "Piece") def.Name = "Card";
    }

    string? CardCell(PackOut pack, string url, ModuleObj.CardSheet s, int cell, string where)
    {
        if (s.Across * s.Down == 1) return Asset(pack, url, "image", where);
        if (Entry(url) is not { } sheet || !AssetStore.IsPicture(sheet.Ext)) return Asset(pack, url, "image", where);
        if (_assets.Cell(sheet, s.Across, s.Down, cell) is not { } cut)
        {
            _rep.Missing(url, $"card {cell + 1} is not on its {s.Across} × {s.Down} sheet", where);
            return Placeholder(pack);
        }
        Put(pack, cut.Sha!, cut.Ext!, _assets.FilePath(cut)!, "image", cut.W, cut.H, url);
        return cut.Sha;
    }

    void FillChecker(PieceDef def, ModuleObj b, PackOut pack)
    {
        var (name, colour) = Checkers[b.Name];
        def.Image = WhiteSquare(pack);
        def.Shape = "circle";
        def.TintsPictures = true;
        def.Tint = b.Color ?? colour;
        def.Tags = def.Tags.Where(t => t != "block").Append("checker").Distinct().ToList();
        if (def.Name == "Piece") def.Name = name;
    }

    void FillPictureDie(PieceDef def, ModuleObj b, PackOut pack, string where)
    {
        var die = b.DieSides == PictureD6 ? StandardPictureDie : TemplateDie(b.DieSides);
        def.Kind = "model";
        def.Shape = "box";
        def.Texture = Asset(pack, b.ImageUrl, "image", where) ?? Placeholder(pack);
        def.Mesh = die.Mesh;
        Put(pack, die.Mesh, "obj", die.MeshFile, "mesh", 0, 0);
        def.Image = null;
        def.Tags = def.Tags.Where(t => t != "block").Concat(new[] { "die", "dice", $"d{b.DieSides}" }).Distinct().ToList();
        if (def.Name == "Piece") def.Name = "Picture die";
        static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        foreach (var f in b.RotationValues)
        {
            var up = UpOf(f);
            var face = die.Faces.MaxBy(n => Dot(n, up))!;
            if (Dot(face, up) < FaceMatch || def.Faces.Any(s => s.Up.SequenceEqual(face))) continue;
            def.Faces.Add(new DieFace { Value = f.Value.Length > 0 ? f.Value : (def.Faces.Count + 1).ToString(), Up = face });
        }
        var bare = die.Faces.Where(n => !def.Faces.Any(s => s.Up.SequenceEqual(n))).ToList();
        if (bare.Count == 0) return;
        foreach (var n in bare)
        {
            var nearest = b.RotationValues.Count > 0 ? b.RotationValues.MaxBy(f => Dot(n, UpOf(f)))!.Value : "";
            def.Faces.Add(new DieFace { Value = nearest.Length > 0 ? nearest : (def.Faces.Count + 1).ToString(), Up = n });
        }
        if (b.RotationValues.Count > 0) _rep.Note($"{where}: {bare.Count} of its sides have no value of their own, so each shows the value saved nearest to it, as the module reads it.");
    }

    public static double[] UpOf(ModuleObj.Face f) => DieTurns.UpOf(f.RotX, f.RotZ);

    sealed record PictureDie(string Mesh, string MeshFile, List<double[]> Faces, double[] Size);

    public const int PictureD6 = 6;
    readonly Dictionary<int, PictureDie> _templates = new();

    PictureDie TemplateDie(int sides)
    {
        if (_templates.TryGetValue(sides, out var known)) return known;
        var asm = Assembly.GetExecutingAssembly();
        using var meshStream = asm.GetManifestResourceStream($"dice/d{sides}.obj") ?? throw new InvalidDataException($"the d{sides} picture die is missing from the converter");
        using var copy = new MemoryStream();
        meshStream.CopyTo(copy);
        var bytes = copy.ToArray();
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var file = Path.Combine(_cfg.Cache, $"{sha}.obj");
        if (!File.Exists(file)) Json.WriteAtomic(file, bytes);
        var layout = TemplateLayout[$"d{sides}"];
        var die = new PictureDie(sha, file, layout.Faces, layout.Size);
        _templates[sides] = die;
        return die;
    }

    public sealed record TemplateFaces(List<double[]> Faces, double[] Size);

    static Dictionary<string, TemplateFaces>? _layout;

    public static Dictionary<string, TemplateFaces> TemplateLayout => _layout ??= LoadTemplateLayout();

    static Dictionary<string, TemplateFaces> LoadTemplateLayout()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("dice/dice.json") ?? throw new InvalidDataException("the picture dice are missing from the converter");
        var root = System.Text.Json.Nodes.JsonNode.Parse(new StreamReader(stream).ReadToEnd())!.AsObject();
        static double[] Numbers(System.Text.Json.Nodes.JsonNode? n) => n!.AsArray().Select(x => x!.GetValue<double>()).ToArray();
        return root.ToDictionary(kv => kv.Key, kv => new TemplateFaces(kv.Value!["faces"]!.AsArray().Select(Numbers).ToList(), Numbers(kv.Value!["size"])));
    }

    public const string StdDicePack = "std.dice";
    PackManifest? _stdDice;
    PackManifest StdDice => _stdDice ??= LoadStdDice();

    static PackManifest LoadStdDice()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var packStream = asm.GetManifestResourceStream("std.dice/pack.json") ?? throw new InvalidDataException("the standard dice are missing from the converter");
        return Json.Deserialize<PackManifest>(new StreamReader(packStream).ReadToEnd());
    }

    PieceDef? StandardDie(int sides) => StdDice.Pieces.FirstOrDefault(p => p.Tags.Contains("d" + sides) && !p.Tags.Contains("picture"));

    static readonly Dictionary<string, double> StockDieRadius = new(StringComparer.Ordinal)
    {
        ["Die_4"] = 0.7719, ["Die_6"] = 0.5426, ["Die_6_Rounded"] = 0.4714, ["Die_8"] = 0.5861, ["Die_10"] = 0.5724, ["Die_12"] = 0.5018, ["Die_20"] = 0.5261,
    };

    readonly Dictionary<string, double> _stdDieRadius = new();

    public double StockDieFit(string type, PieceDef die) =>
        StockDieRadius.TryGetValue(type, out var game) && StdDieRadius(die) is > 0 and var ours ? Math.Round(game / ours, 4) : 1;

    double StdDieRadius(PieceDef die)
    {
        if (die.Mesh == null) return 0;
        if (_stdDieRadius.TryGetValue(die.Mesh, out var known)) return known;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"std.dice/assets/{die.Mesh}.obj");
        var r = stream == null ? 0 : DrawnRadius(new StreamReader(stream).ReadToEnd(), die.Size);
        _stdDieRadius[die.Mesh] = r;
        return r;
    }

    public static double DrawnRadius(string obj, Size2 size)
    {
        var points = obj.Split('\n').Where(l => l.StartsWith("v ", StringComparison.Ordinal)).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 4).Select(p => (X: Parse(p[1]), Y: Parse(p[2]), Z: Parse(p[3]))).ToList();
        if (points.Count == 0) return 0;
        double ax = points.Max(p => p.X) - points.Min(p => p.X), az = points.Max(p => p.Z) - points.Min(p => p.Z);
        double sx = ax > 0 ? size.W / ax : 1, sz = az > 0 ? size.H / az : 1, sy = (sx + sz) / 2;
        double cx = (points.Max(p => p.X) + points.Min(p => p.X)) / 2, cy = (points.Max(p => p.Y) + points.Min(p => p.Y)) / 2, cz = (points.Max(p => p.Z) + points.Min(p => p.Z)) / 2;
        return points.Max(p => Math.Sqrt(Math.Pow((p.X - cx) * sx, 2) + Math.Pow((p.Y - cy) * sy, 2) + Math.Pow((p.Z - cz) * sz, 2)));
    }

    static double Parse(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;

    public static int FaceShown(ModuleObj o, PieceDef die)
    {
        var up = UpOf(new ModuleObj.Face("", o.RotX, o.RotY, o.RotZ));
        double Dot(double[] a) => a[0] * up[0] + a[1] * up[1] + a[2] * up[2];
        if (o.RotationValues.Count > 0)
        {
            var face = o.RotationValues.MaxBy(f => Dot(UpOf(f)))!;
            int byName = die.Faces.FindIndex(s => string.Equals(s.Value, face.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName >= 0) return byName;
        }
        int byUp = 0;
        double best = double.NegativeInfinity;
        for (int i = 0; i < die.Faces.Count; i++)
            if (Dot(die.Faces[i].Up) > best) { best = Dot(die.Faces[i].Up); byUp = i; }
        return byUp;
    }

    PictureDie? _pictureDie;
    PictureDie StandardPictureDie => _pictureDie ??= LoadPictureDie(_cfg.Cache, StdDice);

    static PictureDie LoadPictureDie(string cache, PackManifest pack)
    {
        var asm = Assembly.GetExecutingAssembly();
        var die = pack.Pieces.First(p => p.Tags.Contains("picture") && p.Mesh != null);
        using var meshStream = asm.GetManifestResourceStream($"std.dice/assets/{die.Mesh}.obj") ?? throw new InvalidDataException("the picture die's shape is missing from the converter");
        var file = Path.Combine(cache, $"{die.Mesh}.obj");
        if (!File.Exists(file))
        {
            using var into = new MemoryStream();
            meshStream.CopyTo(into);
            Json.WriteAtomic(file, into.ToArray());
        }
        return new PictureDie(die.Mesh!, file, die.Faces.Select(s => s.Up).ToList(), [DieAcross, DieAcross, DieAcross]);
    }
}
