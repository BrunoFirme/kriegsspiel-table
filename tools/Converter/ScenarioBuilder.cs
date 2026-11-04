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

    public ScenarioBuilder(Config cfg, AssetStore assets, Report rep, MakeWay way) { _cfg = cfg; _assets = assets; _rep = rep; _defs = new DefFactory(cfg, assets, rep); _way = way; }

    sealed class Layer
    {
        public string Name = "";
        public List<TtsObj> Objects = new();
        public List<TtsObj> Effects = new();
        public List<PieceInstance> Pieces = new();
        public Dictionary<PieceInstance, double> Y = new();
    }

    public async Task<Scenario> Build(string masterId)
    {
        var sc = _cfg.Scenario;
        Console.WriteLine($"Scenario {sc.Id} (master {masterId})");
        var master = TtsSave.Load(_cfg, masterId);
        await _assets.FetchAll(master);

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
        var boardSet = AddSheets(scenario, master, pkgRel, pkgDir, oldScenario);

        var layers = new List<Layer> { Collect("umpire", master, boardSet) };

        var common = layers.SelectMany(l => l.Objects).GroupBy(_defs.VisualKey)
            .ToDictionary(g => g.Key, g => g.GroupBy(DefFactory.BaseScale).OrderByDescending(x => x.Count()).ThenBy(x => x.Key).First().Key);

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
                if (o.Flipped) flipped++;
                Instance(o, common[_defs.VisualKey(o)], extras, packDefs, layer, usedPacks, ref rekeyed, null);
            }
        }
        var effects = BundleEffects.Load(root);
        var placedEffects = layers.ToDictionary(l => l, _ => new List<PieceInstance>());
        foreach (var layer in layers)
            foreach (var o in layer.Effects)
                if (Effect(o, effects, extras, packDefs, layer, usedPacks, ref rekeyed) is { } fx) placedEffects[layer].Add(fx);
        Index(extras.M.Pieces);
        foreach (var pd in packDefs.Values.SelectMany(v => v)) _lookup.TryAdd(pd.def.Id, pd.def);
        foreach (var layer in layers) Stack(layer);
        foreach (var (layer, placed) in placedEffects) layer.Pieces.AddRange(placed);
        if (placedEffects.Values.Sum(p => p.Count) is var effectCount and > 0) _rep.Counts[$"{sc.Id}: effects placed as Firme's effects"] = effectCount.ToString();

        scenario.UmpireBoard = layers[0].Pieces;
        scenario.Drawings["umpire"] = new();

        PruneExtras(extras, layers, scenario);
        usedPacks.Add(extras.M.Id);

        scenario.Packs = usedPacks.OrderBy(p => p == extras.M.Id ? 1 : 0).ThenBy(p => p, StringComparer.Ordinal).ToList();
        Json.Save(Path.Combine(pkgDir, "scenario.json"), scenario);
        var start = Path.Combine(pkgDir, "scenario.start.json");
        if (File.Exists(start)) File.Delete(start);

        ReportCounts(scenario, extras, rekeyed, flipped);
        return scenario;
    }

    void TranslateGlobal(Scenario scenario, TtsSave master, PackOut extras)
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

    public static IEnumerable<TtsObj> WithAttachedTerrain(TtsObj o)
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
            t["scaleX"] = c.ScaleX * o.ScaleX; t["scaleY"] = c.ScaleY * o.ScaleY; t["scaleZ"] = c.ScaleZ * o.ScaleZ;
            j.Remove("ChildObjects");
            yield return new TtsObj(j);
        }
    }

    HashSet<TtsObj> AddSheets(Scenario scenario, TtsSave master, string pkgRel, string pkgDir, string? oldScenario)
    {
        var sc = _cfg.Scenario;
        var boards = master.Objects.Where(IsSheet).OrderBy(b => b.PosY).ToList();
        var terrain = master.Objects.Where(o => o.IsTerrain).SelectMany(WithAttachedTerrain).ToList();
        var boardSet = boards.Concat(terrain).ToHashSet();
        if (boardSet.Count > 0) Progress.Stage("maps", boardSet.Count);
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

    SheetDef NewSheet(Scenario scenario, TtsObj o, string pkgRel, CacheEntry picture) => new()
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

    void AddTerrain(Scenario scenario, List<TtsObj> terrain, string pkgRel, string pkgDir, string? oldScenario)
    {
        var sc = _cfg.Scenario;
        var read = new List<(TtsObj o, ReliefSource src, CacheEntry picture)>();
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
                _rep.Note($"{sc.Id}: the 3D terrain {o.Guid} is not centred on its own object in TTS; its map sheet '{sheet.Name}' is laid on the object's centre, so check that it meets its neighbours.");
        }
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
        var usedAssets = extras.M.Pieces.SelectMany(d => new[] { d.Image, d.Back, d.Mesh, d.Texture, d.AddOnMesh, d.AddOnTexture }.Concat(d.States.SelectMany(s => new[] { s.Image, s.Back, s.Mesh, s.Texture }))).OfType<string>().ToHashSet();
        usedAssets.UnionWith(pieces.SelectMany(p => p.Decals).Select(d => d.Picture));
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
        if (flipped > 0) _rep.Note($"{sc.Id}: {flipped} pieces lie face down in TTS; converted face up (no flip flag in the instance model).");
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

    bool IsSheet(TtsObj o) =>
        o.ImageUrl != null && (o.Name == "Custom_Board" || (o.Name is "Custom_Tile" or "Custom_Token" && Math.Abs(o.ScaleX) >= 10)) && !o.IsFurniture;

    Layer Collect(string name, TtsSave save, HashSet<TtsObj> exclude)
    {
        var l = new Layer { Name = name };
        foreach (var o in save.Objects)
        {
            var where = $"{save.WorkshopId} {o.Name} '{o.Nickname}' {o.Guid}";
            if (o.Verdict.Hostile) _rep.HostileScript(o.Verdict.Reasons, where, o.Guid);
            if (exclude.Contains(o)) continue;
            if (IsSheet(o)) { if (name != "umpire") _rep.Skipped("map sheet duplicated in a side module", where); continue; }
            if (_cfg.ContainerTypes.Contains(o.Name)) { _rep.Skipped(o.Name + " (contents not placed in the scenario)", where); continue; }
            if (o.Name == BundleType && !_cfg.IsTableWall(o.BundleUrl)) { l.Effects.Add(o); continue; }
            if (_cfg.IgnoreTypes.Contains(o.Name) || o.Name.StartsWith("Die_")) { _rep.Skipped(o.Name, where); continue; }
            if (o.IsFurniture) { _rep.Skipped("table furniture (custom table / surround / under the table)", where); continue; }
            if (!DefFactory.IsPiece(o)) { _rep.UnknownType(o.Name, where); continue; }
            l.Objects.Add(o);
        }
        return l;
    }

    const string BundleType = "Custom_Assetbundle";

    PieceInstance? Effect(TtsObj o, BundleEffects effects, PackOut extras, Dictionary<string, List<(PieceDef def, string pack)>> packDefs,
        Layer layer, HashSet<string> usedPacks, ref int rekeyed)
    {
        var where = $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid}";
        PieceInstance? fx = null;
        if (_assets.Gone(o.BundleUrl, "bundle") is { } gone) _rep.LeftOut(o.Nickname.Length > 0 ? o.Nickname : "Effect", o.BundleUrl!, gone, where, o.Guid);
        else if (effects.For(o) is { } match)
        {
            fx = new PieceInstance
            {
                Id = $"p-{_nextId++:D6}", Def = match.Def.Id, Pos = new Vec2(Math.Round(o.PosX, 4), Math.Round(o.PosZ, 4)),
                RotDeg = Norm(o.RotY), Scale = match.Scale, Guid = o.Guid.Length > 0 ? o.Guid : null,
            };
            usedPacks.Add(KTable.Client.Effects.EffectKinds.StandardPack);
        }
        else _rep.Skipped(effects.Installed ? BundleType + " (a 3D model or an effect with no counterpart among Firme's effects)" : BundleType + " (Firme's effects are not installed)", where);
        PlaceChildren(o, o.Children, fx, Math.Abs(o.ScaleX), 1, extras, packDefs, layer, usedPacks, ref rekeyed);
        return fx;
    }

    public static List<DecalDef> DecalsOf(TtsObj o, PieceDef def, Func<string, string?> picture)
    {
        const double TtsDecalTurn = 180, Digits = 4;
        double u = def.Tags.Contains("token") ? Math.Max(def.Size.W, def.Size.H) / 3 : def.Size.H / 2;
        var list = new List<DecalDef>();
        foreach (var d in o.Decals.Take(DecalDef.MostOnAPiece))
        {
            if (picture(d.Url) is not { } pic) continue;
            bool back = d.PosY < 0;
            double w = Math.Abs(d.ScaleX) * u, h = Math.Abs(d.ScaleY) * u;
            if (w <= 0 || h <= 0) continue;
            list.Add(new DecalDef
            {
                Picture = pic, Name = d.Name, Face = back ? "back" : "front",
                X = Math.Round((back ? d.PosX : -d.PosX) * u, (int)Digits), Z = Math.Round(d.PosZ * u, (int)Digits),
                W = Math.Round(w, (int)Digits), H = Math.Round(h, (int)Digits), Rot = Math.Round(d.RotY - TtsDecalTurn, 2),
            });
        }
        return list;
    }

    PieceInstance? Instance(TtsObj o, double refScale, PackOut extras, Dictionary<string, List<(PieceDef def, string pack)>> packDefs,
        Layer layer, HashSet<string> usedPacks, ref int rekeyed, PieceInstance? parent)
    {
        var r = _defs.DefFor(o, extras, refScale);
        if (r == null) return null;
        var def = r.Def;
        string packId = extras.M.Id;
        double scale = r.Scale, baseScale = r.Scale;
        var (counters, text) = r.Script.Values(o.LuaState);
        var lua = o.SafeLua;
        if (def.Script != null) { counters = new(); text = new(); }

        if (MatchKey(def) is { } mk && packDefs.TryGetValue(mk, out var cands))
        {
            (PieceDef def, string pack)? best = null; double bestScale = 0, bestMiss = double.MaxValue;
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
                if (best == null || exact && miss < bestMiss) { best = c; bestScale = exact ? 1 : kx; bestMiss = exact ? miss : bestMiss; }
            }
            if (best is { } b)
            {
                def = b.def; packId = b.pack; scale = Math.Round(scale * bestScale, 4); rekeyed++;
            }
        }
        usedPacks.Add(packId);
        const double TtsScaleRoundingNoise = 0.002;
        if (Math.Abs(scale - 1) < TtsScaleRoundingNoise) scale = 1;
        if (def.Script != null) { counters = new(); text = new(); }

        var inst = new PieceInstance
        {
            Id = $"p-{_nextId++:D6}", Def = def.Id,
            Pos = new Vec2(Math.Round(o.PosX, 4), Math.Round(o.PosZ, 4)), RotDeg = Norm(o.RotY),
            State = Math.Min(r.State, Math.Max(0, def.States.Count - 1)),
            Counters = counters, Text = text, Label = o.Nickname.Length > 0 ? o.Nickname : null, Scale = scale,
            Note = o.Description.Trim().Length > 0 ? o.Description.Trim() : null,
            Guid = o.Guid.Length > 0 ? o.Guid : null,
            Tags = o.Tags, Memo = _defs.RewriteUrls(o.Memo, extras, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid} memo"),
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
        if (o.Color is { } col && !string.Equals(col, def.Tint, StringComparison.OrdinalIgnoreCase)) inst.TintOverride = col;
        inst.Decals = DecalsOf(o, def, url => _defs.UiAsset(extras, url, $"{layer.Name} {o.Name} '{o.Nickname}' {o.Guid} decal"));
        layer.Pieces.Add(inst);
        layer.Y[inst] = o.PosY;

        PlaceChildren(o, _defs.BakesChildren(o) ? Enumerable.Empty<TtsObj>() : o.Children, inst, refScale, baseScale, extras, packDefs, layer, usedPacks, ref rekeyed);
        return inst;
    }

    void PlaceChildren(TtsObj o, IEnumerable<TtsObj> children, PieceInstance? parent, double refScale, double baseScale, PackOut extras,
        Dictionary<string, List<(PieceDef def, string pack)>> packDefs, Layer layer, HashSet<string> usedPacks, ref int rekeyed)
    {
        double th = o.RotY * Math.PI / 180;
        foreach (var c in children)
        {
            if (!DefFactory.IsPiece(c)) continue;
            double lx = c.PosX * o.ScaleX, lz = c.PosZ * o.ScaleZ;
            var ci = Instance(c, Math.Abs(c.StateList().states[0].ScaleX) * refScale, extras, packDefs, layer, usedPacks, ref rekeyed, parent);
            if (ci == null) continue;
            ci.Pos = new Vec2(Math.Round(o.PosX + lx * Math.Cos(th) + lz * Math.Sin(th), 4), Math.Round(o.PosZ - lx * Math.Sin(th) + lz * Math.Cos(th), 4));
            ci.RotDeg = Norm(o.RotY + c.RotY);
            var sizedAtParentReferenceScale = ci.Scale * refScale;
            ci.Scale = Math.Round(sizedAtParentReferenceScale * baseScale, 4);
            layer.Y[ci] = o.PosY + 0.01;
            parent?.Attached.Add(ci.Id);
        }
    }

    static string? MatchKey(PieceDef d) => d.Kind == "model"
        ? (d.Mesh == null ? null : $"model|{d.Mesh}|{d.Texture}")
        : (d.Image == null || d.Tags.Contains("block") ? null : $"flat|{d.Image}|{d.Back}");

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
        var order = l.Pieces.OrderBy(p => l.Y[p]).ToList();
        var placed = new List<(PieceInstance p, double y, double rad)>();
        foreach (var p in order)
        {
            var def = Lookup(p.Def);
            double rad = def == null ? 0.5 : 0.5 * Math.Min(def.Size.W, def.Size.H) * p.Scale;
            int s = 0;
            foreach (var (q, qy, qr) in placed)
                if (l.Y[p] > qy + 0.02 && (p.Pos - q.Pos).Length < Math.Max(qr, 0.1)) s = Math.Max(s, q.Stack + 1);
            p.Stack = s;
            placed.Add((p, l.Y[p], rad));
        }
    }

    readonly Dictionary<string, PieceDef> _lookup = new();
    PieceDef? Lookup(string id) => _lookup.TryGetValue(id, out var d) ? d : null;
    public void Index(IEnumerable<PieceDef> defs) { foreach (var d in defs) _lookup[d.Id] = d; }

    static double Norm(double deg) { deg %= 360; if (deg < 0) deg += 360; return Math.Round(deg, 3); }
}
