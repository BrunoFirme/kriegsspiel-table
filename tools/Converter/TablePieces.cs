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

    public static bool IsTablePiece(TtsObj o) => (o.IsCard && o.Sheet != null) || o.IsChecker || o.IsPictureDie;

    public static string? NotTranslated(TtsObj o) =>
        o.IsCard && o.Sheet == null ? o.Name + " (TTS's own playing cards, which have no picture in the module)"
        : o.Name == "Custom_Dice" && !o.IsPictureDie ? o.Name + " (a picture die with other than six sides)"
        : null;

    static string? TableVisualKey(TtsObj b) =>
        b.IsCard && b.Sheet is { } s ? $"|card|{s.FaceUrl}#{b.SheetCell}|{s.BackUrl}|{s.Across}x{s.Down}|{s.UniqueBack}"
        : b.IsChecker ? $"|{b.Color}"
        : null;

    void FillTablePiece(PieceDef def, TtsObj b, PackOut pack, string where)
    {
        if (b.IsCard) FillCard(def, b, pack, where);
        else if (b.IsChecker) FillChecker(def, b, pack);
        else FillPictureDie(def, b, pack, where);
    }

    void FillCard(PieceDef def, TtsObj b, PackOut pack, string where)
    {
        var s = b.Sheet!;
        def.Image = CardCell(pack, s.FaceUrl, s, b.SheetCell, where) ?? Placeholder(pack);
        def.Back = s.BackUrl == null ? null : s.UniqueBack ? CardCell(pack, s.BackUrl, s, b.SheetCell, where) : Asset(pack, s.BackUrl, "image", where);
        def.Shape = "rounded";
        if (s.Type != RoundedRectangleCard) _rep.Note($"The card '{b.Nickname}' ({b.Guid}) has TTS card shape {s.Type}, drawn here as a rounded rectangle.");
        def.Tags = def.Tags.Where(t => t != "block").Append("card").Distinct().ToList();
        if (def.Name == "Piece") def.Name = "Card";
    }

    string? CardCell(PackOut pack, string url, TtsObj.CardSheet s, int cell, string where)
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

    void FillChecker(PieceDef def, TtsObj b, PackOut pack)
    {
        var (name, colour) = Checkers[b.Name];
        def.Image = WhiteSquare(pack);
        def.Shape = "circle";
        def.TintsPictures = true;
        def.Tint = b.Color is { } c && !string.Equals(c, "#FFFFFF", StringComparison.OrdinalIgnoreCase) ? c : colour;
        def.Tags = def.Tags.Where(t => t != "block").Append("checker").Distinct().ToList();
        if (def.Name == "Piece") def.Name = name;
    }

    void FillPictureDie(PieceDef def, TtsObj b, PackOut pack, string where)
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

    public static double[] UpOf(TtsObj.Face f)
    {
        static double R(double deg) => deg * Math.PI / 180;
        double cx = Math.Cos(R(f.RotX)), sx = Math.Sin(R(f.RotX)), cz = Math.Cos(R(f.RotZ)), sz = Math.Sin(R(f.RotZ));
        return new[] { -cx * sz, cx * cz, -sx };
    }

    sealed record PictureDie(string Mesh, string MeshFile, List<double[]> Faces);

    PictureDie? _pictureDie;
    PictureDie StandardPictureDie => _pictureDie ??= LoadPictureDie(_cfg.Cache);

    static PictureDie LoadPictureDie(string cache)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var packStream = asm.GetManifestResourceStream("std.dice/pack.json") ?? throw new InvalidDataException("the standard dice are missing from the converter");
        var pack = Json.Deserialize<PackManifest>(new StreamReader(packStream).ReadToEnd());
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
