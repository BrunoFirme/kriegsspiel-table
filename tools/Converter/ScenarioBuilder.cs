using KTable.Core;
using KTable.Client.Relief;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class ScenarioBuilder
{
    readonly Config _cfg;
    readonly AssetStore _assets;
    readonly Report _rep;
    readonly DefFactory _defs;
    readonly MakeWay _way;
    int _nextId = 1;

    public ScenarioBuilder(Config cfg, AssetStore assets, Report rep, MakeWay way)
    {
        _cfg = cfg;
        _assets = assets;
        _rep = rep;
        _defs = new DefFactory(cfg, assets, rep);
        _way = way;
    }

    sealed class Layer
    {
        public string Name = "";
        public List<ModuleObj> Objects = new();
        public List<ModuleObj> Effects = new();
        public List<PieceInstance> Pieces = new();
        public Dictionary<PieceInstance, double> Y = new();
        public Dictionary<PieceInstance, double> Bottom = new();
        public Dictionary<PieceInstance, double> Top = new();
        public Dictionary<PieceInstance, (PieceInstance Root, double Bottom)> Glued = new();
        public Dictionary<PieceInstance, double> HeightScale = new();
    }

    public Task<Scenario> Build(string masterId) => Build(ModuleSave.Load(_cfg, masterId));

    public async Task<Scenario> Build(ModuleSave master)
    {
        var sc = _cfg.Scenario;
        Console.WriteLine($"Scenario {sc.Id} (master {sc.Master})");
        await _assets.FetchAll(master);
        new BundleBake(_cfg, _assets, _rep).Run(master);
        _defs.Table = master.Table;

        var root = _cfg.Output;
        var pkgRel = "scenarios/" + sc.Dir;
        var pkgDir = Path.Combine(root, "scenarios", sc.Dir);
        _way.Pack(sc.ExtrasPack, _rep);
        var oldScenario = _way.Scenario(sc.Dir, _rep);
        var extras = new PackOut(root, sc.ExtrasPack, sc.Name + " (extras)", sc.ExtrasShort, null);
        extras.M.Source.License = "assets from the example save; see pack sources";

        var scenario = new Scenario
        {
            Id = sc.Id, Name = sc.Name,
            Clock = new GameClock { Turn = sc.Clock.Turn, GameTime = sc.Clock.GameTime, IncrementMinutes = sc.Clock.IncrementMinutes },
            Calibration = new Calibration { UnitsPerMeter = sc.UnitsPerMeter },
        };
        TranslateGlobal(scenario, master, extras);
        scenario.Lighting.Backdrop = Sky(master);
        var boardSet = AddSheets(scenario, master, pkgRel, pkgDir, oldScenario);

        var layers = new List<Layer> { Collect("umpire", master, boardSet) };

        var common = layers.SelectMany(l => l.Objects).SelectMany(o => o.IsBag ? Held(o).Prepend(o) : [o]).GroupBy(_defs.VisualKey)
            .ToDictionary(g => g.Key, g => g.GroupBy(DefFactory.BaseScale).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key);
        _common = common;

        var packDefs = LoadPackDefs(root);

        var usedPacks = new HashSet<string>();
        int rekeyed = 0, flipped = 0;
        Progress.Stage("table", layers.Sum(l => l.Objects.Count));
        foreach (var layer in layers)
        {
            _defs.Context = $"{sc.Id}/{layer.Name}";
            foreach (var o in layer.Objects)
            {
                Progress.Step(o.Nickname.Length > 0 ? o.Nickname : o.Name);
                var placed = Instance(o, common[_defs.VisualKey(o)], extras, packDefs, layer, usedPacks, ref rekeyed, null, absolute: Math.Abs(o.ScaleX));
                if (o.Flipped && placed is { FaceDown: false }) flipped++;
            }
        }
        var effects = Effects;
        if (master.Objects.SelectMany(o => o.Walk()).Any(o => o.Name == BundleType && !_cfg.IsTableWall(o.BundleUrl) && effects.For(o) != null))
            usedPacks.Add(KTable.Client.Effects.EffectKinds.StandardPack);
        var placedEffects = layers.ToDictionary(l => l, _ => new List<PieceInstance>());
        foreach (var layer in layers)
            foreach (var o in layer.Effects)
                if (Effect(o, extras, packDefs, layer, usedPacks, ref rekeyed) is { } fx) placedEffects[layer].Add(fx);
        Index(extras.M.Pieces);
        foreach (var pd in packDefs.Values.SelectMany(v => v)) _lookup.TryAdd(pd.def.Id, pd.def);
        foreach (var layer in layers) Stack(layer);
        foreach (var (layer, placed) in placedEffects) layer.Pieces.AddRange(placed);
        LayNotes(master, scenario, extras, layers[0], usedPacks);
        if (placedEffects.Values.Sum(p => p.Count) is var effectCount and > 0) _rep.Counts[$"{sc.Id}: effects placed as Firme's effects"] = effectCount.ToString();

        scenario.UmpireBoard = layers[0].Pieces;
        scenario.Drawings["umpire"] = TableStrokes(master);
        AddDecals(master, scenario, extras, usedPacks);
        if (master.Rules.Trim().Length > 0) scenario.Briefing = master.Rules.Trim();
        if (_stowed > 0) _rep.Counts[$"{sc.Id}: pieces held inside bags"] = _stowed.ToString();
        if (_hiddenByScale > 0) _rep.Note($"{sc.Id}: {_hiddenByScale} piece(s) the module hid by scaling them to nothing show here at a tenth of their size; delete or hold them if they are in the way.");

        PruneExtras(extras, layers, scenario);
        usedPacks.Add(extras.M.Id);

        scenario.Packs = usedPacks.OrderBy(p => p == extras.M.Id ? 1 : 0).ThenBy(p => p, StringComparer.Ordinal).ToList();
        Json.Save(Path.Combine(pkgDir, "scenario.json"), scenario);
        var start = Path.Combine(pkgDir, "scenario.start.json");
        if (File.Exists(start)) File.Delete(start);

        ReportCounts(scenario, extras, rekeyed, flipped);
        return scenario;
    }

    void TranslateGlobal(Scenario scenario, ModuleSave master, PackOut extras)
    {
        var sc = _cfg.Scenario;
        if (master.Lua.Trim().Length > 0)
        {
            var verdict = KTable.Core.ScriptSafety.Inspect(master.Lua);
            if (verdict.Hostile) _rep.HostileScript(verdict.Reasons, $"{sc.Id} Global script", "global:" + sc.Id);
            if (verdict.Script.Trim().Length > 0)
            {
                scenario.Script = _defs.Translate(verdict.Script, extras, $"{sc.Id} Global script", isGlobal: true).Script;
                scenario.ScriptState = _defs.RewriteUrls(master.LuaState, extras, $"{sc.Id} Global script state");
            }
        }
    }

    string Sky(ModuleSave master)
    {
        if (master.SkyUrl is not { } url) return KTable.Client.Backdrops.None;
        var where = $"{_cfg.Scenario.Id} sky";
        if (_defs.Entry(url) is not { } e || !AssetStore.IsPicture(e.Ext) || _assets.FilePath(e) is not { } file)
        {
            _rep.Missing(url, _assets.Gone(url, "image") ?? _assets.Why(url) ?? "unresolved", where);
            return KTable.Client.Backdrops.None;
        }
        string? kept;
        try { kept = new KTable.Client.WebPictures(_cfg.Output).Keep(File.ReadAllBytes(file)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _rep.Note($"{where}: the module's sky could not be stored ({ex.Message}), so the table has no backdrop."); return KTable.Client.Backdrops.None; }
        if (kept == null) { _rep.Note($"{where}: the module's sky is larger than a backdrop picture may be, so the table has no backdrop."); return KTable.Client.Backdrops.None; }
        _rep.Counts[$"{_cfg.Scenario.Id}: backdrop"] = "the module's own sky";
        return kept;
    }

    public static IEnumerable<ModuleObj> Pile(ModuleObj deck)
    {
        const double PileStep = 0.05;
        var cards = deck.Contained.Where(c => c.IsCard).ToList();
        for (int i = 0; i < cards.Count; i++)
        {
            if (cards[i].J.DeepClone() is not System.Text.Json.Nodes.JsonObject j) continue;
            var own = cards[i].J["Transform"] as System.Text.Json.Nodes.JsonObject;
            int height = deck.Flipped ? cards.Count - 1 - i : i;
            j["Transform"] = new System.Text.Json.Nodes.JsonObject
            {
                ["posX"] = deck.PosX, ["posY"] = deck.PosY + height * PileStep, ["posZ"] = deck.PosZ,
                ["rotX"] = deck.RotX, ["rotY"] = deck.RotY, ["rotZ"] = deck.RotZ,
                ["scaleX"] = own?["scaleX"]?.DeepClone() ?? 1, ["scaleY"] = own?["scaleY"]?.DeepClone() ?? 1, ["scaleZ"] = own?["scaleZ"]?.DeepClone() ?? 1,
            };
            j["Locked"] = deck.Locked;
            yield return new ModuleObj(j);
        }
    }

    public static IEnumerable<ModuleObj> WithAttachedTerrain(ModuleObj o)
    {
        yield return o;
        double th = o.RotY * Math.PI / 180;
        foreach (var c in o.Children.Where(c => c.Name == "Custom_Model" && c.MeshUrl != null && c.DiffuseUrl != null))
        {
            if (c.J.DeepClone() is not System.Text.Json.Nodes.JsonObject j || j["Transform"] is not System.Text.Json.Nodes.JsonObject t) continue;
            double lx = c.PosX * o.ScaleX, lz = c.PosZ * o.ScaleZ;
            t["posX"] = o.PosX + lx * Math.Cos(th) + lz * Math.Sin(th);
            t["posZ"] = o.PosZ - lx * Math.Sin(th) + lz * Math.Cos(th);
            t["posY"] = o.PosY + c.PosY * o.ScaleY;
            t["rotY"] = o.RotY + c.RotY;
            t["scaleX"] = c.ScaleX * o.ScaleX;
            t["scaleY"] = c.ScaleY * o.ScaleY;
            t["scaleZ"] = c.ScaleZ * o.ScaleZ;
            j.Remove("ChildObjects");
            yield return new ModuleObj(j);
        }
    }

    HashSet<ModuleObj> AddSheets(Scenario scenario, ModuleSave master, string pkgRel, string pkgDir, string? oldScenario)
    {
        var sc = _cfg.Scenario;
        var tops = master.Objects.Where(o => o.Top != null).ToList();
        var boards = master.Objects.Where(IsSheet).OrderBy(b => b.PosY).ToList();
        var terrain = master.Objects.Where(o => o.IsTerrain).SelectMany(WithAttachedTerrain).ToList();
        var boardSet = tops.Concat(boards).Concat(terrain).ToHashSet();
        if (boardSet.Count > 0) Progress.Stage("maps", boardSet.Count);
        foreach (var t in tops) AddTableTop(scenario, t, pkgRel, pkgDir, oldScenario);
        foreach (var b in boards)
        {
            var e = _defs.Entry(b.ImageUrl);
            if (e == null) { _rep.Missing(b.ImageUrl ?? "", _assets.Why(b.ImageUrl ?? "") ?? "unresolved", $"{sc.Id} map sheet"); continue; }
            var size = _defs.BaseSize(b);
            var sheet = NewSheet(scenario, b, pkgRel, e);
            sheet.Pos = new Vec2(Math.Round(b.PosX, 4), Math.Round(b.PosZ, 4));
            sheet.RotDeg = Norm(b.RotY);
            sheet.Size = new Size2(Math.Round(size.W * Math.Abs(b.ScaleX), 4), Math.Round(size.H * Math.Abs(b.ScaleX), 4));
            sheet.Url = b.ImageUrl;
            if (Lay(sheet, e, pkgDir, oldScenario)) scenario.Sheets.Add(sheet);
        }
        AddTerrain(scenario, terrain, pkgRel, pkgDir, oldScenario);
        return boardSet;
    }

    void AddTableTop(Scenario scenario, ModuleObj o, string pkgRel, string pkgDir, string? oldScenario)
    {
        var top = o.Top!;
        var e = _defs.Entry(o.DiffuseUrl);
        if (e == null || !AssetStore.IsPicture(e.Ext))
        {
            _rep.Missing(o.DiffuseUrl!, e == null ? _assets.Why(o.DiffuseUrl!) ?? "unresolved" : $"not a picture ({e.Ext})", $"{_cfg.Scenario.Id} table top {o.Guid}");
            return;
        }
        if (BestEffort.Read(() => TableTop.IsCloth(_assets.FilePath(e)!), fallback: false))
        {
            _rep.Skipped("table top of plain cloth (the table's own shade stands in)", $"{_cfg.Scenario.Id} {o.Name} {o.Guid}");
            return;
        }
        double across = Math.Round((top.MaxX - top.MinX) * Math.Abs(o.ScaleX), 4), deep = Math.Round((top.MaxZ - top.MinZ) * Math.Abs(o.ScaleZ), 4);
        double lx = -(top.MinX + top.MaxX) / 2 * o.ScaleX, lz = (top.MinZ + top.MaxZ) / 2 * o.ScaleZ, th = o.RotY * Math.PI / 180;
        var sheet = NewSheet(scenario, o, pkgRel, e);
        sheet.Pos = new Vec2(Math.Round(o.PosX + lx * Math.Cos(th) + lz * Math.Sin(th), 4), Math.Round(o.PosZ - lx * Math.Sin(th) + lz * Math.Cos(th), 4));
        sheet.RotDeg = Norm(o.RotY + top.Turn);
        sheet.Size = top.Upright ? new Size2(across, deep) : new Size2(deep, across);
        sheet.Url = o.DiffuseUrl;
        if (Lay(sheet, e, pkgDir, oldScenario)) scenario.Sheets.Add(sheet);
    }

    SheetDef NewSheet(Scenario scenario, ModuleObj o, string pkgRel, CacheEntry picture) => new()
    {
        Id = scenario.Sheets.Count == 0 ? "main" : $"sheet-{scenario.Sheets.Count + 1}",
        Name = o.Nickname.Length > 0 ? o.Nickname : (scenario.Sheets.Count == 0 ? _cfg.Scenario.Name : $"Sheet {scenario.Sheets.Count + 1}"),
        Image = picture.Sha!, Package = pkgRel, Locked = true, Z = scenario.Sheets.Count, Kind = "map",
    };

    bool Lay(SheetDef sheet, CacheEntry e, string pkgDir, string? oldScenario)
    {
        var sc = _cfg.Scenario;
        Progress.On($"{sheet.Name} ({e.W} × {e.H})");
        try
        {
            var src = _assets.FilePath(e)!;
            Console.WriteLine($"  sheet {sheet.Id}: {e.W}x{e.H} px, {sheet.Size.W}x{sheet.Size.H} units; building tiles...");
            var tilesDir = Path.Combine(pkgDir, "tiles", e.Sha!);
            if (oldScenario != null) ReuseTiles(Path.Combine(oldScenario, "scenario", "tiles", e.Sha!), tilesDir);
            var idx = Tiles.Build(src, tilesDir, _cfg.Tiles);
            _rep.Counts[$"{sc.Id}: sheet {sheet.Id} tiles"] = $"{idx.Levels.Count} levels, {idx.Levels.Sum(l => l.Cols * l.Rows)} tiles ({idx.Ext})";
            var assetDst = Path.Combine(pkgDir, "assets", $"{e.Sha}.{e.Ext}");
            Directory.CreateDirectory(Path.GetDirectoryName(assetDst)!);
            if (!File.Exists(assetDst)) File.Copy(src, assetDst);
            return true;
        }
        catch (Exception ex)
        {
            _rep.Note($"{sc.Id}: the map sheet '{sheet.Name}' was left out: its picture could not be read ({ex.Message}). The rest of the table came in; put the map on the table by hand from a copy of the picture that opens.");
            Console.WriteLine($"  sheet {sheet.Id} '{sheet.Name}' left out: its picture could not be read ({ex.Message})");
            return false;
        }
        finally { Progress.Step(); }
    }

    void AddTerrain(Scenario scenario, List<ModuleObj> terrain, string pkgRel, string pkgDir, string? oldScenario)
    {
        var sc = _cfg.Scenario;
        var read = new List<(ModuleObj o, ReliefSource src, CacheEntry picture)>();
        foreach (var o in terrain)
        {
            Progress.On(o.Nickname);
            var where = $"{sc.Id} 3D terrain {o.Guid}";
            var mesh = _defs.Entry(o.MeshUrl);
            var tex = _defs.Entry(o.DiffuseUrl);
            if (mesh is not { Ext: "obj" }) { _rep.Missing(o.MeshUrl!, mesh == null ? _assets.Why(o.MeshUrl!) ?? "unresolved" : $"not an OBJ model ({mesh.Ext})", where); continue; }
            if (tex == null || !AssetStore.IsPicture(tex.Ext)) { _rep.Missing(o.DiffuseUrl!, tex == null ? _assets.Why(o.DiffuseUrl!) ?? "unresolved" : $"not a picture ({tex.Ext})", where); continue; }
            try
            {
                var src = ReliefBuilder.FromMesh(ModelFiles.Obj(_assets.FilePath(mesh)!), o.Nickname);
                read.Add((o, src, ReliefPicture.AsItIs(src) ? tex : _assets.Fitted(src, tex)));
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                _rep.Note($"{sc.Id}: the 3D terrain {o.Guid} was left out: {e.Message}.");
            }
        }
        if (read.Count == 0) return;
        double floor = read.Min(r => r.o.PosY + r.src.Low * Math.Abs(r.o.ScaleY));
        foreach (var (o, src, picture) in read)
        {
            double sx = Math.Abs(o.ScaleX), sy = Math.Abs(o.ScaleY), sz = Math.Abs(o.ScaleZ);
            var (relief, _) = ReliefFiles.Save(pkgDir, src.ToGrid((floor - o.PosY) / sy));
            var sheet = NewSheet(scenario, o, pkgRel, picture);
            sheet.Pos = new Vec2(Math.Round(o.PosX, 4), Math.Round(o.PosZ, 4));
            sheet.RotDeg = Norm(o.RotY);
            sheet.Size = new Size2(Math.Round(src.Width * sx, 4), Math.Round(src.Depth * sz, 4));
            sheet.Relief = relief;
            sheet.Height = Math.Round(sy / sx, 4);
            sheet.Url = o.DiffuseUrl;
            if (!Lay(sheet, picture, pkgDir, oldScenario)) continue;
            scenario.Sheets.Add(sheet);
            const double CentredWithin = 0.01;
            if (Math.Abs(src.Left + src.Width / 2) > CentredWithin * src.Width || Math.Abs(src.Top + src.Depth / 2) > CentredWithin * src.Depth)
                _rep.Note($"{sc.Id}: the 3D terrain {o.Guid} is not centred on its own object in the module; its map sheet '{sheet.Name}' is laid on the object's centre, so check that it meets its neighbours.");
        }
    }

    const double ModuleDecalTurn = 180;

    void AddDecals(ModuleSave master, Scenario scenario, PackOut extras, HashSet<string> usedPacks)
    {
        var where = $"{_cfg.Scenario.Id} decal";
        scenario.DecalPalette = master.DecalPalette
            .Select(p => _defs.UiAsset(extras, p.Url, where) is { } pic
                ? new PaletteDecal { Name = p.Name, Picture = pic, Size = Math.Clamp(p.Size, PaletteDecal.SmallestSize, PaletteDecal.LargestSize) } : null)
            .OfType<PaletteDecal>().Take(PaletteDecal.MostInAPalette).ToList();
        var stamped = new List<DecalDef>();
        foreach (var d in master.TableDecals.Take(PaletteDecal.MostOnTheTable))
            if (_defs.UiAsset(extras, d.Url, where) is { } pic)
                stamped.Add(new DecalDef
                {
                    Id = $"dc-{_nextId++:D6}", Picture = pic, Name = d.Name, X = Math.Round(d.PosX, 4), Z = Math.Round(d.PosZ, 4),
                    W = Math.Round(Math.Abs(d.ScaleX), 4), H = Math.Round(Math.Abs(d.ScaleY), 4), Rot = Norm(d.RotY - ModuleDecalTurn),
                });
        scenario.Decals["umpire"] = stamped;
        if (scenario.DecalPalette.Count > 0 || stamped.Count > 0) usedPacks.Add(extras.M.Id);
    }

    static void PruneExtras(PackOut extras, List<Layer> layers, Scenario scenario)
    {
        var pieces = layers.SelectMany(l => l.Pieces).ToList();
        var texts = pieces.SelectMany(p => new[] { p.Script, p.ScriptState, p.Memo }).Append(scenario.Script).Append(scenario.ScriptState)
            .OfType<string>().Where(t => t.Length > 0).ToHashSet();
        var referenced = pieces.Select(p => p.Def).ToHashSet();
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (var d in extras.M.Pieces.Where(d => referenced.Contains(d.Id)).ToList())
            {
                foreach (var dep in d.Attachments.Select(a => a.Piece).Concat(d.Counters.Select(c => c.SpawnsMarker)).OfType<string>())
                    grew |= referenced.Add(dep);
                foreach (var t in new[] { d.Script, d.ScriptState }.OfType<string>().Where(t => t.Length > 0)) grew |= texts.Add(t);
            }
            foreach (var d in extras.M.Pieces.Where(d => !referenced.Contains(d.Id) && texts.Any(t => t.Contains(d.Id, StringComparison.Ordinal))).ToList())
                grew |= referenced.Add(d.Id);
        }
        extras.M.Pieces = extras.M.Pieces.Where(d => referenced.Contains(d.Id)).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
        var usedAssets =
            extras.M.Pieces.SelectMany(d => new[] { d.Image, d.Back, d.Mesh, d.Texture, d.AddOnMesh, d.AddOnTexture }.Concat(d.States.SelectMany(s => new[] { s.Image, s.Back, s.Mesh, s.Texture })))
            .OfType<string>().ToHashSet();
        usedAssets.UnionWith(pieces.SelectMany(p => p.Decals).Select(d => d.Picture));
        usedAssets.UnionWith(scenario.Decals.Values.SelectMany(l => l).Select(d => d.Picture).Concat(scenario.DecalPalette.Select(d => d.Picture)));
        usedAssets.UnionWith(extras.M.Assets.Keys.Where(k => !usedAssets.Contains(k) && texts.Any(t => t.Contains(k, StringComparison.Ordinal))));
        foreach (var k in extras.M.Assets.Keys.Where(k => !usedAssets.Contains(k)).ToList()) extras.M.Assets.Remove(k);
        extras.Save();
        PackBuilder.Prune(extras);
    }

    void ReportCounts(Scenario scenario, PackOut extras, int rekeyed, int flipped)
    {
        var sc = _cfg.Scenario;
        _rep.Counts[$"{sc.Id}: umpire pieces"] = scenario.UmpireBoard.Count.ToString();
        _rep.Counts[$"{sc.Id}: extras defs"] = extras.M.Pieces.Count.ToString();
        _rep.Counts[$"{sc.Id}: instances using pack defs"] = rekeyed.ToString();
        _rep.Counts[$"{sc.Id}: packs"] = string.Join(", ", scenario.Packs);
        if (flipped > 0) _rep.Note($"{sc.Id}: {flipped} pieces lie face down in the module but have no back picture, so they are laid face up.");
        Console.WriteLine($"  → umpire {scenario.UmpireBoard.Count}; extras {extras.M.Pieces.Count} defs; {rekeyed} instances use pack defs");
    }

    static void ReuseTiles(string from, string to)
    {
        try
        {
            if (!File.Exists(Path.Combine(from, "index.json")) || File.Exists(Path.Combine(to, "index.json"))) return;
            foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f) == "index.json" ? 1 : 0))
            {
                var dst = Path.Combine(to, Path.GetRelativePath(from, f));
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                File.Copy(f, dst, true);
            }
        }
        catch (Exception e) { Console.WriteLine($"    the earlier tiles could not be copied ({e.Message}); slicing the map again"); }
    }

    bool IsSheet(ModuleObj o) =>
        o.ImageUrl != null && (o.Name == "Custom_Board" || (o.Name is "Custom_Tile" or "Custom_Token" && Math.Abs(o.ScaleX) >= 10)) && !o.IsFurniture;

    Layer Collect(string name, ModuleSave save, HashSet<ModuleObj> exclude)
    {
        var l = new Layer { Name = name };
        foreach (var o in save.Objects)
        {
            var where = $"{save.WorkshopId} {o.Name} '{o.Nickname}' {o.Guid}";
            if (o.Verdict.Hostile) _rep.HostileScript(o.Verdict.Reasons, where, o.Guid);
            if (exclude.Contains(o)) continue;
            if (IsSheet(o) || o.Top != null) { if (name != "umpire") _rep.Skipped("map sheet duplicated in a side module", where); continue; }
            if (o.IsDeck) { l.Objects.AddRange(Pile(o).Where(c => DefFactory.NotTranslated(c) is null)); continue; }
            if (DefFactory.NotTranslated(o) is { } why) { _rep.Skipped(why, where); continue; }
            if (o.IsInfiniteBag) { _rep.Skipped(o.Name + " (infinite bags are left out: place pieces from the Pieces drawer)", where); continue; }
            if (o.Name == BundleType && !_cfg.IsTableWall(o.BundleUrl) && _defs.ModelOfBundle(o) == null) { l.Effects.Add(o); continue; }
            if (_cfg.IgnoreTypes.Contains(o.Name) && !_defs.Shows(o)) { _rep.Skipped(o.Name, where); continue; }
            if (o.IsFurniture) { _rep.Skipped("table furniture (custom table / surround / under the table)", where); continue; }
            if (!_defs.Shows(o)) { _rep.UnknownType(o.Name, where); continue; }
            l.Objects.Add(o);
        }
        return l;
    }

    const string BundleType = "Custom_Assetbundle";

    PieceInstance? EffectPiece(ModuleObj o, string where, HashSet<string> usedPacks)
    {
        if (_assets.Gone(o.BundleUrl, "bundle") is { } gone) { _rep.LeftOut(o.Nickname.Length > 0 ? o.Nickname : "Effect", o.BundleUrl!, gone, where, o.Guid); return null; }
        if (Effects.For(o) is not { } match)
        {
            _rep.Skipped(Effects.Installed ? BundleType + " (a 3D model or an effect with no counterpart among Firme's effects)" : BundleType + " (Firme's effects are not installed)", where);
            return null;
        }
        usedPacks.Add(KTable.Client.Effects.EffectKinds.StandardPack);
        return new PieceInstance
        {
            Id = $"p-{_nextId++:D6}", Def = match.Def.Id, Pos = new Vec2(Math.Round(o.PosX, 4), Math.Round(o.PosZ, 4)),
            RotDeg = Norm(o.RotY), Scale = match.Scale, Guid = o.Guid.Length > 0 ? o.Guid : null,
        };
    }

    PieceInstance? Effect(ModuleObj o, PackOut extras, Dictionary<string, List<(PieceDef def, string pack)>> packDefs,
        Layer layer, HashSet<string> usedPacks, ref int rekeyed)
    {
        var fx = EffectPiece(o, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid}", usedPacks);
        PlaceChildren(o, o.Children, fx, Math.Abs(o.ScaleX), 1, Math.Abs(o.ScaleX), extras, packDefs, layer, usedPacks, ref rekeyed);
        return fx;
    }

    public static (double X, double Z) LocalUnits(ModuleObj o, PieceDef def, double tokenArea) =>
        LocalUnits(o, def.Size, def.Tags.Contains("token"), tokenArea);

    public static (double X, double Z) LocalUnits(ModuleObj o, Size2 size, bool token, double tokenArea)
    {
        const double TileLocalSpan = 2;
        double depth = o.ScaleX == 0 ? 1 : Math.Abs(o.ScaleZ / o.ScaleX);
        if (depth == 0) depth = 1;
        double x = token ? Math.Sqrt(size.W * size.H / (tokenArea * depth)) : size.H / (TileLocalSpan * depth);
        return (x, x * depth);
    }

    public static List<DecalDef> DecalsOf(ModuleObj o, PieceDef def, double tokenArea, Func<string, string?> picture) =>
        DecalsOf(o, def.Size, def.Tags.Contains("token"), tokenArea, picture);

    public static List<DecalDef> DecalsOf(ModuleObj o, Size2 size, bool token, double tokenArea, Func<string, string?> picture)
    {
        const double ModuleDecalTurn = 180, Digits = 4;
        var (ux, uz) = LocalUnits(o, size, token, tokenArea);
        var list = new List<DecalDef>();
        foreach (var d in o.Decals.Take(DecalDef.MostOnAPiece))
        {
            if (picture(d.Url) is not { } pic) continue;
            bool back = d.PosY < 0;
            double turn = (d.RotY - ModuleDecalTurn) * Math.PI / 180, cos = Math.Cos(turn), sin = Math.Sin(turn);
            double w = Math.Abs(d.ScaleX) * Math.Sqrt(ux * ux * cos * cos + uz * uz * sin * sin);
            double h = Math.Abs(d.ScaleY) * Math.Sqrt(ux * ux * sin * sin + uz * uz * cos * cos);
            if (!double.IsFinite(w) || !double.IsFinite(h) || w <= 0 || h <= 0) continue;
            list.Add(new DecalDef
            {
                Picture = pic, Name = d.Name, Face = back ? "back" : "front",
                X = Math.Round((back ? d.PosX : -d.PosX) * ux, (int)Digits), Z = Math.Round(d.PosZ * uz, (int)Digits),
                W = Math.Round(w, (int)Digits), H = Math.Round(h, (int)Digits), Rot = Math.Round(d.RotY - ModuleDecalTurn, 2),
            });
        }
        return list;
    }

    PieceInstance? Instance(ModuleObj o, double refScale, PackOut extras, Dictionary<string, List<(PieceDef def, string pack)>> packDefs,
        Layer layer, HashSet<string> usedPacks, ref int rekeyed, PieceInstance? parent, double heightScale = 1, double absolute = 1)
    {
        var r = _defs.DefFor(o, extras, refScale, heightScale);
        if (r == null) return null;
        var def = r.Def;
        string packId = r.FromPack ?? extras.M.Id;
        double scale = r.Scale, baseScale = r.Scale;
        if (r.FromPack != null) scale = baseScale = Math.Round(absolute * r.Fit, 4);
        if (Math.Abs(o.ScaleX) < DefFactory.NoScale)
        {
            scale = baseScale = DefFactory.HiddenByScale;
            _hiddenByScale++;
        }
        if (o.ScaleX < 0 || o.ScaleZ < 0) _rep.Note($"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid}: the module mirrors this piece by a negative scale; it shows unmirrored here.");
        var (counters, text) = r.Script.Values(o.LuaState);
        var lua = o.SafeLua;
        if (def.Script != null) { counters = new(); text = new(); }

        if (r.FromPack == null && MatchKey(def) is { } mk && packDefs.TryGetValue(mk, out var cands))
        {
            (PieceDef def, string pack)? best = null;
            double bestScale = 0, bestMiss = double.MaxValue;
            foreach (var c in cands)
            {
                if (c.def.States.Count != def.States.Count) continue;
                if (c.def.ScriptHash != def.ScriptHash) continue;
                if (!counters.Keys.All(k => c.def.Counters.Any(x => x.Name == k)) || !text.Keys.All(k => c.def.TextLines.Any(x => x.Name == k))) continue;
                if (def.Counters.Count > c.def.Counters.Count || def.TextLines.Count > c.def.TextLines.Count) continue;
                if (c.def.Size.W <= 0 || c.def.Size.H <= 0) continue;
                double kx = def.Size.W / c.def.Size.W, kz = def.Size.H / c.def.Size.H;
                if (Math.Abs(kx - kz) > 0.01 * kx) continue;
                double miss = Math.Abs(kx - 1);
                bool exact = miss < 0.01;
                if (best == null || exact && miss < bestMiss)
                {
                    best = c;
                    bestScale = exact ? 1 : kx;
                    bestMiss = exact ? miss : bestMiss;
                }
            }
            if (best is { } b)
            {
                def = b.def;
                packId = b.pack;
                scale = Math.Round(scale * bestScale, 4);
                rekeyed++;
            }
        }
        usedPacks.Add(packId);
        const double ModuleScaleRoundingNoise = 0.002;
        if (Math.Abs(scale - 1) < ModuleScaleRoundingNoise) scale = 1;
        if (def.Script != null) { counters = new(); text = new(); }

        var inst = new PieceInstance
        {
            Id = $"p-{_nextId++:D6}", Def = def.Id,
            Pos = new Vec2(Math.Round(o.PosX, 4), Math.Round(o.PosZ, 4)), RotDeg = Facing(def, o.RotY),
            State = Math.Min(r.State, Math.Max(0, def.States.Count - 1)),
            Counters = counters, Text = text, Label = o.Nickname.Length > 0 ? o.Nickname : null, Scale = scale,
            Note = o.Description.Trim().Length > 0 ? o.Description.Trim() : null,
            Guid = o.Guid.Length > 0 ? o.Guid : null,
            Tags = o.Tags, Memo = _defs.RewriteUrls(o.Memo, extras, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid} memo"),
            Locked = parent == null && o.Locked, FaceDown = o.Flipped && def.Kind != "model" && def.Back != null, NotSticky = !o.Sticky,
            NotBoxSelected = !o.DragSelectable, MeasuresMoves = o.MeasureMovement,
        };
        if (lua.Trim().Length > 0)
        {
            inst.ScriptState = _defs.RewriteUrls(o.LuaState, extras, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid} script state");
            if (def.ScriptHash != DefFactory.Hash8(lua))
            {
                var tr = _defs.Translate(lua, extras, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid}");
                if (tr.Script != def.Script) inst.Script = tr.Script;
            }
        }
        var colour = r.FromPack == KTable.Client.Effects.EffectKinds.StandardPack && o.Name == "Bag" ? BagBoxes.Tint(o) : o.Color;
        if (colour is { } col && !string.Equals(col, def.Tint, StringComparison.OrdinalIgnoreCase)) inst.TintOverride = col;
        if (o.VectorLines.Count > 0) inst.Lines = LinesOf(o, def);
        if (!def.States.Any(s => s.Decals is { Count: > 0 }))
            inst.Decals = DecalsOf(o, def, _cfg.BaseSizes.CustomTokenArea, url => _defs.UiAsset(extras, url, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid} decal"));
        layer.Pieces.Add(inst);
        layer.Y[inst] = o.PosY;
        layer.Bottom[inst] = _defs.Bottom(o);
        layer.Top[inst] = _defs.Bottom(o) + _defs.BaseThickness(o) * Math.Abs(o.ScaleY);
        if (o.IsBag) StowContents(o, inst, extras, packDefs, layer, usedPacks, ref rekeyed);

        PlaceChildren(o, _defs.BakesChildren(o) ? Enumerable.Empty<ModuleObj>() : o.Children, inst, refScale, baseScale, absolute, extras, packDefs, layer, usedPacks, ref rekeyed);
        return inst;
    }

    Dictionary<string, double> _common = new();
    BundleEffects? _effects;
    BundleEffects Effects => _effects ??= BundleEffects.Load(_cfg.Output);
    int _stowed, _hiddenByScale;

    IEnumerable<ModuleObj> Held(ModuleObj bag) => bag.Contained.SelectMany(c => c.IsDeck ? Pile(c) : [c]);

    void StowContents(ModuleObj bag, PieceInstance holder, PackOut extras, Dictionary<string, List<(PieceDef def, string pack)>> packDefs, Layer layer, HashSet<string> usedPacks, ref int rekeyed)
    {
        holder.TakeOrder = bag.TakeOrder;
        foreach (var c in Held(bag))
        {
            var where = $"{layer.Name} {bag.Name} '{bag.Nickname}' {bag.Guid} holds {c.Name} '{c.Nickname}' {c.Guid}";
            if (c.Verdict.Hostile) _rep.HostileScript(c.Verdict.Reasons, where, c.Guid);
            if (DefFactory.NotTranslated(c) is { } why) { _rep.Skipped(why, where); continue; }
            if (c.IsInfiniteBag) { _rep.Skipped(c.Name + " (infinite bags are left out: place pieces from the Pieces drawer)", where); continue; }
            if (c.Name == BundleType && _defs.ModelOfBundle(c) == null)
            {
                if (EffectPiece(c, where, usedPacks) is { } fx)
                {
                    holder.Stowed ??= new();
                    holder.Stowed.Add(fx);
                    _stowed++;
                }
                continue;
            }
            if (!_defs.Shows(c)) { _rep.UnknownType(c.Name, where); continue; }
            var refScale = _common.TryGetValue(_defs.VisualKey(c), out var k) ? k : DefFactory.BaseScale(c);
            var ci = Instance(c, refScale, extras, packDefs, layer, usedPacks, ref rekeyed, null, 1, Math.Abs(c.ScaleX));
            if (ci == null) continue;
            holder.Stowed ??= new();
            foreach (var moved in CarriedBy(ci, layer).Prepend(ci))
            {
                layer.Pieces.Remove(moved);
                layer.Y.Remove(moved);
                layer.Bottom.Remove(moved);
                layer.Top.Remove(moved);
                layer.Glued.Remove(moved);
                holder.Stowed.Add(moved);
            }
            _stowed++;
        }
    }

    static List<PieceInstance> CarriedBy(PieceInstance carrier, Layer layer)
    {
        var found = new List<PieceInstance>();
        var todo = new Stack<PieceInstance>();
        todo.Push(carrier);
        while (todo.Count > 0)
            foreach (var id in todo.Pop().Attached)
                if (layer.Pieces.FirstOrDefault(p => p.Id == id) is { } next && !found.Contains(next)) { found.Add(next); todo.Push(next); }
        return found;
    }

    void PlaceChildren(ModuleObj o, IEnumerable<ModuleObj> children, PieceInstance? parent, double refScale, double baseScale, double absolute, PackOut extras,
        Dictionary<string, List<(PieceDef def, string pack)>> packDefs, Layer layer, HashSet<string> usedPacks, ref int rekeyed)
    {
        double th = o.RotY * Math.PI / 180;
        double parentHeight = parent != null && layer.HeightScale.TryGetValue(parent, out var hs) ? hs : Math.Abs(o.ScaleY);
        var (root, parentBottom) = parent != null && layer.Glued.TryGetValue(parent, out var glued) ? glued : (parent, 0.0);
        double parentFloor = _defs.LocalFloor(o);
        foreach (var c in children)
        {
            if (!_defs.Shows(c)) continue;
            double lx = c.PosX * o.ScaleX, lz = c.PosZ * o.ScaleZ;
            double childAbsolute = Math.Abs(c.StateList().states[0].ScaleX) * absolute;
            var ci = Instance(c, Math.Abs(c.StateList().states[0].ScaleX) * refScale, extras, packDefs, layer, usedPacks, ref rekeyed, parent, parentHeight, childAbsolute);
            if (ci == null) continue;
            ci.Pos = new Vec2(Math.Round(o.PosX + lx * Math.Cos(th) + lz * Math.Sin(th), 4), Math.Round(o.PosZ - lx * Math.Sin(th) + lz * Math.Cos(th), 4));
            ci.RotDeg = Norm(o.RotY + ci.RotDeg);
            if (_defs.ModelOfBundle(c) == null)
            {
                var sizedAtParentReferenceScale = ci.Scale * refScale;
                ci.Scale = Math.Round(sizedAtParentReferenceScale * baseScale, 4);
            }
            double floor = _defs.Bottom(c);
            layer.HeightScale[ci] = Math.Abs(c.ScaleY) * parentHeight;
            if (root != null) layer.Glued[ci] = (root, parentBottom + (floor - parentFloor) * parentHeight);
            else layer.Y[ci] = o.PosY;
            parent?.Attached.Add(ci.Id);
        }
    }

    static string? MatchKey(PieceDef d) => d.Kind == "model"
        ? (d.Mesh == null ? null : $"model|{d.Mesh}|{d.Texture}")
        : NoteCard.Is(d) ? $"note|{string.Join("|", d.TextLines.Select(t => t.Default))}"
        : (d.Image == null || d.Tags.Contains("block") ? null : $"flat|{d.Image}|{d.Back}");

    const double NoteTurn = 180;

    static double Facing(PieceDef? def, double moduleRot) => Norm(NoteCard.Is(def) ? moduleRot + NoteTurn : moduleRot);

    const double ThinnestStroke = 0.05;

    static List<Stroke> TableStrokes(ModuleSave master)
    {
        var strokes = new List<Stroke>();
        foreach (var l in master.VectorLines)
        {
            var points = l.Points.Select(p => new Vec2(Math.Round(p.X, 4), Math.Round(p.Z, 4))).ToList();
            if (l.Loop) points.Add(points[0]);
            strokes.Add(new Stroke { Id = $"d-{strokes.Count + 1:D6}", Points = points, Width = Math.Max(ThinnestStroke, l.Thickness), Color = l.Color });
        }
        return strokes;
    }

    List<LineDef> LinesOf(ModuleObj o, PieceDef def)
    {
        var (ux, uz) = _defs.LocalUnits(def, o);
        var lines = new List<LineDef>();
        foreach (var l in o.VectorLines)
            lines.Add(new LineDef
            {
                Points = l.Points.Select(p => new Vec2(Math.Round(-p.X * ux, 4), Math.Round(p.Z * uz, 4))).ToList(),
                Color = l.Color, Width = Math.Max(ThinnestStroke, l.Thickness), Loop = l.Loop,
            });
        return lines;
    }

    void LayNotes(ModuleSave master, Scenario scenario, PackOut extras, Layer layer, HashSet<string> usedPacks)
    {
        var notes = new List<(string title, string body)>();
        if (master.Notes.Trim().Length > 0) notes.Add(("Notes", master.Notes));
        notes.AddRange(master.Notebook);
        if (notes.Count == 0) return;
        const double Gap = 1, ShareOfMap = 0.12, Largest = 10, CharsPerLine = 42;
        var corners = scenario.Sheets.SelectMany(Corners).ToList();
        double right = corners.Count > 0 ? corners.Max(c => c.X) : 0, near = corners.Count > 0 ? corners.Min(c => c.Z) : 0;
        double mapWide = corners.Count > 0 ? right - corners.Min(c => c.X) : 0;
        double z = near;
        foreach (var (title, body) in notes)
        {
            int lines = body.Replace("\r", "").Trim('\n').Split('\n').Sum(l => 1 + l.Length / (int)CharsPerLine);
            double scale = Math.Round(Math.Clamp(Math.Max((double)lines / NoteCard.Lines, mapWide * ShareOfMap / NoteCard.Width), 1, Largest), 2);
            var def = _defs.NoteDef(extras, title, body, scale);
            var inst = new PieceInstance
            {
                Id = $"p-{_nextId++:D6}", Def = def.Id, RotDeg = NoteTurn,
                Pos = new Vec2(Math.Round(right + Gap + def.Size.W / 2, 4), Math.Round(z + def.Size.H / 2, 4)),
            };
            z += def.Size.H + Gap;
            layer.Pieces.Add(inst);
        }
        usedPacks.Add(extras.M.Id);
        _rep.Counts[$"{_cfg.Scenario.Id}: notes laid beside the map"] = notes.Count.ToString();
    }

    static IEnumerable<Vec2> Corners(SheetDef s)
    {
        double th = s.RotDeg * Math.PI / 180, c = Math.Cos(th), n = Math.Sin(th);
        foreach (var (x, z) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
        {
            double lx = x * s.Size.W / 2, lz = z * s.Size.H / 2;
            yield return new Vec2(s.Pos.X + lx * c + lz * n, s.Pos.Z - lx * n + lz * c);
        }
    }

    Dictionary<string, List<(PieceDef def, string pack)>> LoadPackDefs(string root)
    {
        var map = new Dictionary<string, List<(PieceDef, string)>>();
        var placeholder = new HashSet<string>();
        foreach (var pc in _cfg.Packs)
        {
            var path = Path.Combine(root, pc.Id, "pack.json");
            if (!File.Exists(path)) continue;
            var m = Json.Load<PackManifest>(path);
            foreach (var (sha, a) in m.Assets) if (a.W == 64 && a.H == 64 && a.Kind == "image" && m.Pieces.Count(p => p.Image == sha) > 3) placeholder.Add(sha);
            foreach (var d in m.Pieces)
            {
                if (d.Tags.Contains("effect")) continue;
                var k = MatchKey(d);
                if (k == null || (d.Image != null && placeholder.Contains(d.Image))) continue;
                if (!map.TryGetValue(k, out var list)) map[k] = list = new();
                list.Add((d, pc.Id));
            }
        }
        return map;
    }

    void Stack(Layer l)
    {
        var levels = Seat(l, new List<(double bottom, double y)>());
        Seat(l, levels);
        SeatGlued(l);
    }

    List<(double bottom, double y)> Seat(Layer l, List<(double bottom, double y)> levels)
    {
        const double Sunk = 0.02, Digits = 4;
        var order = l.Pieces.Where(p => !l.Glued.ContainsKey(p)).OrderBy(p => l.Bottom.GetValueOrDefault(p, l.Y[p])).ThenBy(p => l.Y[p]).ToList();
        var placed = new List<(PieceInstance p, double moduleTop, double ktTop)>();
        var held = new List<(double bottom, double y)>();
        foreach (var p in order)
        {
            bool bottomKnown = l.Bottom.TryGetValue(p, out var bottom);
            (PieceInstance p, double moduleTop, double ktTop)? under = null;
            foreach (var q in placed)
                if (l.Y[q.p] + Sunk < l.Y[p] && q.moduleTop <= l.Y[p] + Sunk && (under == null || q.moduleTop > under.Value.moduleTop) && Overlaps(q.p, p)) under = q;
            double rest = 0;
            if (under is { } u)
            {
                p.Stack = u.p.Stack + 1;
                rest = u.ktTop;
                if (bottomKnown && bottom >= u.moduleTop - Sunk) held.Add((bottom, rest));
            }
            else
            {
                p.Stack = 0;
                if (bottomKnown && LevelOf(bottom, levels) is { } level) rest = level;
            }
            p.Y = rest > 0 ? Math.Round(rest, (int)Digits) : 0;
            placed.Add((p, l.Top.GetValueOrDefault(p, l.Y[p]), rest + (Lookup(p.Def)?.Thickness ?? 0) * p.Scale));
        }
        return held;
    }

    static double? LevelOf(double bottom, List<(double bottom, double y)> levels)
    {
        const double SameLevel = 0.02;
        var near = levels.Where(lv => Math.Abs(lv.bottom - bottom) < SameLevel).ToList();
        return near.Count == 0 ? null : near.GroupBy(lv => lv.y).OrderByDescending(g => g.Count()).First().Key;
    }

    static void SeatGlued(Layer l)
    {
        const double Above = 0.02, Digits = 4;
        foreach (var family in l.Glued.GroupBy(g => g.Value.Root))
        {
            var root = family.Key;
            double lowest = Math.Min(0, family.Min(g => g.Value.Bottom)), floor = root.Y;
            int under = root.Stack;
            root.Y = Math.Round(floor - lowest, (int)Digits);
            if (-lowest > Above) root.Stack = under + 1;
            foreach (var (p, (_, bottom)) in family)
            {
                p.Y = Math.Round(floor + bottom - lowest, (int)Digits);
                p.Stack = under + (bottom - lowest > Above ? 1 : 0);
            }
        }
    }

    sealed record Footprint(Vec2 Centre, double HalfW, double HalfH, double Turn)
    {
        public (double x, double z) Across => (Math.Cos(Turn), -Math.Sin(Turn));
        public (double x, double z) Along => (Math.Sin(Turn), Math.Cos(Turn));
        public double Reach((double x, double z) axis) =>
            HalfW * Math.Abs(Across.x * axis.x + Across.z * axis.z) + HalfH * Math.Abs(Along.x * axis.x + Along.z * axis.z);
    }

    Footprint FootprintOf(PieceInstance p)
    {
        const double Smallest = 0.1;
        var def = Lookup(p.Def);
        return new Footprint(p.Pos, Math.Max(Smallest, (def?.Size.W ?? 1) * p.Scale / 2), Math.Max(Smallest, (def?.Size.H ?? 1) * p.Scale / 2), p.RotDeg * Math.PI / 180);
    }

    bool Overlaps(PieceInstance a, PieceInstance b)
    {
        var fa = FootprintOf(a);
        var fb = FootprintOf(b);
        double dx = fb.Centre.X - fa.Centre.X, dz = fb.Centre.Z - fa.Centre.Z;
        foreach (var axis in new[] { fa.Across, fa.Along, fb.Across, fb.Along })
            if (Math.Abs(dx * axis.x + dz * axis.z) > fa.Reach(axis) + fb.Reach(axis)) return false;
        return true;
    }

    readonly Dictionary<string, PieceDef> _lookup = new();
    PieceDef? Lookup(string id) => _lookup.TryGetValue(id, out var d) ? d : null;
    public void Index(IEnumerable<PieceDef> defs) { foreach (var d in defs) _lookup[d.Id] = d; }

    static double Norm(double deg)
    {
        deg %= 360;
        if (deg < 0) deg += 360;
        return Math.Round(deg, 3);
    }
}
