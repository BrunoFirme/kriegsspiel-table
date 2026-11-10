using KTable.Core.Model;

namespace KTable.Core;

public static class DataPaths
{
    public const string Name = "KriegsspielTable";
    static string? UserOverride => Environment.GetEnvironmentVariable("KTABLE_USER_DIR") is { Length: > 0 } d ? d : null;
    public static string Local => UserOverride is { } o ? Path.Combine(o, "local", Name) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), Name);
    public static string Documents => Path.Combine(DocumentsFolder, Name);
    public static string Scripts => Path.Combine(Local, "Scripts");
    public static string Captures => Path.Combine(Local, "captures");
    public static string Exports => Path.Combine(Local, "exports");
    public static string LegacyDocuments => UserOverride is { } o ? Path.Combine(o, "profile", "Documents", Name) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", Name);

    static string DocumentsFolder =>
        UserOverride is { } o ? Path.Combine(o, "documents") :
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } docs ? docs :
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents");
}

public sealed class PackageStore
{
    public string Root { get; }
    readonly Dictionary<string, PackManifest> _packs = new();
    readonly Dictionary<string, (PieceDef def, PackManifest pack)> _defs = new();

    public PackageStore(string root) { Root = root; }

    public static string DefaultRoot =>
        Environment.GetEnvironmentVariable("KTABLE_PACKAGES") is { Length: > 0 } env ? env :
        Path.Combine(DataPaths.Local, "packages");


    public string PackageDir(string id) => Path.Combine(Root, id);

    public IEnumerable<string> ListPackIds() =>
        Directory.Exists(Root) ? Directory.GetDirectories(Root).Where(d => File.Exists(Path.Combine(d, "pack.json"))).Select(Path.GetFileName).OfType<string>() : [];

    public IEnumerable<(string id, string path)> ListScenarios()
    {
        var dir = Path.Combine(Root, "scenarios");
        if (!Directory.Exists(dir)) yield break;
        foreach (var d in Directory.GetDirectories(dir))
            if (File.Exists(Path.Combine(d, "scenario.json"))) yield return (Path.GetFileName(d), Path.Combine(d, "scenario.json"));
    }

    public PackManifest? Pack(string id)
    {
        if (_packs.TryGetValue(id, out var p)) return p;
        var path = Path.Combine(PackageDir(id), "pack.json");
        if (!File.Exists(path)) return null;
        try
        {
            p = Json.Load<PackManifest>(path);
            if (p.Pieces == null || p.Assets == null || p.Pieces.Any(d => d?.Id == null)) throw new InvalidDataException("the file is incomplete");
        }
        catch (Exception e)
        {
            _damaged[id] = e.Message;
            return null;
        }
        _damaged.Remove(id);
        _packs[id] = p;
        foreach (var d in p.Pieces) _defs.TryAdd(d.Id, (d, p));
        return p;
    }

    readonly Dictionary<string, string> _damaged = new();

    public string? Problem(string id) => _damaged.GetValueOrDefault(id);

    public void LoadPacks(IEnumerable<string> ids) { foreach (var id in ids) Pack(id); }

    public void Forget(string id)
    {
        _damaged.Remove(id);
        if (!_packs.Remove(id, out var p)) return;
        foreach (var d in p.Pieces)
            if (_defs.TryGetValue(d.Id, out var x) && x.pack == p) _defs.Remove(d.Id);
    }

    public void RegisterDef(PackManifest pack, PieceDef def) => _defs[def.Id] = (def, pack);
    public void UnregisterDef(string defId) => _defs.Remove(defId);

    public PieceDef? Def(string defId) => _defs.TryGetValue(defId, out var x) ? x.def : null;
    public PackManifest? PackOfDef(string defId) => _defs.TryGetValue(defId, out var x) ? x.pack : null;

    public string? AssetPath(PackManifest pack, string? hash)
    {
        if (hash == null || !pack.Assets.TryGetValue(hash, out var a)) return null;
        return Path.Combine(PackageDir(pack.Id), a.File);
    }

    public string? AssetPathForDef(string defId, string? hash) =>
        PackOfDef(defId) is { } p ? AssetPath(p, hash) : null;

    public string TilesDir(SheetDef s) => Path.Combine(PackageDir(s.Package), "tiles", s.Image);

    public bool HasTiles(SheetDef s) => File.Exists(Path.Combine(TilesDir(s), "index.json"));

    public string? FindTilesPackage(string image)
    {
        if (!Directory.Exists(Root) || image.Length == 0) return null;
        bool Has(string dir) => File.Exists(Path.Combine(dir, "tiles", image, "index.json"));
        foreach (var d in Directory.EnumerateDirectories(Root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(d);
            if (string.Equals(name, "scenarios", StringComparison.OrdinalIgnoreCase)) continue;
            if (Has(d)) return name;
        }
        var scen = Path.Combine(Root, "scenarios");
        if (Directory.Exists(scen))
            foreach (var d in Directory.EnumerateDirectories(scen).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                if (Has(d)) return "scenarios/" + Path.GetFileName(d);
        return null;
    }

    public string? FindPackageWith(string rel)
    {
        if (!Directory.Exists(Root) || rel.Length == 0) return null;
        foreach (var d in Directory.EnumerateDirectories(Root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(d);
            if (string.Equals(name, "scenarios", StringComparison.OrdinalIgnoreCase)) continue;
            if (File.Exists(Path.Combine(d, rel))) return name;
        }
        var scen = Path.Combine(Root, "scenarios");
        if (Directory.Exists(scen))
            foreach (var d in Directory.EnumerateDirectories(scen).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                if (File.Exists(Path.Combine(d, rel))) return "scenarios/" + Path.GetFileName(d);
        return null;
    }

    public int RepointLostTiles(IEnumerable<SheetDef> sheets)
    {
        int moved = 0;
        foreach (var s in sheets)
        {
            if (HasTiles(s) || FindTilesPackage(s.Image) is not { } found) continue;
            s.Package = found;
            moved++;
        }
        return moved;
    }

    public IEnumerable<PieceDef> AllDefs => _defs.Values.Select(v => v.def);
    public IEnumerable<PackManifest> LoadedPacks => _packs.Values;
}
