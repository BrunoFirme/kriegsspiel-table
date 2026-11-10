using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed record Brought(IReadOnlyList<string> Packs, int MapFiles)
{
    public static readonly Brought Nothing = new([], 0);
}

public sealed class FetchedFiles
{
    const string PackFile = "pack.json";
    const string TilesFolder = "tiles";
    const string TileIndex = "index.json";
    const string Shared = UploadAsset.ReliefPackage;

    readonly PackageStore _installed;
    readonly PackageStore _fetched;
    readonly HashSet<string> _keptPacks;

    FetchedFiles(PackageStore installed, PackageStore fetched)
    {
        _installed = installed;
        _fetched = fetched;
        _keptPacks = new HashSet<string>(installed.ListPackIds(), StringComparer.OrdinalIgnoreCase);
    }

    public static Brought ForSave(string savePath, PackageStore installed, string fetchedRoot) =>
        HasFetched(installed, fetchedRoot) && BestEffort.Read(() => Json.Load<SaveFile>(savePath).Snapshot, (GameState?)null) is { } game
            ? Install(game, installed, fetchedRoot) : Brought.Nothing;

    public static Brought Install(GameState game, PackageStore installed, string fetchedRoot)
    {
        if (!HasFetched(installed, fetchedRoot)) return Brought.Nothing;
        var f = new FetchedFiles(installed, new PackageStore(fetchedRoot));
        var packs = game.Packs.Distinct().Where(f.BringPack).ToList();
        int files = game.Sheets.Sum(s => f.BringTiles(s) + f.BringRelief(s)) + WebFiles(game).Count(rel => f.Bring(Shared, rel));
        return new(packs, files);
    }

    bool BringPack(string id)
    {
        if (!PackagePaths.IsName(id) || _keptPacks.Contains(id) || _fetched.Pack(id)?.Id != id) return false;
        BringFolder(id, "", PackFile);
        return File.Exists(Path.Combine(_installed.PackageDir(id), PackFile));
    }

    int BringTiles(SheetDef sheet)
    {
        if (!sheet.HasPicture || _installed.FindTilesPackage(sheet.Image) != null || _fetched.FindTilesPackage(sheet.Image) is not { } from) return 0;
        return BringFolder(from, Path.Combine(TilesFolder, sheet.Image), TileIndex);
    }

    int BringRelief(SheetDef sheet)
    {
        if (sheet.Relief is not { Length: > 0 } sha) return 0;
        var header = ReliefFiles.HeaderRel(sha);
        if (_installed.FindPackageWith(header) != null || _fetched.FindPackageWith(header) is not { } from) return 0;
        if (BestEffort.Read(() => Json.Load<ReliefIndex>(Path.Combine(_fetched.PackageDir(from), header)), (ReliefIndex?)null) is not { } index) return 0;
        var holders = new[] { from, sheet.Package, Shared };
        int chunks = ReliefFiles.Files(sha, index).Where(rel => rel != header)
            .Count(rel => holders.FirstOrDefault(p => File.Exists(Path.Combine(_fetched.PackageDir(p), rel))) is { } p && Bring(p, rel));
        return chunks + (Bring(from, header) ? 1 : 0);
    }

    static IEnumerable<string> WebFiles(GameState game) =>
        game.Layers.Values.SelectMany(l => l)
            .SelectMany(p => p.Decals.Select(d => d.Picture).Append(p.FacePicture).Append(p.WebMesh).Append(p.WebTexture))
            .Concat(game.Decals.Values.SelectMany(l => l).Select(d => d.Picture))
            .Concat(game.DecalPalette.Select(d => d.Picture))
            .Append(game.Lighting.Backdrop)
            .OfType<string>().Distinct()
            .Where(f => UploadAsset.IsWebPicture(Shared, f) || UploadAsset.IsWebModel(Shared, f));

    int BringFolder(string package, string folder, string last)
    {
        var dir = Path.Combine(_fetched.PackageDir(package), folder);
        var rels = BestEffort.Read(() => Directory.GetFiles(dir, "*", SearchOption.AllDirectories), []).Where(PackagePaths.IsHostFileType)
            .Select(f => Path.Combine(folder, Path.GetRelativePath(dir, f))).ToList();
        var lastRel = Path.Combine(folder, last);
        int copied = rels.Where(r => !string.Equals(r, lastRel, StringComparison.OrdinalIgnoreCase)).Count(r => Bring(package, r));
        return copied + (Bring(package, lastRel) ? 1 : 0);
    }

    bool Bring(string package, string rel)
    {
        if (_keptPacks.Contains(package)) return false;
        var from = Path.Combine(_fetched.PackageDir(package), rel);
        var to = Path.Combine(_installed.PackageDir(package), rel);
        if (File.Exists(to) || !File.Exists(from)) return false;
        try
        {
            Json.CopyAtomic(from, to, overwrite: false);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    static bool HasFetched(PackageStore installed, string fetchedRoot) =>
        Directory.Exists(fetchedRoot) && !string.Equals(Path.GetFullPath(installed.Root).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(fetchedRoot).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
}
