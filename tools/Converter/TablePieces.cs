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

    static readonly Dictionary<string, (string Name, string Colour)> Checkers = new()
    {
        ["Checker_white"] = ("White checker", "#EDE8DC"),
        ["Checker_red"] = ("Red checker", "#B3261E"),
        ["Checker_black"] = ("Black checker", "#26221F"),
    };

    public const double BagAcross = 1.5, BagThickness = 0.8, PawnAcross = 0.8, PawnHeight = 1.2;
    const string BagCanvas = "#8B7355", PawnIvory = "#E8E2D0";
    const string BoxesMissing = "Firme's Effects and Props is not installed here, so bags come over as round pieces; open the app once and convert again for boxes, chests and crates.";
    bool _boxesMissingNoted;

    public static bool IsTablePiece(ModuleObj o) => (o.IsCard && o.Sheet != null) || o.IsChecker || o.IsPictureDie || o.Name is "Bag" or "PlayerPawn" or TriangleBlock;

    public const string StockTilesetPrefix = "Tileset_";

    public static string? NotTranslated(ModuleObj o) =>
        o.IsCard && o.Sheet == null ? o.Name + " (the digital tabletop's own playing cards, which have no picture in the module)"
        : o.Name == "Custom_Dice" && !o.IsPictureDie && o.StockSides == null ? o.Name + " (a die of a kind the app has not got)"
        : o.Name.StartsWith(StockTilesetPrefix, StringComparison.Ordinal)
            ? o.Name + " (one of the digital tabletop's own props: its model ships with the game and is not ours to copy, so paint the ground there or put a piece of your own on it)"
        : null;

    static string? TableVisualKey(ModuleObj b) =>
        b.IsCard && b.Sheet is { } s ? $"|card|{s.FaceUrl}#{b.SheetCell}|{s.BackUrl}|{s.Across}x{s.Down}|{s.UniqueBack}"
        : b.IsChecker ? $"|{b.Color}"
        : b.Name == "Bag" ? $"|bag|{b.Color}|{b.Nickname}"
        : b.Name == "PlayerPawn" ? $"|pawn|{b.Color}"
        : null;

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
        else FillPictureDie(def, b, pack, where);
    }

    void FillRound(PieceDef def, ModuleObj b, PackOut pack, string tag, string name, string plain)
    {
        def.Image = WhiteSquare(pack);
        def.Shape = "circle";
        def.TintsPictures = true;
        def.Tint = b.Color is { } c && !string.Equals(c, "#FFFFFF", StringComparison.OrdinalIgnoreCase) ? c : plain;
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
        def.Tint = b.Color is { } c && !string.Equals(c, "#FFFFFF", StringComparison.OrdinalIgnoreCase) ? c : colour;
        def.Tags = def.Tags.Where(t => t != "block").Append("checker").Distinct().ToList();
        if (def.Name == "Piece") def.Name = name;
    }

    void FillPictureDie(PieceDef def, ModuleObj b, PackOut pack, string where)
    {
        var die = StandardPictureDie;
        def.Kind = "model";
        def.Shape = "box";
        def.Texture = Asset(pack, b.ImageUrl, "image", where) ?? Placeholder(pack);
        def.Mesh = die.Mesh;
        Put(pack, die.Mesh, "obj", die.MeshFile, "mesh", 0, 0);
        def.Image = null;
        def.Tags = def.Tags.Where(t => t != "block").Concat(new[] { "die", "dice", "d6" }).Distinct().ToList();
        if (def.Name == "Piece") def.Name = "Picture die";
        foreach (var f in b.RotationValues)
        {
            var up = UpOf(f);
            var face = die.Faces.MaxBy(n => n[0] * up[0] + n[1] * up[1] + n[2] * up[2])!;
            if (face[0] * up[0] + face[1] * up[1] + face[2] * up[2] < FaceMatch) { _rep.Note($"{where}: the face '{f.Value}' does not lie on a side of the die and was left out."); continue; }
            if (def.States.Any(s => s.Up!.SequenceEqual(face))) continue;
            def.States.Add(new PieceStateDef { Name = f.Value.Length > 0 ? f.Value : (def.States.Count + 1).ToString(), Up = face });
        }
    }

    public static double[] UpOf(ModuleObj.Face f)
    {
        static double R(double deg) => deg * Math.PI / 180;
        double cx = Math.Cos(R(f.RotX)), sx = Math.Sin(R(f.RotX)), cz = Math.Cos(R(f.RotZ)), sz = Math.Sin(R(f.RotZ));
        return new[] { -cx * sz, cx * cz, -sx };
    }

    sealed record PictureDie(string Mesh, string MeshFile, List<double[]> Faces);

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

    public static int DieFace(ModuleObj o, PieceDef die)
    {
        var up = UpOf(new ModuleObj.Face("", o.RotX, o.RotY, o.RotZ));
        double Dot(double[] a) => a[0] * up[0] + a[1] * up[1] + a[2] * up[2];
        if (o.RotationValues.Count > 0)
        {
            var face = o.RotationValues.MaxBy(f => Dot(UpOf(f)))!;
            int byName = die.States.FindIndex(s => string.Equals(s.Name, face.Value.Trim(), StringComparison.OrdinalIgnoreCase));
            if (byName >= 0) return byName;
        }
        int byUp = 0;
        double best = double.NegativeInfinity;
        for (int i = 0; i < die.States.Count; i++)
            if (die.States[i].Up is { } su && Dot(su) > best) { best = Dot(su); byUp = i; }
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
        return new PictureDie(die.Mesh!, file, die.States.Select(s => s.Up!).ToList());
    }
}
