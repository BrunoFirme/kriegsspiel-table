using System.Globalization;
using System.Text.Json.Nodes;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class TtsExport
{
    readonly Config _cfg;
    readonly PackageStore _store;
    readonly Random _rng = Repeatable.NewRandom();
    readonly HashSet<string> _guids = new();
    public int LocalOnly { get; private set; }

    public TtsExport(Config cfg) { _cfg = cfg; _store = new PackageStore(cfg.Output); }
    public TtsExport(Config cfg, string packagesRoot) { _cfg = cfg; _store = new PackageStore(packagesRoot); }

    public string Export(string source, string? outPath)
    {
        GameState g = source.EndsWith(".kstable", StringComparison.OrdinalIgnoreCase)
            ? Json.Load<SaveFile>(source).Snapshot
            : GameState.FromScenario(Json.Load<Scenario>(source));
        _store.LoadPacks(g.Packs);

        var objs = new JsonArray();
        foreach (var s in g.Sheets) objs.Add(Sheet(s));
        foreach (var p in g.Layer(Layers.Umpire)) if (Piece(p) is { } o) objs.Add(o);

        var save = new JsonObject
        {
            ["SaveName"] = g.Name,
            ["Date"] = Repeatable.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
            ["VersionNumber"] = "v13.2.2", ["GameMode"] = g.Name, ["GameType"] = "", ["GameComplexity"] = "",
            ["Tags"] = new JsonArray(), ["Gravity"] = 0.5, ["PlayArea"] = 0.5,
            ["Table"] = "Table_None", ["Sky"] = "Sky_Museum", ["Note"] = $"Exported from Kriegsspiel Table — turn {g.Clock.Turn}, {g.Clock.GameTime}",
            ["TabStates"] = new JsonObject(), ["LuaScript"] = "", ["LuaScriptState"] = "", ["XmlUI"] = "",
            ["ObjectStates"] = objs,
        };
        outPath ??= Path.Combine(_cfg.SavesDir, "KriegsspielTable_" + DefFactory.Slug(g.Name) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, save.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return outPath;
    }

    public string ExportPack(string packId, string? outPath)
    {
        var pack = _store.Pack(packId) ?? throw new FileNotFoundException($"pack {packId} is not installed");
        var objs = new JsonArray();
        var notecard = Base("Notecard", "Kriegsspiel Table pack", KtMarker.Pack + System.Text.Json.JsonSerializer.Serialize(
            new KtMarker.Info { Id = pack.Id, Name = pack.Name, Authors = pack.Source.Authors, License = pack.Source.License }, Config.Opts),
            Transform(new Vec2(0, 6), 1, 180, 1, 1, 1), null, false);
        notecard.Remove("CustomImage");
        objs.Add(notecard);
        double x = 0, z = 0, rowH = 0;
        const double rowWidth = 40;
        foreach (var def in pack.Pieces.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            double w = Math.Max(0.5, def.Size.W), h = Math.Max(0.5, def.Size.H);
            if (x + w > rowWidth)
            {
                x = 0;
                z -= rowH + 0.8;
                rowH = 0;
            }
            var p = new PieceInstance { Id = "export", Def = def.Id, Pos = new Vec2(x + w / 2 - rowWidth / 2, z - h / 2), RotDeg = 180, Scale = 1 };
            x += w + 0.6;
            rowH = Math.Max(rowH, h);
            if (Piece(p) is not { } o) continue;
            o["GMNotes"] = KtMarker.Def + Json.Serialize(def);
            o["Description"] = string.Join(" · ", def.Tags.Where(t => t != "mine"));
            objs.Add(o);
        }
        var save = new JsonObject
        {
            ["SaveName"] = pack.Name, ["Date"] = Repeatable.Now.ToString("M/d/yyyy h:mm:ss tt", CultureInfo.InvariantCulture),
            ["VersionNumber"] = "v13.2.2", ["GameMode"] = pack.Name, ["GameType"] = "", ["GameComplexity"] = "",
            ["Tags"] = new JsonArray("Kriegsspiel", "Kriegsspiel Table pack"), ["Gravity"] = 0.5, ["PlayArea"] = 0.5,
            ["Table"] = "Table_None", ["Sky"] = "Sky_Museum",
            ["Note"] = $"{pack.Name}: a Kriegsspiel Table piece pack. Kriegsspiel Table users: import this module (or its Workshop link) to get the pack back exactly.",
            ["TabStates"] = new JsonObject(), ["LuaScript"] = "", ["LuaScriptState"] = "", ["XmlUI"] = "", ["ObjectStates"] = objs,
        };
        outPath ??= Path.Combine(_cfg.SavesDir, "KT pack - " + DefFactory.Slug(pack.Name) + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
        File.WriteAllText(outPath, save.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return outPath;
    }

    string Guid6()
    {
        string g;
        do g = _rng.Next(0x100000, 0xFFFFFF).ToString("x6");
        while (!_guids.Add(g));
        return g;
    }

    string UrlFor(PackManifest? pack, string? sha, string? fallbackFile = null)
    {
        if (pack != null && sha != null && pack.Assets.TryGetValue(sha, out var a))
        {
            if (!string.IsNullOrEmpty(a.Url)) return a.Url!;
            LocalOnly++;
            return new Uri(Path.Combine(_store.PackageDir(pack.Id), a.File)).AbsoluteUri;
        }
        if (fallbackFile != null) { LocalOnly++; return new Uri(fallbackFile).AbsoluteUri; }
        return "";
    }

    static JsonObject Transform(Vec2 pos, double y, double rot, double sx, double sy, double sz) => new()
    {
        ["posX"] = pos.X, ["posY"] = y, ["posZ"] = pos.Z, ["rotX"] = 0.0, ["rotY"] = rot, ["rotZ"] = 0.0,
        ["scaleX"] = sx, ["scaleY"] = sy, ["scaleZ"] = sz,
    };

    JsonObject Base(string name, string nick, string desc, JsonObject transform, string? tint, bool locked) => new()
    {
        ["GUID"] = Guid6(), ["Name"] = name, ["Transform"] = transform, ["Nickname"] = nick, ["Description"] = desc, ["GMNotes"] = "",
        ["ColorDiffuse"] = Colour(tint), ["Locked"] = locked, ["Grid"] = false, ["Snap"] = false, ["Autoraise"] = true, ["Sticky"] = true,
        ["Tooltip"] = true, ["LuaScript"] = "", ["LuaScriptState"] = "", ["XmlUI"] = "",
    };

    static JsonObject Colour(string? hex)
    {
        double r = 1, g = 1, b = 1;
        if (hex is { Length: 7 } && hex[0] == '#')
        {
            r = Convert.ToInt32(hex[1..3], 16) / 255.0;
            g = Convert.ToInt32(hex[3..5], 16) / 255.0;
            b = Convert.ToInt32(hex[5..7], 16) / 255.0;
        }
        return new JsonObject { ["r"] = r, ["g"] = g, ["b"] = b };
    }

    JsonObject Sheet(SheetDef s)
    {
        double k = Math.Max(s.Size.W, s.Size.H) / _cfg.BaseSizes.CustomBoard;
        var file = Directory.EnumerateFiles(Path.Combine(_store.PackageDir(s.Package), "assets"), s.Image + ".*").FirstOrDefault();
        var o = Base("Custom_Board", s.Name, "", Transform(s.Pos, 1.0 + s.Z * 0.01, s.RotDeg, k, k, k), null, s.Locked);
        o["CustomImage"] = new JsonObject { ["ImageURL"] = s.Url ?? (file != null ? UrlFor(null, null, file) : ""), ["ImageSecondaryURL"] = "", ["ImageScalar"] = 1.0, ["WidthScale"] = 0.0 };
        return o;
    }

    JsonObject? Piece(PieceInstance p)
    {
        var def = _store.Def(p.Def);
        var pack = _store.PackOfDef(p.Def);
        if (def == null || pack == null) return null;
        var lines = new List<string>();
        foreach (var (k, v) in p.Text) if (v.Length > 0) lines.Add(k == "Name" ? v : $"{k}: {v}");
        foreach (var c in def.Counters) lines.Add($"{c.Name} {p.Counters.GetValueOrDefault(c.Name, c.Default)}");
        var nick = p.Text.GetValueOrDefault("Name") is { Length: > 0 } n ? n : p.Label ?? def.Name;

        JsonObject Make(int state)
        {
            var st = state >= 0 && state < def.States.Count ? def.States[state] : null;
            var size = st?.Size ?? def.Size;
            double y = 1.0 + p.Y + p.Stack * 0.2;
            if (def.Kind == "model" && (st?.Mesh ?? def.Mesh) is { } mesh)
            {
                var o = Base("Custom_Model", nick, string.Join("\n", lines), Transform(p.Pos, y, p.RotDeg, p.Scale, p.Scale, p.Scale), p.TintOverride ?? def.Tint, false);
                o["CustomMesh"] = new JsonObject
                {
                    ["MeshURL"] = UrlFor(pack, mesh), ["DiffuseURL"] = UrlFor(pack, st?.Texture ?? def.Texture), ["NormalURL"] = "", ["ColliderURL"] = "",
                    ["Convex"] = true, ["MaterialIndex"] = 1, ["TypeIndex"] = 4, ["CastShadows"] = true,
                };
                return o;
            }
            var shape = st?.Shape ?? def.Shape;
            var token = shape == "alpha";
            double k = token ? Math.Max(size.W, size.H) * p.Scale / _cfg.BaseSizes.CustomToken : size.H * p.Scale / _cfg.BaseSizes.CustomTile;
            var obj = Base(token ? "Custom_Token" : "Custom_Tile", nick, string.Join("\n", lines), Transform(p.Pos, y, p.RotDeg, k, 1.0, k), p.TintOverride ?? def.Tint, false);
            var img = UrlFor(pack, st?.Image ?? def.Image);
            var back = state > 0 && st != null ? st.Back : def.Back;
            obj["CustomImage"] = token
                ? new JsonObject { ["ImageURL"] = img, ["ImageSecondaryURL"] = "", ["ImageScalar"] = 1.0, ["WidthScale"] = 0.0, ["CustomToken"] = new JsonObject { ["Thickness"] = def.Thickness, ["MergeDistancePixels"] = 15.0, ["StandUp"] = false, ["Stackable"] = false } }
                : new JsonObject
                {
                    ["ImageURL"] = img, ["ImageSecondaryURL"] = UrlFor(pack, back), ["ImageScalar"] = 1.0, ["WidthScale"] = 0.0,
                    ["CustomTile"] = new JsonObject { ["Type"] = shape switch { "hex" => 1, "circle" => 2, "rounded" => 3, _ => 0 }, ["Thickness"] = def.Thickness, ["Stackable"] = false, ["Stretch"] = true },
                };
            return obj;
        }

        var main = Make(p.State);
        if (def.States.Count > 1)
        {
            var states = new JsonObject();
            for (int i = 0; i < def.States.Count; i++) if (i != p.State) states[(i + 1).ToString()] = Make(i);
            main["States"] = states;
        }
        return main;
    }
}
