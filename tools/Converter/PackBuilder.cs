using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter;

public sealed class PackBuilder
{
    readonly Config _cfg;
    readonly AssetStore _assets;
    readonly Report _rep;
    readonly DefFactory _defs;
    readonly MakeWay _way;
    BundleEffects? _effects;
    int _bundled;

    public PackBuilder(Config cfg, AssetStore assets, Report rep, MakeWay way)
    {
        _cfg = cfg;
        _assets = assets;
        _rep = rep;
        _defs = new DefFactory(cfg, assets, rep);
        _way = way;
    }

    public static IEnumerable<string> Urls(TtsSave save) =>
        save.Objects.SelectMany(o => o.Walk()).SelectMany(o => o.Urls().Concat(ScriptUrls(o.Lua)).Concat(ScriptUrls(o.LuaState))).Concat(ScriptUrls(save.Lua)).Concat(ScriptUrls(save.LuaState))
            .Concat(save.SkyUrl is { } sky ? new[] { sky } : []);

    public static IEnumerable<string> PieceUrls(TtsSave save) =>
        save.Objects.SelectMany(o => o.Walk()).Where(o => o.Name != "Custom_Assetbundle" && !o.IsFurniture).SelectMany(o => o.Urls());

    public static IReadOnlyDictionary<string, string> Names(TtsSave save)
    {
        var names = new Dictionary<string, string>();
        foreach (var o in save.Objects.SelectMany(o => o.Walk()))
        {
            if (o.Nickname.Length == 0) continue;
            foreach (var u in o.Urls()) names.TryAdd(u.Trim(), o.Nickname);
        }
        return names;
    }

    int Count(TtsObj o) => _cfg.ContainerTypes.Contains(o.Name) || o.IsDeck ? 1 + o.Contained.Sum(Count) : 1;

