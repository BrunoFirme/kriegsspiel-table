using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace KTable.Client;

public sealed record FolderPlace(string Name, string Path);

public static class FolderPlaces
{
    static readonly char[] Quotes = { '"', '\'', '“', '”' };
    static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string? TypedPath(string? text)
    {
        var t = (text ?? "").Trim().Trim(Quotes).Trim();
        if (t.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(t, UriKind.Absolute, out var uri) || !uri.IsFile) return null;
            t = uri.LocalPath;
        }
        if (t.Length == 0 || t.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || !Path.IsPathFullyQualified(t)) return null;
        return t.Length > Path.GetPathRoot(t)!.Length ? t.TrimEnd('/', '\\') : t;
    }

    public static List<FolderPlace> Gather(IEnumerable<FolderPlace> wanted, Func<string, bool> exists)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return wanted.Where(p => !string.IsNullOrWhiteSpace(p.Path) && exists(p.Path) && seen.Add(p.Path.TrimEnd('/', '\\'))).ToList();
    }

    public static List<FolderPlace> OnThisPc() => Gather(Wanted(), Directory.Exists);

    static IEnumerable<FolderPlace> Wanted()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!OperatingSystem.IsWindows()) yield return new FolderPlace("Home", home);
        yield return new FolderPlace("Desktop", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
        yield return new FolderPlace("Documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        yield return new FolderPlace("Downloads", Downloads() ?? Path.Combine(home, "Downloads"));
        yield return new FolderPlace("Pictures", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
        if (!OperatingSystem.IsWindows()) yield break;
        foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable))
            yield return new FolderPlace(DriveName(d), d.RootDirectory.FullName);
    }

    static string DriveName(DriveInfo d)
    {
        var letter = d.Name.TrimEnd('\\', '/');
        try { return d.IsReady && d.VolumeLabel.Length > 0 ? $"{d.VolumeLabel} ({letter})" : letter; }
        catch (IOException) { return letter; }
        catch (UnauthorizedAccessException) { return letter; }
    }

    static string? Downloads()
    {
        if (!OperatingSystem.IsWindows()) return null;
        if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) != 0) return null;
        try { return Marshal.PtrToStringUni(p); }
        finally { Marshal.FreeCoTaskMem(p); }
    }

    [DllImport("shell32.dll")]
    static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
