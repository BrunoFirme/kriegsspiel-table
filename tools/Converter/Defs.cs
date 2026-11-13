using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server.Scripting.Translate;
using SkiaSharp;

namespace KTable.Converter;

public sealed class PackOut
{
    public PackManifest M { get; }
    public string Dir { get; }
    public string Short { get; }
    public Dictionary<string, PieceDef> ByKey { get; } = new();
    public Dictionary<string, int> Richness { get; } = new();

    public PackOut(string root, string id, string name, string shortName, string? workshopId)
    {
        Dir = Path.Combine(root, id);
        Short = shortName;
        M = new PackManifest { Id = id, Name = name, Source = new PackSource { WorkshopId = workshopId, Url = workshopId != null ? $"https://steamcommunity.com/sharedfiles/filedetails/?id={workshopId}" : "" } };
    }

    public void Save() => Json.Save(Path.Combine(Dir, "pack.json"), M);
}

public sealed record DefResult(PieceDef Def, PackOut Pack, double Scale, int State, ModuleObj Base)
{
    public string? FromPack { get; init; }
    public double Fit { get; init; } = 1;
    public bool Copied { get; init; }
    public bool Figure { get; init; }
    public Lettering? Lettering { get; init; }
}

public sealed partial class DefFactory
{
    readonly Config _cfg;
    readonly AssetStore _assets;
    readonly Report _rep;
    readonly Dictionary<string, (double ex, double ey, double ez)> _meshBounds = new();
    string? _placeholderSha;
    public string Context { get; set; } = "";

    public static readonly HashSet<string> FlatTypes = new() { "Custom_Tile", "Custom_Token", "Custom_Board", "BlockSquare", "BlockRectangle", "Custom_PDF" };
    public static bool IsModel(ModuleObj o) => o.Name is "Custom_Model" or "Custom_Model_Bag";
    public static bool IsPiece(ModuleObj o) => FlatTypes.Contains(o.Name) || IsModel(o) || IsTextObject(o) || IsTablePiece(o);

    public DefFactory(Config cfg, AssetStore assets, Report rep)
    {
        _cfg = cfg;
        _assets = assets;
        _rep = rep;
        ModuleObj.MeshSize = MeshBounds;
        ModuleObj.MeshTop = MeshTopOf;
    }

    readonly Dictionary<string, TableTop?> _tops = new();

    TableTop? MeshTopOf(string url)
    {
        var e = Entry(url);
        if (e == null || e.Ext != "obj") return null;
        if (_tops.TryGetValue(e.Sha!, out var top)) return top;
        try { top = TableTop.Read(_assets.FilePath(e)!); }
        catch (IOException) { top = null; }
        _tops[e.Sha!] = top;
        return top;
    }

    public string? Asset(PackOut pack, string? url, string kind, string where)
    {
        if (url == null) return null;
        var e = _assets.Get(url);
        if (e == null)
        {
            _rep.Missing(url, _assets.Why(url) ?? "unresolved", where);
            return kind == "mesh" ? null : Placeholder(pack);
        }
        if (kind == "image" && e.Ext is not ("png" or "jpg" or "webp" or "gif" or "bmp"))
        {
            _rep.Missing(url, $"not an image ({e.Ext})", where);
            return Placeholder(pack);
        }
        Put(pack, e.Sha!, e.Ext!, _assets.FilePath(e)!, kind, e.W, e.H, url);
        return e.Sha;
    }

    public CacheEntry? Entry(string? url) => url == null ? null : _assets.Get(url);

    TokenOutlines? _tokenOutlines;
    PieceMirror? _mirrors;
    GluedParts? _glued;
    SharedMirror? _shared;
    internal SharedMirror Shared => _shared ??= new SharedMirror(_cfg.Output, _cfg.Cache, _mirrors ??= new PieceMirror(_cfg.Cache));
    CarriedLooks? _carried;
    CarriedLooks Carried => _carried ??= new CarriedLooks(this, _rep);
    public GluedParts Glued => _glued ??= new GluedParts(this, _rep, _cfg.BaseSizes.CustomTokenArea);
    static string PartsKey(PieceDef d) => d.Parts == null && d.States.All(s => s.Parts == null) ? "" : "|p" + Hash8(Json.Serialize(d.Parts) + string.Join(";", d.States.Select(s => Json.Serialize(s.Parts))));
    public string? FlippedPicture(PackOut pack, string? sha) => (_mirrors ??= new PieceMirror(_cfg.Cache)).Picture(pack, sha, new Mirror(true, false, false));
    TokenOutlines Outlines => _tokenOutlines ??= new TokenOutlines(_assets);