    public static IEnumerable<string> ScriptUrls(string lua)
    {
        if (lua.Length == 0) yield break;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(lua, @"""(https?://[^""\s]+)"""))
            yield return m.Groups[1].Value;
    }

    void KtDef(TtsObj o, PackOut pack, string where)
    {
        PieceDef def;
        try { def = KTable.Core.Json.Deserialize<PieceDef>(o.GmNotes[KtMarker.Def.Length..]); }
        catch (Exception e) { _rep.DroppedScript("unreadable Kriegsspiel Table piece data: " + e.Message, where); return; }
        var (states, _) = o.StateList();
        string? Re(string? sha, string? url, string kind)
        {
            if (sha == null) return null;
            if (url == null) { _rep.Missing(sha, "no URL in the module", where); return _defs.Placeholder(pack); }
            return _defs.Asset(pack, url, kind, where) ?? _defs.Placeholder(pack);
        }
        var main = states.Count > 0 ? states[0] : o;
        def.Image = Re(def.Image, main.ImageUrl, "image");
        def.Back = Re(def.Back, main.BackUrl, "image");
        def.Mesh = Re(def.Mesh, main.MeshUrl, "mesh");
        def.Texture = Re(def.Texture, main.DiffuseUrl, "image");
        for (int i = 0; i < def.States.Count; i++)
        {
            var src = i < states.Count ? states[i] : main;
            var st = def.States[i];
            st.Image = Re(st.Image, src.ImageUrl, "image");
            st.Back = Re(st.Back, src.BackUrl, "image");
            st.Mesh = Re(st.Mesh, src.MeshUrl, "mesh");
            st.Texture = Re(st.Texture, src.DiffuseUrl, "image");
        }
        pack.M.Pieces.RemoveAll(p => p.Id == def.Id);
        pack.M.Pieces.Add(def);
    }

    public async Task<PackOut> Build(PackCfg pc)
    {
        if (_cfg.ExcludedWorkshopIds.Contains(pc.WorkshopId)) throw new InvalidOperationException($"{pc.WorkshopId} is excluded");
        Console.WriteLine($"Pack {pc.Id} ({pc.WorkshopId})");
        var save = TtsSave.Load(_cfg, pc.WorkshopId);
        await _assets.FetchAll(save);
        _defs.Table = save.Table;
        Progress.Stage("pieces", save.Objects.Sum(Count));
        _way.Pack(pc.Id, _rep);
        var pack = new PackOut(_cfg.Output, pc.Id, pc.Name.Length > 0 ? pc.Name : save.SaveName, pc.Short, save.WorkshopId.Length > 0 ? save.WorkshopId : null);
        _defs.Context = pc.Id;
        int objects = 0, effects = 0;
        foreach (var o in save.Objects) Visit(o, pack, ref objects, ref effects);
        pack.M.Pieces.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        pack.Save();
        Prune(pack);
        _rep.Counts[$"{pc.Id}: objects converted"] = objects.ToString();
        _rep.Counts[$"{pc.Id}: piece defs"] = pack.M.Pieces.Count.ToString();
        _rep.Counts[$"{pc.Id}: assets"] = pack.M.Assets.Count.ToString();
        if (effects > 0) _rep.Counts[$"{pc.Id}: effects (asset bundles)"] = effects.ToString();
        if (_bundled > 0) _rep.Counts[$"{pc.Id}: figures and props read from asset bundles"] = _bundled.ToString();
        Console.WriteLine($"  → {pack.M.Pieces.Count} defs, {pack.M.Assets.Count} assets");
        return pack;
    }

    void Visit(TtsObj o, PackOut pack, ref int objects, ref int effects)
    {
        var where = $"{pack.M.Id} {o.Name} '{o.Nickname}' {o.Guid}";
        Progress.Step(o.Nickname.Length > 0 ? o.Nickname : o.Name);
        if (o.Verdict.Hostile) _rep.HostileScript(o.Verdict.Reasons, where, o.Guid);
        if (_cfg.ContainerTypes.Contains(o.Name) || o.IsDeck)
        {
            foreach (var c in o.Contained) Visit(c, pack, ref objects, ref effects);
            return;
        }
        if (o.GmNotes.StartsWith(KtMarker.Def))
        {
            KtDef(o, pack, where);
            objects++;
            return;
        }
        if (o.Description.StartsWith(KtMarker.Pack)) return;
        if (o.Name == "Custom_Assetbundle")
        {
            if (pack.M.Id.Contains("effects")) { EffectDef(o, pack); effects++; }
            else if (_defs.ModelOfBundle(o) != null) _bundled++;
            else _rep.Skipped(o.Name, where);
            foreach (var c in o.Children.Where(_defs.Shows))
                if (_defs.DefFor(c, pack, Math.Abs(c.StateList().states[0].ScaleX) * Math.Abs(o.ScaleX)) != null) objects++;
            return;
        }
        if (_cfg.IgnoreTypes.Contains(o.Name) || o.Name.StartsWith("Die_")) { _rep.Skipped(o.Name, where); return; }
        if (DefFactory.NotTranslated(o) is { } why) { _rep.Skipped(why, where); return; }
        if (!DefFactory.IsPiece(o)) { _rep.UnknownType(o.Name, where); return; }
        if (o.IsFurniture) { _rep.Skipped("table furniture (custom table / surround / under the table)", where); return; }
        if (o.IsTerrain) { _rep.Skipped("3D terrain (laid as a map sheet of the scenario)", where); return; }
        var r = _defs.DefFor(o, pack);
        if (r == null) return;
        objects++;
        if (Math.Max(r.Def.Size.W, r.Def.Size.H) >= 40 && !r.Def.Tags.Contains("map")) r.Def.Tags.Add("map");
    }

    void EffectDef(TtsObj o, PackOut pack)
    {
        if (_cfg.IsTableWall(o.BundleUrl)) { _rep.Skipped("Custom_Assetbundle table walls", $"{pack.M.Id} {o.Guid}"); return; }
        var e = _defs.Entry(o.BundleUrl);
        var name = o.Nickname.Length > 0 ? o.Nickname : "Effect " + DefFactory.Hash8(o.BundleUrl ?? o.Guid)[..4];
        if (e == null) _rep.Missing(o.BundleUrl ?? "(none)", _assets.Why(o.BundleUrl ?? "") ?? "unresolved", $"{pack.M.Id} effect '{name}'");
        var key = $"effect|{e?.Sha ?? o.BundleUrl}";
        if (pack.ByKey.ContainsKey(key)) return;
        var img = _defs.Placeholder(pack);
        var d = new PieceDef
        {
            Id = $"{pack.Short}.{DefFactory.Slug(name)}-{DefFactory.Hash8(key)}",
            Name = name, Image = img, Size = new Size2(2 * Math.Abs(o.ScaleX), 2 * Math.Abs(o.ScaleZ)), Thickness = 0.02,
            Tags = new() { "effect", "bundle:" + (e?.Sha ?? "missing") },
        };
        _effects ??= BundleEffects.Load(_cfg.Output);
        if (_effects.For(o) is { } fx)
        {
            d.Effect = Json.Deserialize<EffectDef>(Json.Serialize(fx.Def.Effect!));
            d.Size = new Size2(Math.Round(fx.Def.Size.W * fx.Scale, 4), Math.Round(fx.Def.Size.H * fx.Scale, 4));
        }
        pack.ByKey[key] = d;
        pack.M.Pieces.Add(d);
    }

    public static void Prune(PackOut pack)
    {
        var dir = Path.Combine(pack.Dir, "assets");
        if (!Directory.Exists(dir)) return;
        var keep = pack.M.Assets.Values.Select(a => Path.GetFullPath(Path.Combine(pack.Dir, a.File))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(dir))
            if (!keep.Contains(Path.GetFullPath(f))) File.Delete(f);
    }
}
