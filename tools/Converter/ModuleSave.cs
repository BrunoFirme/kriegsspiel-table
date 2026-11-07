using System.Globalization;
using System.Text.Json.Nodes;

namespace KTable.Converter;

public sealed class ModuleObj
{
    public JsonObject J { get; }
    public ModuleObj(JsonObject j) { J = j; }

    public string Name => S(J["Name"]) ?? "";
    public string Nickname => (S(J["Nickname"]) ?? "").Trim();
    public string Guid => S(J["GUID"]) ?? "";
    public bool Locked => ModuleValues.Flag(J["Locked"], false);
    public string Lua => S(J["LuaScript"]) ?? "";
    KTable.Core.ScriptSafety.Verdict? _verdict;
    public KTable.Core.ScriptSafety.Verdict Verdict => _verdict ??= KTable.Core.ScriptSafety.Inspect(Lua);
    public string SafeLua => Verdict.Script;
    public string GmNotes => S(J["GMNotes"]) ?? "";
    public string Description => S(J["Description"]) ?? "";
    public string LuaState => S(J["LuaScriptState"]) ?? "";
    public string Memo => S(J["Memo"]) ?? "";
    public int Worth => J["Value"] != null ? I(J, "Value") : 0;
    public int StackCount => J["Number"] != null ? Math.Max(1, I(J, "Number")) : 1;
    public string XmlUi => S(J["XmlUI"]) ?? "";
    public List<(string Name, string Url)> UiAssets => UiAssetsOf(J["CustomUIAssets"]);

