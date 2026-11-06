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
    public bool Locked => J["Locked"]?.GetValue<bool>() ?? false;
    public string Lua => S(J["LuaScript"]) ?? "";
    KTable.Core.ScriptSafety.Verdict? _verdict;
    public KTable.Core.ScriptSafety.Verdict Verdict => _verdict ??= KTable.Core.ScriptSafety.Inspect(Lua);
    public string SafeLua => Verdict.Script;
    public string GmNotes => S(J["GMNotes"]) ?? "";
    public string Description => S(J["Description"]) ?? "";
    public string LuaState => S(J["LuaScriptState"]) ?? "";
    public string Memo => S(J["Memo"]) ?? "";
    public List<string> Tags => (J["Tags"] as JsonArray)?.Select(t => S(t) ?? "").Where(t => t.Length > 0).ToList() ?? new();

    JsonObject? T => J["Transform"] as JsonObject;
    public double PosX => D(T, "posX");
    public double PosY => D(T, "posY");
    public double PosZ => D(T, "posZ");
    const double TableScale = 6, UnderTheTable = -0.5, TerrainSpan = 40;
    public static Func<string, (double ex, double ey, double ez)?>? MeshSize { get; set; }
    double Span => MeshUrl is { } u && MeshSize?.Invoke(u) is { } b ? Math.Max(Math.Abs(ScaleX) * b.ex, Math.Abs(ScaleZ) * b.ez) : 0;
    public bool IsTerrain => Name == "Custom_Model" && Locked && PosY >= UnderTheTable && MeshUrl != null && DiffuseUrl != null
                             && (Math.Abs(ScaleX) >= TableScale || Span >= TerrainSpan);
    public bool IsFurniture => ((Name == "Custom_Model" && Math.Abs(ScaleX) >= TableScale && !IsTerrain) || (Locked && PosY < UnderTheTable && Name is "Custom_Model" or "Custom_Tile" or "Custom_Token" or "Custom_Board"))
                               && Top == null;
    public static Func<string, TableTop?>? MeshTop { get; set; }
    public TableTop? Top => Name == "Custom_Model" && Locked && DiffuseUrl != null && MeshUrl is { } u && ScaleX * ScaleZ > 0 && PosY < UnderTheTable
                            && MeshTop?.Invoke(u) is { } t && PosY + t.Top * ScaleY >= UnderTheTable ? t : null;
    public double RotX => D(T, "rotX");
    public double RotY => D(T, "rotY");
    public double RotZ => D(T, "rotZ");
    public double ScaleX => D(T, "scaleX", 1);
    public double ScaleY => D(T, "scaleY", 1);
    public double ScaleZ => D(T, "scaleZ", 1);
    public bool TransformNotANumber => T is { } t && t.Any(f => NotANumber(f.Value));
    public bool Flipped => Near180(RotX) ^ Near180(RotZ);
    static bool Near180(double a) { a = ((a % 360) + 360) % 360; return Math.Abs(a - 180) < 30; }

    JsonObject? CI => J["CustomImage"] as JsonObject;
    public string? ImageUrl => Blank(S(CI?["ImageURL"]));
    public string? BackUrl => Blank(S(CI?["ImageSecondaryURL"]));
    public int TileType => (CI?["CustomTile"] as JsonObject)?["Type"]?.GetValue<int>() ?? 0;
    public bool TileStretch => (CI?["CustomTile"] as JsonObject)?["Stretch"]?.GetValue<bool>() ?? true;
    public double WidthScale => CI?["WidthScale"] is JsonValue v && v.TryGetValue<double>(out var d) ? d : 0;
    public double Thickness =>
        (CI?["CustomTile"] as JsonObject)?["Thickness"]?.GetValue<double>()
        ?? (CI?["CustomToken"] as JsonObject)?["Thickness"]?.GetValue<double>() ?? 0.2;

    JsonObject? CM => J["CustomMesh"] as JsonObject;
    public string? MeshUrl => Blank(S(CM?["MeshURL"]));
    public string? DiffuseUrl => Blank(S(CM?["DiffuseURL"]));
    public string? BundleUrl => Blank(S((J["CustomAssetbundle"] as JsonObject)?["AssetbundleURL"]));
    public string? PdfUrl => Blank(S((J["CustomPDF"] as JsonObject)?["PDFUrl"]));

    public string? Color => J["ColorDiffuse"] is JsonObject c ? HexOf(c) : null;

    public static string HexOf(JsonObject c)
    {
        int r = (int)Math.Round(Math.Clamp(D(c, "r"), 0, 1) * 255), g = (int)Math.Round(Math.Clamp(D(c, "g"), 0, 1) * 255), b = (int)Math.Round(Math.Clamp(D(c, "b"), 0, 1) * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }

    public sealed record Line(List<(double X, double Y, double Z)> Points, string Color, double Thickness, bool Loop);

    public List<Line> VectorLines => LinesOf(J["AttachedVectorLines"]);

    public static List<Line> LinesOf(JsonNode? node)
    {
        const double DefaultThickness = 0.1, Opaque = 1;
        var lines = new List<Line>();
        foreach (var l in (node as JsonArray)?.OfType<JsonObject>() ?? [])
        {
            var points = (l["points3"] as JsonArray)?.OfType<JsonObject>().Select(p => (D(p, "x"), D(p, "y"), D(p, "z"))).ToList() ?? new();
            if (points.Count < 2) continue;
            var c = l["color"] as JsonObject;
            double alpha = c is null ? Opaque : Math.Clamp(D(c, "a", Opaque), 0, Opaque);
            if (alpha <= 0) continue;
            var colour = c is null ? "#FFFFFF" : alpha < Opaque ? $"{HexOf(c)}{(int)Math.Round(alpha * 255):X2}" : HexOf(c);
            lines.Add(new Line(points, colour, D(l, "thickness", DefaultThickness), l["loop"]?.GetValue<bool>() ?? false));
        }
        return lines;
    }

    public bool IsCard => Name is "Card" or "CardCustom";
    public bool IsDeck => Name is "Deck" or "DeckCustom";
    public bool IsChecker => Name is "Checker_white" or "Checker_red" or "Checker_black";
    public bool IsBag => Name is "Bag" or "Custom_Model_Bag";
    public bool Sticky => J["Sticky"] is not JsonValue v || !v.TryGetValue<bool>(out var on) || on;
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
    public int DiceType => (CI?["CustomDice"] as JsonObject)?["Type"]?.GetValue<int>() ?? -1;
    static readonly int[] DiceTypeSides = { 4, 6, 8, 10, 12, 20 };
    public int? StockSides => Name switch
    {
        "Die_4" => 4, "Die_6" or "Die_6_Rounded" => 6, "Die_8" => 8, "Die_10" => 10, "Die_12" => 12, "Die_20" => 20,
        _ => null,
    };

    public sealed record CardSheet(string FaceUrl, string? BackUrl, int Across, int Down, bool UniqueBack, int Type);

    public int CardId => J["CardID"]?.GetValue<int>() ?? 0;

    public Dictionary<int, CardSheet> CardSheets
    {
        get
        {
            var sheets = new Dictionary<int, CardSheet>();
            if (J["CustomDeck"] is not JsonObject deck) return sheets;
            foreach (var (k, v) in deck)
            {
                if (!int.TryParse(k, out var id) || v is not JsonObject s || Blank(S(s["FaceURL"])) is not { } face) continue;
                sheets[id] = new CardSheet(face, Blank(S(s["BackURL"])), Math.Max(1, I(s, "NumWidth")), Math.Max(1, I(s, "NumHeight")),
                    s["UniqueBack"]?.GetValue<bool>() ?? false, I(s, "Type"));
            }
            return sheets;
        }
    }

    public CardSheet? Sheet => CardSheets.GetValueOrDefault(CardId / CardsPerSheet);
    public int SheetCell => CardId % CardsPerSheet;
    const int CardsPerSheet = 100;

    public sealed record Face(string Value, double RotX, double RotY, double RotZ);

    public List<Face> RotationValues => (J["RotationValues"] as JsonArray)?.OfType<JsonObject>()
        .Select(r => (value: r["Value"], rot: r["Rotation"] as JsonObject))
        .Select(r => new Face(r.value is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : r.value?.ToJsonString() ?? "", D(r.rot, "x"), D(r.rot, "y"), D(r.rot, "z")))
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

    public List<Decal> Decals => (J["AttachedDecals"] as JsonArray)?.OfType<JsonObject>()
        .Select(d => (t: d["Transform"] as JsonObject, c: d["CustomDecal"] as JsonObject))
        .Where(d => Blank(S(d.c?["ImageURL"])) != null)
        .Select(d => new Decal(S(d.c!["Name"]) ?? "", Blank(S(d.c!["ImageURL"]))!, D(d.t, "posX"), D(d.t, "posY"), D(d.t, "posZ"), D(d.t, "rotY"), D(d.t, "scaleX", 1), D(d.t, "scaleY", 1)))
        .ToList() ?? new();

    public IEnumerable<string> Urls()
    {
        foreach (var u in new[] { ImageUrl, BackUrl, MeshUrl, DiffuseUrl, BundleUrl, PdfUrl }) if (u != null) yield return u;
        foreach (var d in Decals) yield return d.Url;
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

    static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    static int I(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : (int)Math.Round(D(o, k));
    static double D(JsonObject? o, string k, double def = 0)
    {
        var n = o?[k];
        if (n is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return d;
            if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) && double.IsFinite(d)) return d;
        }
        return def;
    }

    static bool NotANumber(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<string>(out var s)
        && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && !double.IsFinite(d);
}

public sealed class ModuleSave
{
    public string WorkshopId { get; init; } = "";
    public string SaveName { get; init; } = "";
    public List<ModuleObj> Objects { get; init; } = new();
    public string Lua { get; init; } = "";
    public string LuaState { get; init; } = "";
    public string? SkyUrl { get; init; }
    public string Table { get; init; } = "";
    public string Notes { get; init; } = "";
    public List<(string Title, string Body)> Notebook { get; init; } = new();
    public string Rules { get; init; } = "";
    public List<ModuleObj.Line> VectorLines { get; init; } = new();

    public static ModuleSave Load(Config cfg, string workshopIdOrPath)
    {
        var path = File.Exists(workshopIdOrPath) ? workshopIdOrPath : Path.Combine(cfg.WorkshopDir, workshopIdOrPath + ".json");
        var stem = Path.GetFileNameWithoutExtension(path);
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new InvalidDataException(path);
        return new ModuleSave
        {
            WorkshopId = stem.All(char.IsDigit) ? stem : "",
            SaveName = root["SaveName"]?.GetValue<string>() ?? stem,
            Objects = (root["ObjectStates"] as JsonArray)?.OfType<JsonObject>().Select(o => new ModuleObj(o)).ToList() ?? new(),
            Lua = root["LuaScript"]?.GetValue<string>() ?? "",
            LuaState = root["LuaScriptState"]?.GetValue<string>() ?? "",
            SkyUrl = root["SkyURL"]?.GetValue<string>() is { } sky && !string.IsNullOrWhiteSpace(sky) ? sky.Trim() : null,
            Table = root["Table"]?.GetValue<string>() ?? "",
            Notes = root["Note"] is JsonValue note && note.TryGetValue<string>(out var n) ? n : "",
            Notebook = (root["TabStates"] as JsonObject)?.Select(t => t.Value).OfType<JsonObject>()
                .Select(t => (Title: t["title"] is JsonValue tv && tv.TryGetValue<string>(out var ts) ? ts : "", Body: t["body"] is JsonValue bv && bv.TryGetValue<string>(out var bs) ? bs : ""))
                .Where(t => t.Body.Trim().Length > 0).ToList() ?? new(),
            Rules = root["Rules"] is JsonValue rules && rules.TryGetValue<string>(out var rl) ? rl : "",
            VectorLines = ModuleObj.LinesOf(root["VectorLines"]),
        };
    }
}
