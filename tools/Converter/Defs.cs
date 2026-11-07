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

public sealed record DefResult(PieceDef Def, PackOut Pack, double Scale, int State, ModuleObj Base, ScriptInfo Script)
{
    public string? FromPack { get; init; }
    public double Fit { get; init; } = 1;
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
        foreach (var line in File.ReadLines(_assets.FilePath(e)!))
        {
            if (line.Length < 2 || line[0] != 'v' || line[1] != ' ') continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 4) continue;
            if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var z)) continue;
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

    public double LocalFloor(ModuleObj o) =>
        IsModel(o) && MeshBounds(o.MeshUrl) != null && Entry(o.MeshUrl) is { Sha: { } sha }
            ? _meshFloor[sha]
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
        double ratio = o.ScaleX == 0 ? 1 : o.ScaleZ / o.ScaleX;
        if (o.Name == "Custom_PDF") return new Size2(b.CustomTile * (PdfOf(o.PdfUrl, o.PdfPassword)?.Aspect ?? 1), b.CustomTile * ratio);
        if (IsModel(o))
        {
            var mb = MeshBounds(o.MeshUrl) ?? (1, 1, 1);
            return new Size2(mb.ex, mb.ez * ratio);
        }
        if (Boxes.For(o) is { } box) return new Size2(box.Size.W, box.Size.H * ratio);
        if (o.Name == Figurine) return FigurineSize(o, ratio);
        if (o.DieSides is var n and not (0 or PictureD6)) return new Size2(TemplateDie(n).Size[0], TemplateDie(n).Size[2] * ratio);
        if (o.Name == "Custom_Board") return Mul(new Size2(b.CustomBoard * (o.WidthScale > 0 ? o.WidthScale : aspect), b.CustomBoard * ratio), BoardGrowth);
        return TypeSize(o.Name, aw, ah, aspect, ratio, stretched: o.TileType is 0 or 3 && o.TileStretch);
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

    public double BaseThickness(ModuleObj o) => o.Name switch
    {
        "Custom_Tile" or "Custom_Token" => o.Thickness,
        "Custom_PDF" => BookThickness,
        "Custom_Model" or "Custom_Model_Bag" => (MeshBounds(o.MeshUrl)?.ey ?? 0.2),
        "BlockSquare" or "BlockRectangle" => _cfg.BaseSizes.BlockSquare,
        TriangleBlock => TriangleRise * _cfg.BaseSizes.BlockSquare,
        "Card" or "CardCustom" => CardThickness,
        "Checker_white" or "Checker_red" or "Checker_black" => CheckerThickness,
        "Custom_Dice" => o.DieSides is var n and not (0 or PictureD6) ? TemplateDie(n).Size[1] : DieAcross,
        Figurine => FigurineTop,
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
        if (def.Shape == "alpha" && o.ImageUrl != null && Entry(o.ImageUrl) is { W: > 0, H: > 0 } e)
        {
            double aspect = e.W / (double)e.H, area = _cfg.BaseSizes.CustomTokenArea;
            return (def.Size.W / Math.Sqrt(area * aspect), def.Size.H / Math.Sqrt(area / aspect));
        }
        return (def.Size.H / 2, def.Size.H / 2);
    }

    public string VisualKey(ModuleObj o)
    {
        var (states, _) = o.StateList();
        var b = states[0];
        var pdf = b.PdfUrl is { } url ? $"|pdf:{Entry(url)?.Sha ?? url}" : "";
        return $"{b.Name}|{Entry(b.ImageUrl)?.Sha ?? b.ImageUrl}|{Entry(b.BackUrl)?.Sha ?? b.BackUrl}|{Entry(b.MeshUrl)?.Sha ?? b.MeshUrl}|{Entry(b.DiffuseUrl)?.Sha ?? b.DiffuseUrl}|{(b.Name.StartsWith("Block") ? Rgb(b.Color) : "")}{TableVisualKey(b)}{pdf}";
    }

    public const double BookThickness = 0.1;

    public static double BaseScale(ModuleObj o) => Math.Round(Math.Abs(o.StateList().states[0].ScaleX), 4);

    public BundleModels Models => _models ??= BundleModels.Load(_cfg.Output);
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

    DefResult? TreeDef(ModuleObj o, ModuleObj b, PackOut pack, ScriptInfo script)
    {
        if (FirmeProp("tree") is not { Size.W: > 0 } tree)
        {
            if (!_treeMissingNoted) _rep.Note(TreeMissing);
            _treeMissingNoted = true;
            return null;
        }
        double fit = TreeCrownAcross / tree.Size.W;
        return new DefResult(tree, pack, Math.Round(Math.Abs(o.ScaleX) * fit, 4), 0, b, script) { FromPack = KTable.Client.Effects.EffectKinds.StandardPack, Fit = fit };
    }
    BagBoxes? _boxes;
    BundleModels? _models;

    public (PieceDef Def, string Pack)? ModelOfBundle(ModuleObj o) =>
        o.Name == "Custom_Assetbundle" && !_cfg.IsTableWall(o.BundleUrl) ? Models.For(o.BundleUrl) : null;

    public const double NoScale = 1e-6, HiddenByScale = 0.1;

    public bool Shows(ModuleObj o) => IsPiece(o) || ModelOfBundle(o) != null || o.StockSides != null || o.Name == TilesetTree;

    DefResult? StandardDef(ModuleObj o, ModuleObj b, PackOut pack, double bs, double instScale)
    {
        if (ModelOfBundle(o) is { } model)
            return new DefResult(model.Def, pack, instScale, 0, b, ScriptInfo.Parse("", "")) { FromPack = model.Pack };
        var script = ScriptInfo.Parse(b.SafeLua.Length > 0 ? b.SafeLua : o.SafeLua, o.LuaState);
        if (o.StockSides is { } sides && StandardDie(sides) is { } die)
            return new DefResult(die, pack, Math.Round(bs, 4), FaceShown(o, die), b, script) { FromPack = StdDicePack };
        if (Boxes.For(o) is { } box)
            return new DefResult(box, pack, Math.Round(bs, 4), 0, b, script) { FromPack = KTable.Client.Effects.EffectKinds.StandardPack };
        if (o.Name == TilesetTree) return TreeDef(o, b, pack, script);
        return null;
    }

    public DefResult? DefFor(ModuleObj o, PackOut pack, double? refScale = null, double heightScale = 1)
    {
        if (!Shows(o)) return null;
        if (IsTextObject(o)) return TextDef(o, pack, refScale);
        var (states, self) = o.StateList();
        var b = states[0];
        double bs = Math.Abs(b.ScaleX) < NoScale ? 1 : Math.Abs(b.ScaleX);
        double r = refScale is > NoScale && !BakesChildren(o) ? refScale.Value : bs;
        double instScale = Math.Round(bs / r, 4);
        if (StandardDef(o, b, pack, bs, instScale) is { } standard) return standard;
        if (o.Name == TilesetTree) return null;
        var where = $"{Context} {o.Name} '{o.Nickname}' {o.Guid}";

        var script = ScriptInfo.Parse(b.SafeLua.Length > 0 ? b.SafeLua : o.SafeLua, o.LuaState);
        var def = BuildDef(b, pack, r, where, script, heightScale);
        if (def == null) return null;
        if (RollsOnItsValues(b)) FillModelFaces(def, b);
        if (o.TransformNotANumber) _rep.Note($"{where}: the module gives this piece a position, turn or scale that is not a number; the usual value is taken instead");
        var lua = b.SafeLua.Length > 0 ? b.SafeLua : o.SafeLua;
        string scriptKey = "";
        if (lua.Trim().Length > 0)
        {
            def.Script = Translate(lua, pack, where).Script;
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
                var stShape = PackManifest.ShapeOf(s.Name, s.TileType);
                if (stShape != def.Shape) sd.Shape = stShape;
                if (IsModel(s)) { sd.Mesh = ModelMesh(pack, s, where); sd.Texture = Asset(pack, s.DiffuseUrl, "image", where); }
                else { sd.Image = Asset(pack, s.ImageUrl, "image", where); sd.Back = Asset(pack, s.BackUrl, "image", where); }
                if (s.Lua.Trim().Length > 0 && !ReferenceEquals(s, states[0])) { sd.Script = s.Lua; _rep.Note($"{where}: the state \"{sd.Name}\" carries a script of its own (kept, not run)"); }
                if (s.TransformNotANumber) _rep.Note($"{where}: the module scales the state \"{sd.Name}\" by something that is not a number; it takes the piece's own size");
                if (Math.Abs(sz.W - def.Size.W) > 0.01 * def.Size.W || Math.Abs(sz.H - def.Size.H) > 0.01 * def.Size.H) sd.Size = sz;
                def.States.Add(sd);
            }
        }

        bool baked = BakesChildren(o) && BakeStates(def, states, bs, r, heightScale, pack);
        for (int i = 0; i < def.States.Count && i < states.Count; i++)
        {
            var sd = def.States[i];
            var decals = ScenarioBuilder.DecalsOf(states[i], sd.Size ?? def.Size, def.Tags.Contains("token"),
                _cfg.BaseSizes.CustomTokenArea, url => UiAsset(pack, url, $"{where} state \"{sd.Name}\" decal"));
            if (decals.Count > 0) sd.Decals = decals;
        }
        foreach (var c in baked ? Enumerable.Empty<ModuleObj>() : o.Children)
        {
            if (!IsPiece(c)) { _rep.Skipped("child " + c.Name, where); continue; }
            var cr = DefFor(c, pack, Math.Abs(c.StateList().states[0].ScaleX) * r, heightScale * Math.Abs(b.ScaleY));
            if (cr == null) continue;
            def.Attachments.Add(new AttachmentDef { Piece = cr.Def.Id, Offset = new Vec2(Math.Round(c.PosX * r, 4), Math.Round(c.PosZ * r * (b.ScaleZ / b.ScaleX), 4)) });
        }

        var bakedKey = baked ? "|" + string.Join(",", def.States.Select(s => s.Image)) : "";
        var decalKey = def.States.Any(s => s.Decals is { Count: > 0 })
            ? "|d" + Hash8(string.Join(";", def.States.Select(s => string.Join(",", (s.Decals ?? new()).Select(d => d.Picture)))))
            : "";
        var gluedKey = heightScale == 1 || IsModel(b) ? "" :string.Create(CultureInfo.InvariantCulture, $"|t{def.Thickness:0.###}");
        var facesKey = !RollsOnItsValues(o) ? "" : "|f" + Hash8(string.Join(";", def.Faces.Select(f => string.Create(CultureInfo.InvariantCulture, $"{f.Value}@{f.Up[0]},{f.Up[1]},{f.Up[2]}"))));
        var surfaceKey = (def.Shadowless ? "|ns" : "") + (def.Normal is { } normal ? "|n" + normal : "");
        var key = string.Create(CultureInfo.InvariantCulture, $"{VisualKey(o)}|{def.Size.W:0.###}x{def.Size.H:0.###}{scriptKey}{bakedKey}{decalKey}{gluedKey}{facesKey}{surfaceKey}");
        int rich = def.States.Count * 100 + def.Counters.Count * 10 + def.TextLines.Count + def.Attachments.Count * 1000 + (def.Script != null ? 5000 : 0);
        if (pack.ByKey.TryGetValue(key, out var existing))
        {
            if (rich > pack.Richness.GetValueOrDefault(existing.Id))
            {
                existing.States = def.States;
                existing.Counters = def.Counters;
                existing.TextLines = def.TextLines;
                existing.Attachments = def.Attachments;
                existing.Script = def.Script;
                existing.ScriptHash = def.ScriptHash;
                existing.ScriptState = def.ScriptState;
                if (existing.Name == "Piece" && def.Name != "Piece") existing.Name = def.Name;
                pack.Richness[existing.Id] = rich;
            }
            return new DefResult(existing, pack, instScale, Shown(o, existing, self), b, script);
        }
        def.Id = $"{pack.Short}.{Slug(def.Name)}-{Hash8(key)}";
        pack.ByKey[key] = def;
        pack.Richness[def.Id] = rich;
        pack.M.Pieces.Add(def);
        return new DefResult(def, pack, instScale, Shown(o, def, self), b, script);
    }

    public static int Shown(ModuleObj o, PieceDef def, int self) =>
        o.IsPictureDie || RollsOnItsValues(o) ? FaceShown(o, def) : o.Name == "Custom_PDF" ? Math.Min(o.PdfPage, Math.Max(0, def.States.Count - 1)) : self;

    public TranslateResult Translate(string lua, PackOut pack, string where, bool isGlobal = false)
    {
        var tr = ModuleTranslator.Translate(lua, new TranslateContext
        {
            IsGlobal = isGlobal,
            ResolveImage = (url, type, scale) => MarkerDef(pack, new SpawnSpec(type, scale, url, "Marker"), where)?.Id,
            ResolveModel = (mesh, diffuse, sx, sy) => MarkerModelDef(pack, mesh, diffuse, sx, sy, where)?.Id,
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

    PieceDef? BuildDef(ModuleObj b, PackOut pack, double r, string where, ScriptInfo script, double heightScale)
    {
        var (lookUrl, lookKind) = IsModel(b) ? (b.MeshUrl, "mesh") : b.Name == "Custom_PDF" ? (b.PdfUrl, "pdf")
            : b.Name.StartsWith("Block") || b.IsChecker ? (null, "") : b.IsCard ? (b.Sheet?.FaceUrl, "image") : (b.ImageUrl, "image");
        if (_assets.Gone(lookUrl, lookKind) is { } gone)
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
            def.Back = Asset(pack, b.BackUrl, "image", where);
            def.Image ??= Placeholder(pack);
        }
        if (def.Name == "Piece") def.Name = Describe(pack, def);
        ModuleSources.Fill(def, b);
        def.Counters = script.Counters.Select(c => new CounterDef { Name = c.Name, Min = c.Min, Max = c.Max, Default = c.Default }).ToList();
        def.TextLines = script.TextLines.Select(t => new TextLineDef { Name = t.Name, Default = t.Default }).ToList();
        foreach (var (counter, sp) in script.Spawns)
        {
            var marker = MarkerDef(pack, sp, where);
            var c = def.Counters.First(x => x.Name == counter);
            c.SpawnsMarker = marker?.Id;
        }
        return def;
    }

    PieceDef? MarkerDef(PackOut pack, SpawnSpec sp, string where)
    {
        var img = Asset(pack, sp.ImageUrl, "image", where + " (spawned marker)");
        if (img == null) return null;
        var e = Entry(sp.ImageUrl);
        double m = e is { W: > 0, H: > 0 } ? Math.Max(e.W, e.H) : 1;
        double aw = e is { W: > 0 } ? e.W / m : 1, ah = e is { H: > 0 } ? e.H / m : 1;
        double aspect = e is { W: > 0, H: > 0 } ? e.W / (double)e.H : 1;
        var basis = TypeSize(sp.Type, aw, ah, aspect, 1, stretched: true);
        var size = new Size2(Math.Round(basis.W * sp.Scale, 4), Math.Round(basis.H * sp.Scale, 4));
        var key = string.Create(CultureInfo.InvariantCulture, $"{sp.Type}|{img}|||||{size.W:0.###}x{size.H:0.###}");
        if (pack.ByKey.TryGetValue(key, out var d)) return d;
        d = new PieceDef { Name = sp.Name, Image = img, Size = size, Thickness = 0.1, Shape = PackManifest.ShapeOf(sp.Type, 0), Tags = new() { "marker", sp.Type == "Custom_Token" ? "token" : "tile" } };
        d.Id = $"{pack.Short}.{Slug(d.Name)}-{Hash8(key)}";
        pack.ByKey[key] = d;
        pack.M.Pieces.Add(d);
        return d;
    }

    PieceDef? MarkerModelDef(PackOut pack, string meshUrl, string? diffuseUrl, double sx, double sy, string where)
    {
        var spawned = SpawnedModel(meshUrl, diffuseUrl, sx, sy);
        var mesh = ModelMesh(pack, spawned, where + " (spawned model)");
        if (mesh == null) return null;
        var texture = Asset(pack, diffuseUrl, "image", where + " (spawned model)");
        double r = Math.Abs(sx) < 1e-6 ? 1 : Math.Abs(sx);
        var size = Mul(BaseSize(spawned), r);
        var key = string.Create(CultureInfo.InvariantCulture, $"model|{mesh}|{texture}|{size.W:0.###}x{size.H:0.###}");
        if (pack.ByKey.TryGetValue(key, out var d)) return d;
        d = new PieceDef
        {
            Name = "Marker", Kind = "model", Mesh = mesh, Texture = texture, Size = size, LocalUnit = new Size2(Math.Round(r, 4), Math.Round(r, 4)),
            Thickness = Math.Round(Math.Clamp(BaseThickness(spawned) * Stretch(spawned) * r, 0.02, 3), 3),
            Shape = PackManifest.ShapeOf("Custom_Model", 0), Tags = new() { "marker", "model" },
        };
        d.Id = $"{pack.Short}.{Slug(d.Name)}-{Hash8(key)}";
        pack.ByKey[key] = d;
        pack.M.Pieces.Add(d);
        return d;
    }

    static ModuleObj SpawnedModel(string meshUrl, string? diffuseUrl, double sx, double sy)
    {
        var mesh = new System.Text.Json.Nodes.JsonObject { ["MeshURL"] = meshUrl };
        if (diffuseUrl != null) mesh["DiffuseURL"] = diffuseUrl;
        return new ModuleObj(new System.Text.Json.Nodes.JsonObject
        {
            ["Name"] = "Custom_Model",
            ["CustomMesh"] = mesh,
            ["Transform"] = new System.Text.Json.Nodes.JsonObject { ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sx },
        });
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
