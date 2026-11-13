using System.IO.Compression;

namespace KTable.Core;

public sealed class TooLargeException(string message) : IOException(message);

public static class ContentLimits
{
    public const long MaxFileBytes = 1L << 30;
    public const long MaxItemBytes = 16L << 30;
    public const int MaxFiles = 250_000;
    public const int MaxExpansion = 250;
    public const long ExpansionCheckedAbove = 1 << 20;
    public const long MaxManifestBytes = 64L << 20;
    public const long MaxModelBytes = 256L << 20;
    public const int MaxPictureSide = 16384;
    public const long MaxPicturePixels = 1L << 26;
    public const long SpareDiskBytes = 256L << 20;

    public static void CheckArchive(IEnumerable<ZipArchiveEntry> entries, Func<ZipArchiveEntry, bool> taken)
    {
        long total = 0;
        int files = 0;
        foreach (var e in entries)
        {
            if (!taken(e)) continue;
            if (++files > MaxFiles) throw new TooLargeException($"it holds more than {MaxFiles:N0} files");
            CheckFile(e.FullName, e.Length);
            if (e.Length > ExpansionCheckedAbove && e.Length > Math.Max(1, e.CompressedLength) * MaxExpansion)
                throw new TooLargeException($"{e.FullName} unpacks to {Describe(e.Length)} from {Describe(e.CompressedLength)}, more than a real file does");
            total += e.Length;
            if (total > MaxItemBytes) throw new TooLargeException($"it unpacks to more than {Describe(MaxItemBytes)}");
        }
    }

    public static long CheckFolder(string dir)
    {
        long total = 0;
        int files = 0;
        foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (++files > MaxFiles) throw new TooLargeException($"it holds more than {MaxFiles:N0} files");
            CheckFile(Path.GetRelativePath(dir, f.FullName), f.Length);
            total += f.Length;
            if (total > MaxItemBytes) throw new TooLargeException($"it holds more than {Describe(MaxItemBytes)}");
        }
        return total;
    }

    static void CheckFile(string name, long bytes)
    {
        if (bytes < 0 || bytes > MaxFileBytes) throw new TooLargeException($"{name} is {Describe(bytes)}, more than the {Describe(MaxFileBytes)} a file may be");
    }

    public static void CheckFreeSpace(string targetDir, long bytes)
    {
        var free = BestEffort.Read(() => new DriveInfo(Path.GetPathRoot(Path.GetFullPath(targetDir))!).AvailableFreeSpace, fallback: long.MaxValue);
        if (bytes + SpareDiskBytes > free) throw new IOException($"it needs {Describe(bytes)} and the disk has {Describe(free)} free");
    }

    public static void Extract(ZipArchiveEntry e, string to)
    {
        var tmp = to + ".part";
        try
        {
            using (var src = e.Open())
            using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                CopyAtMost(src, dst, e.Length, e.FullName);
            File.Move(tmp, to, true);
        }
        finally { BestEffort.DeleteFile(tmp); }
    }

    public static byte[] Read(ZipArchiveEntry e, long max = MaxManifestBytes)
    {
        if (e.Length > max) throw new TooLargeException($"{e.FullName} is {Describe(e.Length)}, more than the {Describe(max)} it may be");
        using var src = e.Open();
        using var ms = new MemoryStream();
        CopyAtMost(src, ms, e.Length, e.FullName);
        return ms.ToArray();
    }

    static void CopyAtMost(Stream src, Stream dst, long declared, string name)
    {
        var buffer = new byte[81920];
        long copied = 0;
        int n;
        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied += n;
            if (copied > declared) throw new TooLargeException($"{name} unpacks to more than it says it holds");
            dst.Write(buffer, 0, n);
        }
    }

    public static bool PictureFits(int w, int h) =>
        w > 0 && h > 0 && w <= MaxPictureSide && h <= MaxPictureSide && (long)w * h <= MaxPicturePixels;

    public static bool PictureAllowed(string path) =>
        PictureHeader.Of(path) is not var (w, h) || PictureFits(w, h);

    public static bool PictureAllowed(ReadOnlySpan<byte> head, string? extension = null) =>
        PictureHeader.Read(head, extension) is not var (w, h) || PictureFits(w, h);

    public static bool ModelAllowed(string path, long most = MaxModelBytes) =>
        BestEffort.Read(() => new FileInfo(path).Length, fallback: long.MaxValue) <= most;

    public static string Describe(long bytes) => bytes switch
    {
        >= 1L << 30 => FormattableString.Invariant($"{bytes / (double)(1L << 30):0.#} GB"),
        >= 1L << 20 => FormattableString.Invariant($"{bytes / (double)(1L << 20):0.#} MB"),
        >= 1L << 10 => FormattableString.Invariant($"{bytes / (double)(1L << 10):0.#} KB"),
        _ => FormattableString.Invariant($"{bytes} bytes"),
    };
}
