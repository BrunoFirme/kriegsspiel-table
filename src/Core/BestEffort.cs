namespace KTable.Core;

public static class BestEffort
{
    public static bool Try(Action step)
    {
        try { step(); return true; }
        catch { return false; }
    }

    public static bool Try<T1, T2, T3>(Action<T1, T2, T3> step, T1 first, T2 second, T3 third)
    {
        try { step(first, second, third); return true; }
        catch { return false; }
    }

    public static T Read<T>(Func<T> read, T fallback)
    {
        try { return read(); }
        catch { return fallback; }
    }

    public static bool DeleteFile(string path) => Try(() => File.Delete(path));

    public static bool DeleteFolder(string path) => Try(() => Directory.Delete(path, recursive: true));

    public static bool DeleteEmptyFolder(string path) => Try(() => Directory.Delete(path, recursive: false));
}
