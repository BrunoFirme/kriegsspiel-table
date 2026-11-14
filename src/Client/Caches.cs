using System.Collections.Concurrent;
using KTable.Core;

namespace KTable.Client;

public enum CacheKind { ModuleDownloads, FromHosts, Display }

public sealed record Cache(CacheKind Kind, string Name, string Holds, string Costs, string Path)
{
    public string InSentence => Name.Length > 1 && char.IsUpper(Name[1]) ? Name : char.ToLowerInvariant(Name[0]) + Name[1..];
}

public sealed record Cleared(long Freed, int Left);

public static class Caches
{
    static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

    public static string Root => Path.Combine(DataPaths.Local, "cache");
    public static string Display => Path.Combine(Root, "display");
    public static string GroundCards => Path.Combine(Display, "ground");
    public static string FromHosts => Path.Combine(Root, "packages");
    public static string ModuleDownloads => Path.Combine(Root, "tts");
    public static string Relief => Path.Combine(Root, "relief");

    public static IReadOnlyList<Cache> All =>
    [
        new(CacheKind.ModuleDownloads, "Conversion cache", "What converting modules downloaded, cleared a day after the last conversion. Converted packs keep their own copies.",
            "Converted packs keep their own copies. Converting a module again downloads it again, except what has gone from the web since.", ModuleDownloads),
        new(CacheKind.FromHosts, "Join cache", "Packs and maps fetched to join other people's games. A file unused for 30 days is cleared, unless a mirrored game needs it.",
            "Joining a game fetches them again. Hosting a mirrored game installs from here the packs and maps this PC lacks; once cleared, a mirror opens without them.", FromHosts),
        new(CacheKind.Display, "Picture cache", "Smaller copies of pictures, for drawing them on screen. A file unused for 30 days is cleared.",
            "They are made again as the table draws them, a little slower the first time.", Display),
    ];

    public static readonly TimeSpan ModuleDownloadsKept = TimeSpan.FromDays(1), WorkFilesKept = TimeSpan.FromDays(1);
    const string UsedMark = "used";

    public static void Used(string dir, DateTime utcNow) => BestEffort.Try(() =>
    {
        Directory.CreateDirectory(dir);
        var mark = Path.Combine(dir, UsedMark);
        if (!File.Exists(mark)) File.WriteAllBytes(mark, []);
        File.SetLastWriteTimeUtc(mark, utcNow);
    });

    public static Cleared? Expire(string dir, DateTime utcNow, TimeSpan kept, Func<bool>? stop = null) =>
        LastUsed(dir) is { } last && utcNow - last >= kept ? Clear(dir, stop) : null;

    static DateTime? LastUsed(string dir)
    {
        var mark = new FileInfo(Path.Combine(dir, UsedMark));
        if (mark.Exists) return mark.LastWriteTimeUtc;
        var files = Files(dir);
        return files.Count == 0 ? null : files.Max(f => BestEffort.Read(() => f.LastWriteTimeUtc, DateTime.MinValue));
    }

    public const int UnusedDays = 30;
    const string HostSaves = "_saves";
    static readonly TimeSpan SeenEvery = TimeSpan.FromDays(1);
    static readonly ConcurrentDictionary<string, byte> SeenThisRun = new(StringComparer.OrdinalIgnoreCase);

    public static void Seen(string file) => Seen(file, DateTime.UtcNow);

    public static void Seen(string file, DateTime utcNow)
    {
        if (!SeenThisRun.TryAdd(file, 0) || !DataFolder.Inside(file, Root)) return;
        BestEffort.Try(() =>
        {
            if (utcNow - File.GetLastWriteTimeUtc(file) > SeenEvery) File.SetLastWriteTimeUtc(file, utcNow);
        });
    }

    public static Cleared ClearUnused(string dir, DateTime utcNow, Func<bool>? stop = null, Func<IReadOnlyCollection<string>?>? needed = null)
    {
        var before = utcNow.AddDays(-UnusedDays);
        var spared = new Lazy<IReadOnlyCollection<string>?>(() => needed == null ? [] : needed());
        bool Spared(FileInfo f) => spared.Value is not { } paths || paths.Any(p => DataFolder.Same(f.FullName, p) || DataFolder.Inside(f.FullName, p));
        bool HostSave(FileInfo f) => Path.GetRelativePath(dir, f.FullName).StartsWith(HostSaves + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        return Clear(dir, f => Written(f) < before && !HostSave(f) && !Spared(f), stop);
    }

    static DateTime Written(FileInfo f) => BestEffort.Read(() => f.LastWriteTimeUtc, DateTime.MaxValue);

    public static long Size(string dir)
    {
        long total = 0;
        foreach (var f in Files(dir)) total += BestEffort.Read(() => f.Length, 0L);
        return total;
    }

    public static Cleared Clear(string dir, Func<bool>? stop = null) => Clear(dir, _ => true, stop);

    public static Cleared ClearOlderThan(string dir, DateTime utc, Func<bool>? stop = null) =>
        Clear(dir, f => Written(f) < utc, stop);

    static Cleared Clear(string dir, Func<FileInfo, bool> goes, Func<bool>? stop)
    {
        long freed = 0;
        int left = 0;
        foreach (var f in Files(dir).Where(goes))
        {
            if (stop?.Invoke() == true) return new(freed, left);
            long length = BestEffort.Read(() => f.Length, 0L);
            if (Delete(f)) freed += length;
            else left++;
        }
        var folders = BestEffort.Read(() => new DirectoryInfo(dir).EnumerateDirectories("*", Walk).Select(d => d.FullName).ToList(), []);
        foreach (var d in folders.OrderByDescending(d => d.Length)) BestEffort.DeleteEmptyFolder(d);
        return new(freed, left);
    }

    static List<FileInfo> Files(string dir) =>
        Directory.Exists(dir) ? BestEffort.Read(() => new DirectoryInfo(dir).EnumerateFiles("*", Walk).ToList(), []) : [];

    static bool Delete(FileInfo f) => BestEffort.Try(() =>
    {
        if (f.IsReadOnly) f.IsReadOnly = false;
        f.Delete();
    });
}
