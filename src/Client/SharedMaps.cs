using KTable.Core;
using KTable.Core.Protocol;

namespace KTable.Client;

public sealed record Joined(long Freed, int Files, int Waiting);

public static class SharedMaps
{
    public const string RecordFile = "shared-maps.json";
    const string Tiles = "tiles", Index = "index.json", Scenarios = "scenarios", Aside = "_";
    const int Block = 1 << 20;
    static readonly string[] Unfinished = [".part", ".tmp"];

    sealed record Folder(string Rel, string Dir);

    public static string RecordBeside(string packagesRoot) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(packagesRoot))) ?? packagesRoot, RecordFile);

    public static Joined Join(string packagesRoot, string record, Func<bool>? stop = null)
    {
        if (!Directory.Exists(packagesRoot)) return new Joined(0, 0, 0);
        var known = File.Exists(record) ? BestEffort.Read(() => Json.Load<Dictionary<string, string>>(record), fallback: null) ?? new() : new Dictionary<string, string>();
        var done = new Dictionary<string, string>();
        var keepers = new Dictionary<string, Folder>(StringComparer.OrdinalIgnoreCase);
        long freed = 0;
        int files = 0, waiting = 0;
        foreach (var folder in Holders(packagesRoot))
        {
            var image = Path.GetFileName(folder.Dir);
            if (!keepers.TryGetValue(image, out var keeper)) { keepers[image] = folder; continue; }
            var stamp = $"{keeper.Rel}|{Made(keeper.Dir)}|{Made(folder.Dir)}";
            if (known.GetValueOrDefault(folder.Rel) == stamp) { done[folder.Rel] = stamp; continue; }
            if (stop?.Invoke() == true) { waiting++; continue; }
            var (bytes, count, whole) = Shared(keeper.Dir, folder.Dir, stop);
            freed += bytes;
            files += count;
            if (whole) done[folder.Rel] = stamp;
            else waiting++;
        }
        if (done.Count != known.Count || done.Any(d => known.GetValueOrDefault(d.Key) != d.Value))
            BestEffort.Try(() => Json.Save(record, done));
        return new Joined(freed, files, waiting);
    }

    static IEnumerable<Folder> Holders(string root)
    {
        var packages = Directory.GetDirectories(root).Where(d => !Path.GetFileName(d).StartsWith(Aside, StringComparison.Ordinal))
            .OrderBy(d => Path.GetFileName(d) == UploadAsset.ReliefPackage ? 0 : 1).ThenBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
        var own = Path.Combine(root, Scenarios);
        var held = packages.Where(d => !string.Equals(d, own, StringComparison.OrdinalIgnoreCase)).Select(d => (Rel: Path.GetFileName(d), Dir: d))
            .Concat(Directory.Exists(own) ? Directory.GetDirectories(own).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(d => (Rel: $"{Scenarios}/{Path.GetFileName(d)}", Dir: d)) : []);
        foreach (var (rel, dir) in held)
        {
            var tiles = Path.Combine(dir, Tiles);
            if (!Directory.Exists(tiles)) continue;
            foreach (var map in Directory.GetDirectories(tiles).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                if (File.Exists(Path.Combine(map, Index))) yield return new Folder($"{rel}/{Tiles}/{Path.GetFileName(map)}", map);
        }
    }

    static long Made(string dir) => Directory.GetCreationTimeUtc(dir).Ticks;

    static (long Freed, int Files, bool Whole) Shared(string keeper, string doubled, Func<bool>? stop)
    {
        long freed = 0;
        int files = 0;
        bool whole = true;
        foreach (var file in Directory.EnumerateFiles(doubled, "*", SearchOption.AllDirectories))
        {
            if (Unfinished.Any(end => file.EndsWith(end, StringComparison.OrdinalIgnoreCase))) continue;
            if (stop?.Invoke() == true) return (freed, files, false);
            var kept = Path.Combine(keeper, Path.GetRelativePath(doubled, file));
            if (!File.Exists(kept)) continue;
            var same = BestEffort.Read(() => (bool?)Same(kept, file), fallback: null);
            if (same == false) continue;
            if (same == null || !FileLinks.Replace(kept, file))
            {
                whole = false;
                continue;
            }
            freed += new FileInfo(file).Length;
            files++;
        }
        return (freed, files, whole);
    }

    static bool Same(string a, string b)
    {
        using var first = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, Block);
        using var second = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, Block);
        if (first.Length != second.Length) return false;
        byte[] one = new byte[Block], other = new byte[Block];
        while (true)
        {
            int read = first.ReadAtLeast(one, Block, throwOnEndOfStream: false);
            if (second.ReadAtLeast(other, Block, throwOnEndOfStream: false) != read) return false;
            if (read == 0) return true;
            if (!one.AsSpan(0, read).SequenceEqual(other.AsSpan(0, read))) return false;
        }
    }
}
