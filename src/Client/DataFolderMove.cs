using System.Security.Cryptography;
using KTable.Core;

namespace KTable.Client;

public sealed record DataMovePlan(string From, string To, bool UseAsIs, string? Refusal, long Bytes, int Files);

public sealed class DataMoveFailed(string message, Exception? cause = null) : IOException(message, cause);

public static class DataFolderMove
{
    static readonly string[] DataMarks = { "packages", "saves" };
    const string LockFiles = "*.lock";
    const int CopyBuffer = 1 << 20;

    public static string Home(string picked)
    {
        var whole = DataFolder.Whole(picked);
        return string.Equals(Path.GetFileName(whole), DataPaths.Name, StringComparison.OrdinalIgnoreCase) ? whole : Path.Combine(whole, DataPaths.Name);
    }

    public static DataMovePlan Look(string picked, string from)
    {
        var to = Home(picked);
        DataMovePlan Refused(string why) => new(from, to, false, why, 0, 0);
        if (DataFolder.Same(to, from)) return Refused("The data is already in that folder.");
        if (DataFolder.Inside(to, from) || DataFolder.Inside(from, to)) return Refused("One folder would be inside the other.");
        if (Entries(to).Any())
            return DataMarks.Any(mark => Directory.Exists(Path.Combine(to, mark))) ? new(from, to, true, null, 0, 0) : Refused("That folder holds other files. Pick an empty one.");
        var files = Files(from);
        long bytes = files.Sum(rel => new FileInfo(Path.Combine(from, rel)).Length);
        if (FreeAt(to) is { } free && free < bytes) return Refused($"Not enough room there: {ContentLimits.Describe(bytes)} needed, {ContentLimits.Describe(free)} free.");
        return new(from, to, false, null, bytes, files.Count);
    }

    static IEnumerable<string> Entries(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFileSystemEntries(folder).Where(e => !DataFolder.StaysBehind(Path.GetFileName(e))) : [];

    static List<string> Files(string folder) =>
        !Directory.Exists(folder) ? [] : Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(folder, f))
            .Where(rel => !DataFolder.StaysBehind(rel) && !rel.EndsWith(".lock", StringComparison.OrdinalIgnoreCase))
            .OrderBy(rel => rel, StringComparer.Ordinal).ToList();

    static long? FreeAt(string folder)
    {
        try { return Path.GetPathRoot(DataFolder.Whole(folder)) is { Length: > 0 } root ? new DriveInfo(root).AvailableFreeSpace : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    public static string? GameOpenIn(string folder) =>
        !Directory.Exists(folder) ? null : Directory.EnumerateFiles(folder, LockFiles, SearchOption.AllDirectories)
            .Select(held => held[..^Path.GetExtension(held).Length])
            .Where(SaveLock.IsTaken)
            .Select(Path.GetFileNameWithoutExtension)
            .FirstOrDefault();

    public static IReadOnlyList<string> Move(DataMovePlan plan, string defaultFolder, Action<long, long>? progress = null)
    {
        if (plan.Refusal != null || plan.UseAsIs) throw new InvalidOperationException("Only a plan to move into an empty folder is moved.");
        if (GameOpenIn(plan.From) is { } open) throw new DataMoveFailed($"{open} is open in a copy of the app. Leave that table first.");
        var files = Files(plan.From);
        var made = new List<string>();
        try
        {
            long done = 0;
            foreach (var rel in files)
            {
                done += Copy(Path.Combine(plan.From, rel), Path.Combine(plan.To, rel), () => made.Add(rel));
                progress?.Invoke(done, plan.Bytes);
            }
            foreach (var rel in files)
                if (!SameContent(Path.Combine(plan.From, rel), Path.Combine(plan.To, rel))) throw new DataMoveFailed($"{rel} did not arrive whole.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Remove(plan.To, made);
            throw e as DataMoveFailed ?? new DataMoveFailed(e.Message, e);
        }
        DataFolder.Choose(defaultFolder, plan.To);
        return Remove(plan.From, files);
    }

    static long Copy(string from, string to, Action created)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var read = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBuffer);
        using var write = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBuffer);
        created();
        read.CopyTo(write, CopyBuffer);
        write.Flush(flushToDisk: true);
        return read.Length;
    }

    static bool SameContent(string a, string b)
    {
        if (!File.Exists(b) || new FileInfo(a).Length != new FileInfo(b).Length) return false;
        return Hash(a).AsSpan().SequenceEqual(Hash(b));
    }

    static byte[] Hash(string file)
    {
        using var read = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBuffer);
        return SHA256.HashData(read);
    }

    static IReadOnlyList<string> Remove(string folder, List<string> files)
    {
        var left = files.Where(rel => !BestEffort.Try(() => File.Delete(Path.Combine(folder, rel)))).ToList();
        if (!Directory.Exists(folder)) return left;
        foreach (var dir in Directory.EnumerateDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).Append(folder))
            if (!Directory.EnumerateFileSystemEntries(dir).Any()) BestEffort.Try(() => Directory.Delete(dir));
        return left;
    }

    public static string Rebased(string path, string from, string to) =>
        DataFolder.Same(path, from) ? to : DataFolder.Inside(path, from) ? Path.Combine(to, Path.GetRelativePath(from, path)) : path;
}
