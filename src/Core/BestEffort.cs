namespace KTable.Core;

public static class BestEffort
{
    public static Action<Exception>? Noted { get; set; }

    public static void Note(Exception e)
    {
        if (e is FileNotFoundException or DirectoryNotFoundException) return;
        Noted?.Invoke(e);
    }

    public static bool Try(Action step)
    {
        try { step(); return true; }
        catch (Exception e) { Note(e); return false; }
    }

    public static bool Try<T1, T2, T3>(Action<T1, T2, T3> step, T1 first, T2 second, T3 third)
    {
        try { step(first, second, third); return true; }
        catch (Exception e) { Note(e); return false; }
    }

    public static T Read<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch (Exception e) { Note(e); return fallback; }
    }

    public static bool DeleteFile(string path) => Try(() => File.Delete(path));

    public static bool DeleteFolder(string path) => !Directory.Exists(path) || Try(() => Directory.Delete(path, recursive: true));

    public static bool DeleteEmptyFolder(string path)
    {
        if (!Directory.Exists(path)) return true;
        if (Read(() => Directory.EnumerateFileSystemEntries(path).Any(), true)) return false;
        return Try(() => Directory.Delete(path, recursive: false));
    }
}
