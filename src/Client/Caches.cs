using KTable.Core;

namespace KTable.Client;

public enum CacheKind { TtsDownloads, FromHosts, Display }

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
    public static string FromHosts => Path.Combine(Root, "packages");
    public static string TtsDownloads => Path.Combine(Root, "tts");
    public static string Relief => Path.Combine(Root, "relief");

    public static IReadOnlyList<Cache> All =>
    [
        new(CacheKind.TtsDownloads, "TTS downloads", "What converting from TTS downloaded. Converted packs keep their own copies.",
            "Converted packs keep their own copies. Converting a module again downloads it again, except what has gone from the web since.", TtsDownloads),
        new(CacheKind.FromHosts, "Packs from hosts", "Packs fetched to join other people's games",
            "Joining a game fetches its packs again.", FromHosts),
        new(CacheKind.Display, "Display pictures", "Smaller copies of pictures, for drawing them on screen",
            "They are made again as the table draws them, a little slower the first time.", Display),
    ];

    public static long Size(string dir)
    {
        long total = 0;
        foreach (var f in Files(dir)) total += BestEffort.Read(() => f.Length, 0L);
        return total;
    }

    public static Cleared Clear(string dir)
    {
        long freed = 0;
        int left = 0;
        foreach (var f in Files(dir))
        {
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
