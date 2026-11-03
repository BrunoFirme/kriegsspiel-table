using KTable.Core;

namespace KTable.Client;

public sealed class LocalCopies(string installedRoot, string cacheRoot)
{
    const string TilesFolder = "tiles";
    const string TileIndex = "index.json";
    static readonly string[] NotPacks = ["_saves", "_installing", TilesFolder];

    Task<Dictionary<string, string>>? _byName;
    readonly Dictionary<string, string?> _tilesFrom = new();

    public void Warm() => _byName ??= Task.Run(IndexByName);

    public string? Candidate(string package, string rel)
    {
        var parts = rel.Replace('\\', '/').Split('/');
        if (parts[^1] is "pack.json" or TileIndex) return null;
        if (parts is [TilesFolder, var image, _, ..]) return TileFrom(package, image, rel);
        if (!FileHashes.IsHashNamed(rel)) return null;
        var same = Path.Combine(installedRoot, package, rel);
        if (File.Exists(same)) return same;
        return _byName is { IsCompletedSuccessfully: true } index && index.Result.TryGetValue(Path.GetFileName(rel), out var elsewhere) ? elsewhere : null;
    }

    string? TileFrom(string package, string image, string rel)
    {
        if (!_tilesFrom.TryGetValue(image, out var dir))
        {
            if (!File.Exists(Path.Combine(cacheRoot, package, TilesFolder, image, TileIndex))) return null;
            _tilesFrom[image] = dir = SameTiles(package, image);
        }
        if (dir == null) return null;
        var path = Path.Combine(dir, Path.GetRelativePath(Path.Combine(TilesFolder, image), rel));
        return File.Exists(path) ? path : null;
    }

    string? SameTiles(string package, string image)
    {
        var hostIndex = Path.Combine(cacheRoot, package, TilesFolder, image, TileIndex);
        if (new PackageStore(installedRoot).FindTilesPackage(image) is not { } found) return null;
        var dir = Path.Combine(installedRoot, found, TilesFolder, image);
        return BestEffort.Read(() => File.ReadAllBytes(hostIndex).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(dir, TileIndex))), false) ? dir : null;
    }

    public bool CopyTo(string from, string to)
    {
        if (FileHashes.IsHashNamed(to) && !BestEffort.Read(() => FileHashes.Sha256(from) == Path.GetFileNameWithoutExtension(to), false)) return false;
        try
        {
            Json.CopyAtomic(from, to);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    Dictionary<string, string> IndexByName()
    {
        var byName = new Dictionary<string, string>(StringComparer.Ordinal);
        var folders = new Stack<string>();
        folders.Push(installedRoot);
        while (folders.TryPop(out var folder))
        {
            foreach (var sub in BestEffort.Read(() => Directory.GetDirectories(folder), []))
                if (!NotPacks.Contains(Path.GetFileName(sub), StringComparer.OrdinalIgnoreCase)) folders.Push(sub);
            foreach (var file in BestEffort.Read(() => Directory.GetFiles(folder), []))
                if (FileHashes.IsHashNamed(file)) byName.TryAdd(Path.GetFileName(file), file);
        }
        return byName;
    }
}