    public static List<(string Name, string Url)> UiAssetsOf(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>()
        .Where(a => I(a, "Type") == 0 && Blank(S(a["Name"])) != null && Blank(S(a["URL"])) != null)
        .Select(a => (Blank(S(a["Name"]))!, Blank(S(a["URL"]))!)).ToList() ?? new();
    public List<string> Tags => (J["Tags"] as JsonArray)?.Select(t => S(t) ?? "").Where(t => t.Length > 0).ToList() ?? new();

    JsonObject? T => J["Transform"] as JsonObject;
    public double PosX => D(T, "posX");
    public double PosY => D(T, "posY");
    public double PosZ => D(T, "posZ");
    const double TableScale = 6, UnderTheTable = -0.5, TerrainSpan = 40;
    static readonly AsyncLocal<Func<string, (double ex, double ey, double ez)?>?> _meshSize = new();
    public static Func<string, (double ex, double ey, double ez)?>? MeshSize { get => _meshSize.Value; set => _meshSize.Value = value; }
    double Span => MeshUrl is { } u && MeshSize?.Invoke(u) is { } b ? Math.Max(Math.Abs(ScaleX) * b.ex, Math.Abs(ScaleZ) * b.ez) : 0;
    public bool IsTerrain => Name == "Custom_Model" && Locked && PosY >= UnderTheTable && MeshUrl != null && DiffuseUrl != null
                             && (Math.Abs(ScaleX) >= TableScale || Span >= TerrainSpan);
    public bool IsFurniture => ((Name == "Custom_Model" && Math.Abs(ScaleX) >= TableScale && !IsTerrain) || (Locked && PosY < UnderTheTable && Name is "Custom_Model" or "Custom_Tile" or "Custom_Token" or "Custom_Board"))
                               && Top == null;
    static readonly AsyncLocal<Func<string, TableTop?>?> _meshTop = new();
    public static Func<string, TableTop?>? MeshTop { get => _meshTop.Value; set => _meshTop.Value = value; }
    public TableTop? Top => Name == "Custom_Model" && Locked && DiffuseUrl != null && MeshUrl is { } u && ScaleX * ScaleZ > 0 && PosY < UnderTheTable
                            && MeshTop?.Invoke(u) is { } t && PosY + t.Top * ScaleY >= UnderTheTable ? t : null;
    public double RotX => D(T, "rotX");
    public double RotY => D(T, "rotY");
    public double RotZ => D(T, "rotZ");
    public double ScaleX => D(T, "scaleX", 1);
    public double ScaleY => HeightLocked ? 1 : D(T, "scaleY", 1);
    public double ScaleZ => D(T, "scaleZ", 1);
    public bool TransformNotANumber => T is { } t && t.Any(f => ModuleValues.NotANumber(f.Value));
    public bool Garbage => T is { } t && TransformKeys.All(k => ModuleValues.Number(t[k], 0) == 0);
    static readonly string[] TransformKeys = { "posX", "posY", "posZ", "scaleX", "scaleY", "scaleZ" };
    public bool Flipped => Near180(RotX) ^ Near180(RotZ);
    public double Heading => Near180(RotX) ? RotY + HalfTurn : RotY;
    const double HalfTurn = 180;
    static bool Near180(double a) { a = ((a % 360) + 360) % 360; return Math.Abs(a - 180) < 30; }

    JsonObject? CI => J["CustomImage"] as JsonObject;
    public string? ImageUrl => Blank(S(CI?["ImageURL"]));
    public string? BackUrl => Blank(S(CI?["ImageSecondaryURL"]));
    public bool MirroredBack => CI?["CustomFigurine"] is JsonObject fig && ModuleValues.Flag(fig["MirroredBack"], false);
    JsonObject? Tile => CI?["CustomTile"] as JsonObject;
    JsonObject? Token => CI?["CustomToken"] as JsonObject;
    const double PrefabThickness = 0.2, PrefabMergeDistance = 15;
    public int TileType => Tile is { } t ? ModuleValues.Whole(t["Type"], 0) : 0;
    public bool TileStretch => Tile is not { } t || ModuleValues.Flag(t["Stretch"], false);
    public double WidthScale => ModuleValues.Finite(CI?["WidthScale"], 0);
    public double ImageScalar => CI is { } ci ? ModuleValues.Finite(ci["ImageScalar"], 1) : 1;
    public double Thickness => Name switch
    {
        "Custom_Tile" => Tile is { } t ? ModuleValues.Finite(t["Thickness"], 0) : PrefabThickness,
        "Custom_Token" => Token is { } k ? ModuleValues.Finite(k["Thickness"], 0) : PrefabThickness,
        _ => PrefabThickness,
    };
    public double MergeDistance => Token is { } k ? ModuleValues.Finite(k["MergeDistancePixels"], 0) : PrefabMergeDistance;
    public bool StandsUp => Token is { } k && ModuleValues.Flag(k["StandUp"], false);
    public bool HeightLocked => Name is "Custom_Tile" or "Custom_PDF" or "Card" or "CardCustom" or "Deck" or "DeckCustom";

    JsonObject? CM => J["CustomMesh"] as JsonObject;
    public string? MeshUrl => Blank(S(CM?["MeshURL"]));
    public string? DiffuseUrl => Blank(S(CM?["DiffuseURL"]));
    public string? NormalUrl => Blank(S(CM?["NormalURL"]));
    public bool CastsShadows => CM is not { } m || ModuleValues.Flag(m["CastShadows"], true);
    public int MeshType => ModuleValues.Whole(CM?["TypeIndex"] ?? (J["CustomAssetbundle"] as JsonObject)?["TypeIndex"], 0);
    public string? Finish => KTable.Core.Model.Finishes.Of(CM?["MaterialIndex"] != null ? I(CM, "MaterialIndex") : J["MaterialIndex"] != null ? I(J, "MaterialIndex") : -1);
    public KTable.Core.Model.ShineDef? Shine => CM?["CustomShader"] is JsonObject sh
        ? new KTable.Core.Model.ShineDef
        {
            Color = sh["SpecularColor"] is JsonObject c ? HexOf(c) : "#E6E6E6", Strength = D(sh, "SpecularIntensity", 0.1), Sharpness = D(sh, "SpecularSharpness", 3),
            Rim = D(sh, "FresnelStrength", 0.1),
        }
        : null;
    public string? BundleUrl => Blank(S((J["CustomAssetbundle"] as JsonObject)?["AssetbundleURL"]));
    public string? PdfUrl => Blank(S((J["CustomPDF"] as JsonObject)?["PDFUrl"]));
    public int PdfPage => J["CustomPDF"] is JsonObject pdf && pdf["PDFPage"] != null ? Math.Max(0, I(pdf, "PDFPage")) : 0;
    public int PdfPageOffset => J["CustomPDF"] is JsonObject pdf && pdf["PDFPageOffset"] != null ? I(pdf, "PDFPageOffset") : 0;
    public string? PdfPassword => J["CustomPDF"] is JsonObject pdf ? Blank(S(pdf["PDFPassword"])) : null;

    public string? Color => J["ColorDiffuse"] is JsonObject c ? HexOf(c) + AlphaOf(c) : PrefabColours.GetValueOrDefault(Name);

    static readonly Dictionary<string, string> PrefabColours = new(StringComparer.Ordinal)
    {
        ["BlockSquare"] = "#EC0000", ["BlockRectangle"] = "#0033D2", ["BlockTriangle"] = "#01C500", ["Bag"] = "#B45D00", ["PlayerPawn"] = "#DBDBDB",
        ["Figurine_Custom"] = "#000000", ["Card"] = "#B6B6B6", ["CardCustom"] = "#B6B6B6", ["Deck"] = "#B6B6B6", ["Custom_Board"] = "#C9C9C9",
        ["Die_4"] = "#264D71", ["Die_6_Rounded"] = "#0F0E0E", ["Die_8"] = "#901515", ["Die_10"] = "#136A2C", ["Die_12"] = "#D0821E", ["Die_20"] = "#841B9E",
    };

    const double Opaque = 0.999;

    static string AlphaOf(JsonObject c) => c["a"] != null && D(c, "a", 1) is var a and < Opaque ? $"{(int)Math.Round(Math.Clamp(a, 0, 1) * 255):X2}" : "";

    public static string HexOf(JsonObject c)
    {
        int r = (int)Math.Round(Math.Clamp(D(c, "r"), 0, 1) * 255), g = (int)Math.Round(Math.Clamp(D(c, "g"), 0, 1) * 255), b = (int)Math.Round(Math.Clamp(D(c, "b"), 0, 1) * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public sealed record Line(List<(double X, double Y, double Z)> Points, string Color, double Thickness, bool Loop, bool Round = false);

    public List<Line> VectorLines => LinesOf(J["AttachedVectorLines"]);

    public static List<Line> LinesOf(JsonNode? node)
    {
        const double DefaultThickness = 0.1;
        var lines = new List<Line>();
        foreach (var l in (node as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var turn = l["rotation"] as JsonObject;
            var points = (l["points3"] as JsonArray)?.OfType<JsonObject>().Select(p => Turned((D(p, "x"), D(p, "y"), D(p, "z")), D(turn, "x"), D(turn, "y"), D(turn, "z")))
                .ToList() ?? new();
            double thickness = D(l, "thickness", DefaultThickness);
            if (points.Count < 2 || thickness <= 0) continue;
            var c = l["color"] as JsonObject;
            if (c?["a"] != null && D(c, "a", 1) <= 0) continue;
            var colour = c is null ? "#FFFFFF" : HexOf(c) + AlphaOf(c);
            lines.Add(new Line(points, colour, thickness, ModuleValues.Flag(l["loop"], false), !ModuleValues.Flag(l["square"], false)));
        }
        return lines;
    }

    public static (double X, double Y, double Z) Turned((double X, double Y, double Z) p, double rx, double ry, double rz)
    {
        const double Degree = Math.PI / 180;
        double cz = Math.Cos(rz * Degree), sz = Math.Sin(rz * Degree), cx = Math.Cos(rx * Degree), sx = Math.Sin(rx * Degree);
        double cy = Math.Cos(ry * Degree), sy = Math.Sin(ry * Degree);
        double x1 = p.X * cz - p.Y * sz, y1 = p.X * sz + p.Y * cz, z1 = p.Z;
        double y2 = y1 * cx - z1 * sx, z2 = y1 * sx + z1 * cx;
        return (x1 * cy + z2 * sy, y2, -x1 * sy + z2 * cy);
    }

    public bool IsCard => Name is "Card" or "CardCustom";
    public bool IsDeck => Name is "Deck" or "DeckCustom";
    public bool IsChecker => Name is "Checker_white" or "Checker_red" or "Checker_black";
    public bool IsBag => Name is "Bag" or "Custom_Model_Bag";
    public bool Sticky => Flag("Sticky", true);
    public bool DragSelectable => Flag("DragSelectable", true);
    public bool MeasureMovement => Flag("MeasureMovement", false);
    bool Flag(string key, bool unset) => ModuleValues.Flag(J[key], unset);
    public bool IsInfiniteBag => Name.Contains("Infinite", StringComparison.Ordinal);
    const int LastInFirstOut = 0, RandomOrder = 2;
    public string? TakeOrder => (J["Bag"] is JsonObject b && b["Order"] != null ? I(b, "Order") : LastInFirstOut) switch
    {
        LastInFirstOut => KTable.Core.Model.TakeOrders.Last,
        RandomOrder => KTable.Core.Model.TakeOrders.Random,
        _ => null,
    };
    public bool IsPictureDie => Name == "Custom_Dice" && DiceType >= 0 && DiceType < DiceTypeSides.Length;
    public int DieSides => IsPictureDie ? DiceTypeSides[DiceType] : 0;
    public int DiceType => CI?["CustomDice"] is JsonObject d ? ModuleValues.Whole(d["Type"], 0) : -1;
    static readonly int[] DiceTypeSides = { 4, 6, 8, 10, 12, 20 };
    public int? StockSides => Name switch
    {
        "Die_4" => 4, "Die_6" or "Die_6_Rounded" => 6, "Die_8" => 8, "Die_10" => 10, "Die_12" => 12, "Die_20" => 20,
        _ => null,
    };

    public sealed record CardSheet(string FaceUrl, string? BackUrl, int Across, int Down, bool UniqueBack, int Type);

    public int CardId => ModuleValues.Whole(J["CardID"], 0);

    public Dictionary<int, CardSheet> CardSheets
    {
        get
        {
            var sheets = new Dictionary<int, CardSheet>();
            if (J["CustomDeck"] is not JsonObject deck) return sheets;
            foreach (var (k, v) in deck)
            {
                if (!int.TryParse(k, out var id) || v is not JsonObject s || Blank(S(s["FaceURL"])) is not { } face) continue;
                var back = Blank(S(s["BackURL"]));
                bool unique = ModuleValues.Flag(s["UniqueBack"], false) || (back ?? "").EndsWith(UniqueMark, StringComparison.Ordinal);
                sheets[id] = new CardSheet(face, back, Math.Max(1, ModuleValues.Whole(s["NumWidth"], SheetAcross)), Math.Max(1, ModuleValues.Whole(s["NumHeight"], SheetDown)),
                    unique, I(s, "Type"));
            }
            return sheets;
        }
    }

    const int SheetAcross = 10, SheetDown = 7;
    const string UniqueMark = "{Unique}";

    public CardSheet? Sheet => CardSheets.GetValueOrDefault(CardId / CardsPerSheet);
    public int SheetCell => Sheet is { } s ? Math.Clamp(CardId % CardsPerSheet, 0, s.Across * s.Down - 1) : CardId % CardsPerSheet;
    const int CardsPerSheet = 100;

    public sealed record Face(string Value, double RotX, double RotY, double RotZ);

    public List<Face> RotationValues => (J["RotationValues"] as JsonArray)?.OfType<JsonObject>()
        .Select(r => (value: r["Value"], rot: r["Rotation"] as JsonObject))
        .Select(r => new Face(ModuleValues.Text(r.value) ?? "", D(r.rot, "x"), D(r.rot, "y"), D(r.rot, "z")))
        .ToList() ?? new();

    public IEnumerable<ModuleObj> Contained => Arr("ContainedObjects");
    public IEnumerable<ModuleObj> Children => Arr("ChildObjects");
    IEnumerable<ModuleObj> Arr(string k) => (J[k] as JsonArray)?.OfType<JsonObject>().Select(o => new ModuleObj(o)) ?? [];

    public (List<ModuleObj> states, int selfIndex) StateList()
    {
        if (J["States"] is not JsonObject st || st.Count == 0) return (new() { this }, 0);
        var map = new SortedDictionary<int, ModuleObj>();
        foreach (var (k, v) in st)
            if (int.TryParse(k, out var id) && v is JsonObject o) map[id] = new ModuleObj(o);
        int self = 1;
        while (map.ContainsKey(self)) self++;
        map[self] = this;
        var list = map.Values.ToList();
        return (list, list.IndexOf(this));
    }

    public sealed record Decal(string Name, string Url, double PosX, double PosY, double PosZ, double RotY, double ScaleX, double ScaleY);

    public List<Decal> Decals => DecalsOf(J["AttachedDecals"]);

    public sealed record PaletteEntry(string Name, string Url, double Size);

    public static List<PaletteEntry> PaletteOf(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>()
        .Where(c => Blank(S(c["ImageURL"])) != null)
        .Select(c => new PaletteEntry(S(c["Name"]) ?? "", Blank(S(c["ImageURL"]))!, D(c, "Size", 0)))
        .Distinct().ToList() ?? new();

    public static List<Decal> DecalsOf(JsonNode? node) => (node as JsonArray)?.OfType<JsonObject>()
        .Select(d => (t: d["Transform"] as JsonObject, c: d["CustomDecal"] as JsonObject))
        .Where(d => Blank(S(d.c?["ImageURL"])) != null)
        .Select(d => new Decal(S(d.c!["Name"]) ?? "", Blank(S(d.c!["ImageURL"]))!, D(d.t, "posX"), D(d.t, "posY"), D(d.t, "posZ"), D(d.t, "rotY"), D(d.t, "scaleX", 1), D(d.t, "scaleY", 1)))
        .ToList() ?? new();

    public IEnumerable<string> Urls()
    {
        foreach (var u in new[] { ImageUrl, BackUrl, MeshUrl, DiffuseUrl, BundleUrl, PdfUrl }) if (u != null) yield return u;
        foreach (var d in Decals) yield return d.Url;
        foreach (var a in UiAssets) yield return a.Url;
        foreach (var s in CardSheets.Values)
        {
            yield return s.FaceUrl;
            if (s.BackUrl != null) yield return s.BackUrl;
        }
    }

    public IEnumerable<ModuleObj> Walk()
    {
        yield return this;
        if (J["States"] is JsonObject st)
            foreach (var (_, v) in st) if (v is JsonObject o) foreach (var x in new ModuleObj(o).Walk()) yield return x;
        foreach (var c in Children) foreach (var x in c.Walk()) yield return x;
        foreach (var c in Contained) foreach (var x in c.Walk()) yield return x;
    }

    static string? S(JsonNode? n) => ModuleValues.Text(n);
    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    static int I(JsonObject o, string k) => ModuleValues.Whole(o[k], 0);
    static double D(JsonObject? o, string k, double def = 0) => ModuleValues.Finite(o?[k], def);
}

public sealed class ModuleSave
{
    public string WorkshopId { get; init; } = "";
    public string SaveName { get; init; } = "";
    public List<ModuleObj> Objects { get; init; } = new();
    public string Lua { get; init; } = "";
    public string LuaState { get; init; } = "";
    public string? SkyUrl { get; init; }
    public string StockSky { get; init; } = "";
    public string Table { get; init; } = "";
    public string Notes { get; init; } = "";
    public List<(string Title, string Body)> Notebook { get; init; } = new();
    public string Rules { get; init; } = "";
    public List<ModuleObj.Line> VectorLines { get; init; } = new();
    public List<ModuleObj.Decal> TableDecals { get; init; } = new();
    public List<ModuleObj.PaletteEntry> DecalPalette { get; init; } = new();
    public string XmlUi { get; init; } = "";
    public List<(string Name, string Url)> UiAssets { get; init; } = new();

    public static ModuleSave Load(Config cfg, string workshopIdOrPath)
    {
        var path = File.Exists(workshopIdOrPath) ? workshopIdOrPath : Path.Combine(cfg.WorkshopDir, workshopIdOrPath + ".json");
        var stem = Path.GetFileNameWithoutExtension(path);
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException(path);
        return new ModuleSave
        {
            WorkshopId = stem.All(char.IsDigit) ? stem : "",
            SaveName = ModuleValues.Text(root["SaveName"]) ?? stem,
            Objects = (root["ObjectStates"] as JsonArray)?.OfType<JsonObject>().Select(o => new ModuleObj(o)).ToList() ?? new(),
            Lua = ModuleValues.Text(root["LuaScript"]) ?? "",
            LuaState = ModuleValues.Text(root["LuaScriptState"]) ?? "",
            SkyUrl = ModuleValues.Text(root["SkyURL"]) is { } sky && !string.IsNullOrWhiteSpace(sky) ? sky.Trim() : null,
            StockSky = ModuleValues.Text(root["Sky"]) ?? "",
            Table = ModuleValues.Text(root["Table"]) ?? "",
            Notes = ModuleValues.Text(root["Note"]) ?? "",
            Notebook = (root["TabStates"] as JsonObject)?.Select(t => t.Value).OfType<JsonObject>()
                .Select(t => (Title: ModuleValues.Text(t["title"]) ?? "", Body: ModuleValues.Text(t["body"]) ?? ""))
                .Where(t => t.Body.Trim().Length > 0).ToList() ?? new(),
            Rules = (root["TabStates"] as JsonObject)?.Count is > 0 ? "" : ModuleValues.Text(root["Rules"]) ?? "",
            VectorLines = ModuleObj.LinesOf(root["VectorLines"]),
            TableDecals = ModuleObj.DecalsOf(root["Decals"]),
            DecalPalette = ModuleObj.PaletteOf(root["DecalPallet"]),
            XmlUi = ModuleValues.Text(root["XmlUI"]) ?? "",
            UiAssets = ModuleObj.UiAssetsOf(root["CustomUIAssets"]),
        };
    }
}