    public static void Put(PackOut pack, string sha, string ext, string src, string kind, int w, int h, string? url = null)
    {
        var rel = $"assets/{sha}.{ext}";
        var dst = Path.Combine(pack.Dir, "assets", $"{sha}.{ext}");
        if (!File.Exists(dst))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(src, dst, false);
        }
        if (!pack.M.Assets.TryAdd(sha, new AssetEntry { File = rel, Kind = kind, W = w, H = h, Url = url }) && url != null)
            pack.M.Assets[sha].Url ??= url;
    }

    public string Placeholder(PackOut pack)
    {
        var path = Path.Combine(_cfg.Cache, "placeholder.png");
        if (_placeholderSha == null)
        {
            if (!File.Exists(path))
            {
                using var img = new SKBitmap(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul);
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                        img.SetPixel(x, y, x < 8 || x >= 56 || y < 8 || y >= 56 ? new SKColor(255, 0, 255) :
                            (x is >= 24 and < 40) && (y is >= 12 and < 40 or >= 44 and < 52) ? new SKColor(255, 0, 255) : new SKColor(40, 0, 40));
                SavePng(img, path);
            }
            _placeholderSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        Put(pack, _placeholderSha, "png", path, "image", 64, 64);
        return _placeholderSha;
    }

    (double ex, double ey, double ez)? MeshBounds(string? url)
    {
        var e = Entry(url);
        if (e == null || e.Ext != "obj") return null;
        if (_meshBounds.TryGetValue(e.Sha!, out var b)) return b;
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var (verts, used) = MeshVertices(File.ReadLines(_assets.FilePath(e)!));
        for (int i = 0; i < verts.Count; i++)
        {
            if (used.Count > 0 && !used.Contains(i)) continue;
            var (x, y, z) = verts[i];
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
            minZ = Math.Min(minZ, z);
            maxZ = Math.Max(maxZ, z);
        }
        b = maxX < minX ? (1, 1, 1) : (maxX - minX, maxY - minY, maxZ - minZ);
        _meshBounds[e.Sha!] = b;
        _meshFloor[e.Sha!] = maxY < minY ? 0 : minY;
        return b;
    }

    readonly Dictionary<string, double> _meshFloor = new();

    internal static (List<(double X, double Y, double Z)> Verts, HashSet<int> Used) MeshVertices(IEnumerable<string> lines)
    {
        var verts = new List<(double, double, double)>();
        var used = new HashSet<int>();
        static bool Num(string t, out double v) => double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        foreach (var line in lines)
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 4 && p[0] == "v" && Num(p[1], out var x) && Num(p[2], out var y) && Num(p[3], out var z)) verts.Add((x, y, z));
            else if (p.Length >= 4 && p[0] == "f" && KTable.Core.Model.ModuleMeshes.KeptFace(p))
                foreach (var corner in p.Skip(1))
                    if (int.TryParse(corner.Split('/')[0], out var at) && at != 0) used.Add(at > 0 ? at - 1 : verts.Count + at);
        }
        return (verts, used);
    }

    public double LocalFloor(ModuleObj o) =>
        IsModel(o) && MeshBounds(o.MeshUrl) != null && Entry(o.MeshUrl) is { Sha: { } sha } ? _meshFloor[sha]
        : ModelOfBundle(o) is { Def.ModuleOrigin: { } origin } ? -origin
        : -BaseThickness(o) / 2;

    public double Bottom(ModuleObj o) => o.PosY + LocalFloor(o) * Math.Abs(o.ScaleY);

    public Size2 BaseSize(ModuleObj o)
    {
        var b = _cfg.BaseSizes;
        double aw = 1, ah = 1, aspect = 1;
        if (o.ImageUrl != null && Entry(o.ImageUrl) is { W: > 0, H: > 0 } e)
        {
            double m = Math.Max(e.W, e.H);
            aw = e.W / m;
            ah = e.H / m;
            aspect = e.W / (double)e.H;
        }
        double ratio = o.ScaleX == 0 ? 1 : Math.Abs(o.ScaleZ / o.ScaleX);
        if (o.Name == "Custom_PDF") return new Size2(BookSpan * (PdfOf(o.PdfUrl, o.PdfPassword)?.Aspect ?? 1), BookSpan * ratio);
        if (IsModel(o))
        {
            var mb = MeshBounds(o.MeshUrl) ?? (1, 1, 1);
            return new Size2(mb.ex, mb.ez * ratio);
        }
        if (Boxes.For(o) is { } box) return new Size2(box.Size.W, box.Size.H * ratio);
        if (o.IsCard && o.Sheet is { } sheet) return new Size2(CardWidthOf(sheet), CardHeight * ratio);
        if (o.Name == Figurine) return FigurineSize(o, ratio);
        if (o.DieSides is var n and not (0 or PictureD6)) return new Size2(TemplateDie(n).Size[0], TemplateDie(n).Size[2] * ratio);
        if (o.Name == "Custom_Board") return Mul(new Size2(b.CustomBoard * (o.WidthScale > 0 ? o.WidthScale : aspect), b.CustomBoard * ratio), BoardGrowth);
        return TypeSize(o.Name, aw, ah, aspect, ratio, stretched: o.TileStretch);
    }

    public string Table { get; set; } = "";

    double BoardGrowth => Table switch { "Table_Custom" => 1.6, "Table_RPG" => 1.4, "Table_Circular" => 1.2, _ => 1 };

    Size2 TypeSize(string type, double aw, double ah, double aspect, double ratio, bool stretched)
    {
        var b = _cfg.BaseSizes;
        return type switch
        {
            "Custom_Tile" => stretched ? new Size2(b.CustomTile * aspect, b.CustomTile * ratio) : new Size2(b.CustomTile, b.CustomTile * ratio),
            "Custom_Token" => new Size2(Math.Sqrt(b.CustomTokenArea * aspect), Math.Sqrt(b.CustomTokenArea / aspect) * ratio),
            "Custom_Board" => new Size2(b.CustomBoard * aw, b.CustomBoard * ah * ratio),
            "BlockSquare" => new Size2(b.BlockSquare, b.BlockSquare * ratio),
            "BlockRectangle" => new Size2(b.BlockSquare, 2 * b.BlockSquare * ratio),
            TriangleBlock => new Size2(TriangleAcross * b.BlockSquare, b.BlockSquare * ratio),
            "Card" or "CardCustom" => new Size2(CardWidth, CardHeight * ratio),
            "Checker_white" or "Checker_red" or "Checker_black" => new Size2(CheckerAcross, CheckerAcross * ratio),
            "Custom_Dice" => new Size2(DieAcross, DieAcross * ratio),
            "Bag" => new Size2(BagAcross, BagAcross * ratio),
            "PlayerPawn" => new Size2(PawnAcross, PawnAcross * ratio),
            _ => new Size2(1, ratio),
        };
    }

    void SetFlatFrame(PieceDef def, ModuleObj b, double heightScale)
    {
        double rise = Math.Abs(b.ScaleY) * heightScale;
        double low = b.Name == "Custom_Token" ? -BaseThickness(b) / 2 : 0;
        def.ModuleRise = Math.Round(rise, 4);
        def.ModuleOrigin = Math.Round(-low * rise, 4);
    }

    public double BaseThickness(ModuleObj o) => o.Name switch
    {
        "Custom_Tile" or "Custom_Token" => o.Thickness,
        "Custom_PDF" => BookThickness,
        "Custom_Model" or "Custom_Model_Bag" => (MeshBounds(o.MeshUrl)?.ey ?? 0.2),
        "Custom_Assetbundle" when ModelOfBundle(o) is { } figure => figure.Def.Thickness,
        "BlockSquare" or "BlockRectangle" => _cfg.BaseSizes.BlockSquare,
        TriangleBlock => TriangleRise * _cfg.BaseSizes.BlockSquare,
        "Card" or "CardCustom" => CardThickness,
        "Checker_white" or "Checker_red" or "Checker_black" => CheckerThickness,
        "Custom_Dice" => o.DieSides is var n and not (0 or PictureD6) ? TemplateDie(n).Size[1] : DieAcross,
        Figurine => FigurineTopOf(o),
        "Bag" => Boxes.For(o)?.Thickness ?? BagThickness,
        "PlayerPawn" => PawnHeight,
        "Notecard" or "3DText" => TextThickness,
        _ => 0.2,
    };

    public const double TextThickness = 0.02;

    static Size2 Mul(Size2 s, double k) => new(Math.Round(s.W * k, 4), Math.Round(s.H * k, 4));

    public (double X, double Z) LocalUnits(PieceDef def, ModuleObj o)
    {
        if (def.LocalUnit is { } lu) return (lu.W, lu.H);
        var size = def.Size;
        if (def.Shape == "alpha" && o.ImageUrl != null && Entry(o.ImageUrl) is { W: > 0, H: > 0 } e)
        {
            double aspect = e.W / (double)e.H, area = _cfg.BaseSizes.CustomTokenArea;
            return (size.W / Math.Sqrt(area * aspect), size.H / Math.Sqrt(area / aspect));
        }
        return (size.H / 2, size.H / 2);
    }

    public string VisualKey(ModuleObj o)
    {
        var (states, _) = o.StateList();
        var b = states[0];
        var pdf = b.PdfUrl is { } url ? $"|pdf:{Entry(url)?.Sha ?? url}" : "";
        return $"{b.Name}|{Entry(b.ImageUrl)?.Sha ?? b.ImageUrl}|{Entry(b.BackUrl)?.Sha ?? b.BackUrl}|{Entry(b.MeshUrl)?.Sha ?? b.MeshUrl}|{Entry(b.DiffuseUrl)?.Sha ?? b.DiffuseUrl}|{(b.Name.StartsWith("Block") ? Rgb(b.Color) : "")}{TableVisualKey(b)}{pdf}";
    }

    string? MeshRefusal(string? url) =>
        Entry(url) is { Ext: "obj" } e && _assets.FilePath(e) is { } file && KTable.Core.Model.ModuleMeshes.Refusal(File.ReadLines(file)) is { } why
            ? $"a model file the digital tabletop cannot read either ({why})" : null;

    public PieceStateDef? OwnLook(ModuleObj s, PackOut pack, double baseScale, string where, double heightScale)
    {
        double k = Math.Abs(s.ScaleX) < NoScale ? 1 : Math.Abs(s.ScaleX) / baseScale;
        if (!Shows(s) || BuildDef(s, pack, k, where, heightScale) is not { } d) return null;
        return new PieceStateDef
        {
            Name = s.Nickname.Length > 0 ? s.Nickname : d.Name, Kind = d.Kind, Shape = d.Shape, Image = d.Image, Back = d.Back, Mesh = d.Mesh, Texture = d.Texture,
            Outline = d.Outline, Size = d.Size, Thickness = d.Kind == "model" ? d.Thickness : Math.Round(d.Thickness / baseScale, 3), Tint = d.Tint,
        };
    }

    string StretchKey(ModuleObj b) => IsModel(b) && Stretch(b) is var k && k != 1 ? string.Create(CultureInfo.InvariantCulture, $"|y{k:0.###}") : "";

    string LooksKey(List<ModuleObj> states) => states.Count < 2 ? ""
        : "|lk" + Hash8(string.Join(";", states.Select(s => string.Create(CultureInfo.InvariantCulture, $"{VisualKey(s)}|{Math.Abs(s.ScaleX):0.####}|{Hash8(s.SafeLua)}"))));

    public const double BookThickness = 0.2, BookSpan = 4;

    public static double BaseScale(ModuleObj o) => Math.Round(Math.Abs(o.StateList().states[0].ScaleX), 4);

    public BundleModels Models => _models ??= BundleModels.Load(_cfg.Output, BundleBake.Dir(_cfg.Cache));
    public BagBoxes Boxes => _boxes ??= BagBoxes.Load(_cfg.Output);

    public const string TilesetTree = "Tileset_Tree";
    const double TreeCrownAcross = 3.62;
    const string TreeMissing = "Firme's Effects and Props is not installed here, so the module's trees were left out; open the app once and convert again for trees.";
    bool _treeMissingNoted;
    Dictionary<string, PieceDef>? _firme;

    PieceDef? FirmeProp(string name)
    {
        if (_firme == null)
        {
            var path = Path.Combine(_cfg.Output, KTable.Client.Effects.EffectKinds.StandardPack, "pack.json");
            _firme = File.Exists(path) ? Json.Load<PackManifest>(path).Pieces.Where(p => p.Kind == "model").ToDictionary(p => p.Id) : new();
        }
        return _firme.GetValueOrDefault($"{KTable.Client.Effects.EffectKinds.StandardPack}.{name}");
    }

    DefResult? TreeDef(ModuleObj o, ModuleObj b, PackOut pack)
    {
        if (FirmeProp("tree") is not { Size.W: > 0 } tree)
        {
            if (!_treeMissingNoted) _rep.Note(TreeMissing);
            _treeMissingNoted = true;
            return null;
        }
        double fit = TreeCrownAcross / tree.Size.W;
        return new DefResult(tree, pack, Math.Round(Math.Abs(o.ScaleX) * fit, 4), 0, b) { FromPack = KTable.Client.Effects.EffectKinds.StandardPack, Fit = fit };
    }
    BagBoxes? _boxes;
    BundleModels? _models;

    public (PieceDef Def, string Pack)? ModelOfBundle(ModuleObj o) =>
        o.Name == "Custom_Assetbundle" && !_cfg.IsTableWall(o.BundleUrl) ? Models.For(o.BundleUrl) : null;

    const double GroundFlatness = 0.25;

    public (string Mesh, string Picture)? TerrainOfBundle(ModuleObj o)
    {
        if (!o.Locked || ModelOfBundle(o) is not { Def: { Mesh: { } mesh, Texture: { } picture } def }) return null;
        double wide = def.Size.W * Math.Abs(o.ScaleX), deep = def.Size.H * Math.Abs(o.ScaleZ), high = def.Thickness * Math.Abs(o.ScaleY);
        double top = o.PosY + (def.Thickness - (def.ModuleOrigin ?? 0)) * Math.Abs(o.ScaleY);
        if (Math.Max(wide, deep) < ModuleObj.TerrainSpan || high > GroundFlatness * Math.Min(wide, deep) || top < ModuleObj.UnderTheTable) return null;
        return Models.FileOf(o.BundleUrl, mesh) is { } meshFile && Models.FileOf(o.BundleUrl, picture) is { } pictureFile ? (meshFile, pictureFile) : null;
    }

    public const double NoScale = 1e-6, HiddenByScale = 0.1;

    public bool Shows(ModuleObj o) => IsPiece(o) || ModelOfBundle(o) != null || o.StockSides != null || o.Name == TilesetTree;

    DefResult? StandardDef(ModuleObj o, ModuleObj b, PackOut pack, double bs, double instScale)
    {
        if (ModelOfBundle(o) is { } model)
            return new DefResult(model.Def, pack, instScale, 0, b) { FromPack = model.Pack };
        if (o.StockSides is { } sides && StandardDie(sides) is { } die)
            return new DefResult(die, pack, Math.Round(bs, 4), FaceShown(o, die), b) { FromPack = StdDicePack, Fit = StockDieFit(o.Name, die) };
        if (Boxes.For(o) is { } box)
        {
            double fit = box.Size.W > 0 ? Math.Round(BagAcross / box.Size.W, 4) : 1;
            return new DefResult(box, pack, Math.Round(bs, 4), 0, b) { FromPack = KTable.Client.Effects.EffectKinds.StandardPack, Fit = fit };
        }
        if (o.Name == TilesetTree) return TreeDef(o, b, pack);
        return null;
    }

    public DefResult? DefFor(ModuleObj o, PackOut pack, double? refScale = null, double heightScale = 1, bool glued = false)
    {
        if (!Shows(o)) return null;
        if (IsTextObject(o)) return TextDef(o, pack, refScale);
        var (states, self) = o.StateList();
        var b = states[0];
        double bs = Math.Abs(b.ScaleX) < NoScale ? 1 : Math.Abs(b.ScaleX);
        double r = refScale is > NoScale ? refScale.Value : bs;
        double instScale = Math.Round(bs / r, 4);
        if (StandardDef(o, b, pack, bs, instScale) is { } standard)
            return Carried.Of(o, states, self, Shared.Adopted(Shared.Shaped(b, Shared.Of(o, standard, pack), pack), pack), pack, bs, heightScale);
        if (o.Name == TilesetTree) return null;
        var where = $"{Context} {o.Name} '{o.Nickname}' {o.Guid}";

        var def = BuildDef(b, pack, r, where, heightScale);
        if (def == null) return null;
        if (RollsOnItsValues(b)) FillModelFaces(def, b);
        if (o.TransformNotANumber) _rep.Note($"{where}: the module gives this piece a position, turn or scale that is not a number; the usual value is taken instead");
        var lua = glued ? "" : b.SafeLua.Length > 0 ? b.SafeLua : o.SafeLua;
        string scriptKey = "";
        if (lua.Trim().Length > 0)
        {
            def.Script = Translate(lua, pack, where, owner: b).Script;
            def.ScriptHash = Hash8(lua);
            def.ScriptState = o.LuaState.Trim().Length > 0 ? RewriteUrls(o.LuaState, pack, where) : null;
            def.Counters.Clear();
            def.TextLines.Clear();
            scriptKey = "|" + def.ScriptHash;
        }

        if (states.Count > 1)
        {
            foreach (var s in states)
            {
                double k = Math.Abs(s.ScaleX) / bs * r;
                var sz = Mul(BaseSize(s), k);
                var sd = new PieceStateDef { Name = s.Nickname.Length > 0 ? s.Nickname : $"State {def.States.Count + 1}" };
                var lookTint = s.Color ?? "#FFFFFF";
                if (!string.Equals(lookTint, def.Tint, StringComparison.OrdinalIgnoreCase)) sd.Tint = lookTint;
                var stShape = PackManifest.ShapeOf(s.Name, s.TileType);
                if (stShape != def.Shape) sd.Shape = stShape;
                if (IsModel(s)) { sd.Mesh = ModelMesh(pack, s, where); sd.Texture = Asset(pack, s.DiffuseUrl, "image", where); }
                else if (s.Name.StartsWith("Block")) sd.Image = WhiteSquare(pack);
                else { sd.Image = Asset(pack, s.ImageUrl, "image", where); sd.Back = s.Name == "Custom_Token" ? null : Asset(pack, s.BackUrl, "image", where); }
                if (IsModel(s) != IsModel(b)) sd.Kind = IsModel(s) ? "model" : "flat";
                double lookThick = Math.Round(Math.Clamp(BaseThickness(s) * Math.Abs(s.ScaleY) * (IsModel(s) ? r / bs : heightScale), 0.02, 3), 3);
                if (Math.Abs(lookThick - def.Thickness) > 0.001) sd.Thickness = lookThick;
                if (s.Name == "Custom_Token") sd.Outline = Outlines.Of(s.ImageUrl, s.MergeDistance);
                sd.Script = glued || s.SafeLua.Trim().Length == 0 ? "" : Translate(s.SafeLua, pack, $"{where} state \"{sd.Name}\"", owner: s).Script;
                if (s.TransformNotANumber) _rep.Note($"{where}: the module scales the state \"{sd.Name}\" by something that is not a number; it takes the piece's own size");
                if (Math.Abs(sz.W - def.Size.W) > 0.01 * def.Size.W || Math.Abs(sz.H - def.Size.H) > 0.01 * def.Size.H) sd.Size = sz;
                def.States.Add(sd);
            }
        }

        ModuleSources.States(def, states, (text, at) => RewriteUrls(text, pack, at), where);
        if (b.Name is "Custom_Token" or "Custom_Tile") SetFlatFrame(def, b, heightScale);
        for (int i = 0; i < def.States.Count && i < states.Count; i++)
        {
            var sd = def.States[i];
            var decals = ScenarioBuilder.DecalsOf(states[i], sd.Size ?? def.Size, def.Tags.Contains("token"),
                _cfg.BaseSizes.CustomTokenArea, url => UiAsset(pack, url, $"{where} state \"{sd.Name}\" decal"), sha => FlippedPicture(pack, sha));
            if (decals.Count > 0) sd.Decals = decals;
        }
        Glued.Fill(def, states, bs, r, heightScale, pack, where, Mirror.Of(o));

        var mirror = Mirror.Of(o);
        (_mirrors ??= new PieceMirror(_cfg.Cache)).Apply(def, pack, mirror);
        var decalKey = def.States.Any(s => s.Decals is { Count: > 0 })
            ? "|d" + Hash8(string.Join(";", def.States.Select(s => string.Join(",", (s.Decals ?? new()).Select(d => d.Picture)))))
            : "";
        var gluedKey = (heightScale == 1 && !b.IsGlued) || IsModel(b) ? "" : string.Create(CultureInfo.InvariantCulture, $"|t{def.Thickness:0.###}");
        var facesKey = !RollsOnItsValues(o) ? "" : "|f" + Hash8(string.Join(";", def.Faces.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Value}@{f.Up[0]},{f.Up[1]},{f.Up[2]}"))));
        var surfaceKey = (def.Shadowless ? "|ns" : "") + (def.Normal is { } normal ? "|n" + normal : "") + TokenOutlines.MergeKey(o) + (b.StandsUp ? "|up" : "");
        var lookTints = def.States.Any(s => s.Tint != null) ? "|lt" + Hash8(string.Join(",", def.States.Select(s => s.Tint ?? ""))) : "";
        var key = string.Create(CultureInfo.InvariantCulture, $"{VisualKey(o)}|{def.Size.W:0.###}x{def.Size.H:0.###}{scriptKey}{decalKey}{gluedKey}{facesKey}{surfaceKey}{mirror.Key}{PartsKey(def)}{lookTints}{LooksKey(states)}{StretchKey(b)}");
        int rich = def.States.Count * 100 + def.Counters.Count * 10 + def.TextLines.Count + (def.Parts?.Count ?? 0) * 1000 + (def.Script != null ? 5000 : 0);
        if (pack.ByKey.TryGetValue(key, out var existing))
        {
            if (rich > pack.Richness.GetValueOrDefault(existing.Id))
            {
                existing.States = def.States;
                existing.Counters = def.Counters;
                existing.TextLines = def.TextLines;
                existing.Attachments = def.Attachments;
                existing.Parts = def.Parts;
                existing.Script = def.Script;
                existing.ScriptHash = def.ScriptHash;
                existing.ScriptState = def.ScriptState;
                if (existing.Name == "Piece" && def.Name != "Piece") existing.Name = def.Name;
                pack.Richness[existing.Id] = rich;
            }
            return new DefResult(existing, pack, instScale, Shown(o, existing, self), b);
        }
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = rich;
        pack.M.Pieces.Add(def);
        return new DefResult(def, pack, instScale, Shown(o, def, self), b);
    }

    public static int Shown(ModuleObj o, PieceDef def, int self) =>
        o.IsPictureDie || RollsOnItsValues(o) ? FaceShown(o, def) : o.Name == "Custom_PDF" ? Math.Min(o.PdfPage, Math.Max(0, def.States.Count - 1)) : self;

    public TranslateResult Translate(string lua, PackOut pack, string where, bool isGlobal = false, ModuleObj? owner = null)
    {
        var tr = ModuleTranslator.Translate(lua, new TranslateContext
        {
            IsGlobal = isGlobal,
            Owner = owner == null ? null : new SpawnLook(owner.Name, Math.Abs(owner.ScaleX), Math.Abs(owner.ScaleY), Math.Abs(owner.ScaleZ), new Dictionary<string, string>()),
            ResolveLook = look => SpawnedKind(pack, look, where)?.Id,
            ResolveData = data => DefFor(new ModuleObj(data), pack)?.Def.Id,
            ResolveAsset = url => UiAsset(pack, url, where),
        });
        _rep.ScriptTranslated(tr, where);
        return tr;
    }

    public string RewriteUrls(string text, PackOut pack, string where)
    {
        if (text.Length == 0 || !text.Contains("http")) return text;
        return System.Text.RegularExpressions.Regex.Replace(text, @"https?://[^""\\\s]+", m => UiAsset(pack, m.Value, where) ?? m.Value);
    }

    internal string? UiAsset(PackOut pack, string url, string where)
    {
        var e = _assets.Get(url);
        if (e == null || e.Ext is not ("png" or "jpg" or "webp" or "gif" or "bmp")) return null;
        return Asset(pack, url, "image", where);
    }

    PieceDef? BuildDef(ModuleObj b, PackOut pack, double r, string where, double heightScale)
    {
        var (lookUrl, lookKind) = IsModel(b) ? (b.MeshUrl, "mesh") : b.Name == "Custom_PDF" ? (b.PdfUrl, "pdf")
            : b.Name.StartsWith("Block") || b.IsChecker ? (null, "") : b.IsCard ? (b.Sheet?.FaceUrl, "image") : (b.ImageUrl, "image");
        if ((_assets.Gone(lookUrl, lookKind) ?? (IsModel(b) ? MeshRefusal(b.MeshUrl) : null)) is { } gone)
        {
            _rep.LeftOut(Name(b) is var n && n != "Piece" ? n : FileName(lookUrl!), lookUrl!, gone, where, b.Guid);
            return null;
        }
        var size = Mul(BaseSize(b), r);
        var def = new PieceDef
        {
            Name = Name(b),
            Size = size,
            LocalUnit = IsModel(b) ? new Size2(Math.Round(r, 4), Math.Round(r * (b.ScaleX == 0 ? 1 : Math.Abs(b.ScaleZ / b.ScaleX)), 4)) : null,
            Thickness = Math.Round(Math.Clamp(BaseThickness(b) * Math.Abs(b.ScaleY) * (IsModel(b) ? r / Math.Max(1e-6, Math.Abs(b.ScaleX)) : heightScale), 0.02, 3), 3),
            Tint = b.Color ?? "#FFFFFF",
            TintsPictures = b.Name == "Custom_Token" || b.Name.StartsWith("Block"),
            Shape = PackManifest.ShapeOf(b.Name, b.TileType),
            Tags = b.Tags.Concat(new[] { b.Name switch { "Custom_Tile" => "tile", "Custom_Token" => "token", "Custom_Model" or "Custom_Model_Bag" => "model", "Custom_Board" => "board", "Custom_PDF" => "book", _ => "block" } })
                .Concat(b.IsBag ? ["bag"] : []).Distinct().ToList(),
        };
        if (IsTablePiece(b))
        {
            FillTablePiece(def, b, pack, where);
        }
        else if (IsModel(b))
        {
            def.Kind = "model";
            def.HeightScale = 1;
            def.Mesh = ModelMesh(pack, b, where);
            def.Texture = Asset(pack, b.DiffuseUrl, "image", where);
            def.Normal = Asset(pack, b.NormalUrl, "image", where);
            def.Shadowless = !b.CastsShadows;
            def.Finish = b.Finish;
            def.Shine = b.Shine;
            if (def.Mesh == null) { def.Kind = "flat"; def.Image = def.Texture ?? Placeholder(pack); }
        }
        else if (b.Name == "Custom_PDF")
        {
            FillBook(def, b, pack, where);
        }
        else if (b.Name.StartsWith("Block"))
        {
            def.Image = WhiteSquare(pack);
        }
        else
        {
            def.Image = Asset(pack, b.ImageUrl, "image", where);
            def.Back = b.Name == "Custom_Token" ? null : Asset(pack, b.BackUrl, "image", where);
            if (b.Name == "Custom_Token") def.Outline = Outlines.Of(b.ImageUrl, b.MergeDistance);
            def.Image ??= Placeholder(pack);
            if (b.Name == "Custom_Token" && b.StandsUp) StandUp(def, pack);
        }
        if (def.Name == "Piece") def.Name = Describe(pack, def);
        ModuleSources.Fill(def, b);
        return def;
    }

    string? _whiteSha;
    string WhiteSquare(PackOut pack)
    {
        var path = Path.Combine(_cfg.Cache, "white.png");
        if (_whiteSha == null)
        {
            if (!File.Exists(path))
            {
                using var img = new SKBitmap(16, 16);
                img.Erase(SKColors.White);
                SavePng(img, path);
            }
            _whiteSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
        }
        Put(pack, _whiteSha, "png", path, "image", 16, 16);
        return _whiteSha;
    }

    static string Describe(PackOut pack, PieceDef def)
    {
        if (def.Tags.Contains("book")) return def.States.Count > 1 ? $"Book of {def.States.Count} pages" : "Book";
        var form = def.Kind == "model" ? "model" : def.Shape switch
        {
            "alpha" => "token", "circle" => "disc", "hex" => "hex",
            _ => (Math.Max(def.Size.W, def.Size.H) / Math.Max(1e-6, Math.Min(def.Size.W, def.Size.H))) switch { > 2.5 => "bar", > 1.3 => "block", _ => "square" },
        };
        var hash = def.Image ?? def.Texture;
        string colour = "";
        if (hash != null && pack.M.Assets.TryGetValue(hash, out var a))
        {
            colour = BestEffort.Read(() => ColourName(Path.Combine(pack.Dir, a.File)), fallback: colour);
        }
        var name = (colour.Length > 0 ? colour + " " : "") + form;
        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    static string ColourName(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec == null) return "";
        var dims = codec.GetScaledDimensions(Math.Min(1f, 96f / Math.Max(codec.Info.Width, codec.Info.Height)));
        using var bmp = SKBitmap.Decode(codec, new SKImageInfo(Math.Max(1, dims.Width), Math.Max(1, dims.Height), SKColorType.Rgba8888, SKAlphaType.Unpremul));
        if (bmp == null) return "";
        var hues = new double[24];
        double grey = 0, greySum = 0, sat = 0;
        foreach (var c in bmp.Pixels)
        {
            if (c.Alpha < 128) continue;
            c.ToHsv(out var h, out var sv, out var v);
            if (sv > 35 && v > 25) { hues[(int)(h / 15) % 24] += sv * v; sat++; }
            else { grey++; greySum += v; }
        }
        if (sat < (sat + grey) * 0.12) return grey == 0 ? "" : (greySum / grey) switch { < 25 => "black", > 80 => "white", _ => "grey" };
        int best = Array.IndexOf(hues, hues.Max());
        double centre = best * 15 + 7.5;
        return centre switch
        {
            < 18 or >= 340 => "red", < 45 => "orange", < 70 => "yellow", < 170 => "green",
            < 255 => "blue", < 290 => "purple", _ => "pink",
        };
    }

    static void SavePng(SKBitmap bmp, string path)
    {
        using var data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }

    static string Name(ModuleObj b)
    {
        if (b.Nickname.Length > 0) return b.Nickname;
        try
        {
            if (b.LuaState.StartsWith('{'))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(b.LuaState);
                foreach (var k in new[] { "name", "name1" })
                    if (doc.RootElement.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String && v.GetString() is { Length: > 0 } s) return s;
            }
        }
        catch (System.Text.Json.JsonException) { }
        return "Piece";
    }

    static string FileName(string url)
    {
        var last = url.Trim().TrimEnd('/', '\\').Split('?')[0];
        var stem = Path.GetFileNameWithoutExtension(last[(last.LastIndexOfAny(new[] { '/', '\\' }) + 1)..]);
        return stem.Length > 0 ? stem : "Piece";
    }

    [GeneratedRegex(@"[^a-z0-9]+")] private static partial Regex SlugRx();
    public static string Slug(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in norm) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        var slug = SlugRx().Replace(sb.ToString().ToLowerInvariant(), "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].Trim('-');
        return slug.Length == 0 ? "piece" : slug;
    }
    public static string Hash8(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)))[..8].ToLowerInvariant();
}
