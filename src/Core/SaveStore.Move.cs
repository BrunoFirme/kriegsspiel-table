namespace KTable.Core;

public sealed partial class SaveStore
{
    public static List<string> MoveTree(string from, string to)
    {
        var lines = new List<string>();
        if (Directory.Exists(from)) MoveFolder(from, to, lines);
        return lines;
    }

    static void MoveFolder(string from, string to, List<string> lines)
    {
        try
        {
            if (!Directory.Exists(to) && !File.Exists(to) && SameVolume(from, to))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(to))!);
                try
                {
                    Directory.Move(from, to);
                    lines.Add($"{from} -> {to}");
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { BestEffort.Note(e); }
            }
            Directory.CreateDirectory(to);
            foreach (var f in Directory.GetFiles(from)) MoveFile(f, Path.Combine(to, Path.GetFileName(f)), lines);
            foreach (var d in Directory.GetDirectories(from))
            {
                MoveFolder(d, Path.Combine(to, Path.GetFileName(d)), lines);
            }
            RemoveIfEmpty(from);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { lines.Add($"{from}: not moved ({e.Message}); tried again at the next start"); }
    }

    static void MoveFile(string from, string to, List<string> lines)
    {
        try
        {
            if (File.Exists(to) || Directory.Exists(to))
            {
                if (File.Exists(to) && SameBytes(from, to))
                {
                    File.Delete(from);
                    lines.Add($"{from}: the same file is at {to} already");
                    return;
                }
                to = FreeFile(to);
            }
            if (SameVolume(from, to)) File.Move(from, to, overwrite: false);
            else
            {
                Json.CopyAtomic(from, to, overwrite: false);
                File.Delete(from);
            }
            lines.Add($"{from} -> {to}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { lines.Add($"{from}: not moved ({e.Message}); tried again at the next start"); }
    }

    static string FreeFile(string path)
    {
        var dir = Path.GetDirectoryName(path)!;
        var (stem, ext) = (Path.GetFileNameWithoutExtension(path), Path.GetExtension(path));
        for (int n = 2; ; n++)
        {
            var free = Path.Combine(dir, $"{stem} ({n}){ext}");
            if (!File.Exists(free) && !Directory.Exists(free)) return free;
        }
    }

    static bool SameBytes(string a, string b)
    {
        using var x = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var y = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (x.Length != y.Length) return false;
        var (bx, by) = (new byte[1 << 16], new byte[1 << 16]);
        while (true)
        {
            int n = x.ReadAtLeast(bx, bx.Length, throwOnEndOfStream: false);
            if (n == 0) return true;
            if (y.ReadAtLeast(by, n, throwOnEndOfStream: false) != n || !bx.AsSpan(0, n).SequenceEqual(by.AsSpan(0, n))) return false;
        }
    }

    static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)), StringComparison.OrdinalIgnoreCase);

    static void RemoveIfEmpty(string folder)
    {
        try { if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { BestEffort.Note(e); }
    }
}
