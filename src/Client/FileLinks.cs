using System.Runtime.InteropServices;
using KTable.Core;

namespace KTable.Client;

public static class FileLinks
{
    const int LongFrom = 248;
    const string Half = ".part";

    internal static readonly AsyncLocal<Func<string, string, bool>?> Maker = new();

    public static bool Link(string existing, string made)
    {
        if (!File.Exists(existing) || File.Exists(made)) return false;
        return BestEffort.Read(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(made))!);
            return (Maker.Value ?? Native)(Path.GetFullPath(existing), Path.GetFullPath(made)) && File.Exists(made);
        }, fallback: false);
    }

    public static bool Replace(string existing, string doubled)
    {
        var half = doubled + Half;
        BestEffort.DeleteFile(half);
        if (!Link(existing, half)) return false;
        try
        {
            File.Move(half, doubled, overwrite: true);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        finally { BestEffort.DeleteFile(half); }
    }

    static bool Native(string existing, string made) =>
        OperatingSystem.IsWindows() ? CreateHardLinkW(Long(made), Long(existing), IntPtr.Zero) : link(existing, made) == 0;

    static string Long(string path) => path.Length >= LongFrom && !path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\" + path : path;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateHardLinkW(string made, string existing, IntPtr security);

    [DllImport("libc", SetLastError = true)]
    static extern int link([MarshalAs(UnmanagedType.LPUTF8Str)] string existing, [MarshalAs(UnmanagedType.LPUTF8Str)] string made);
}
