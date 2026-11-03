using System.Text.RegularExpressions;
using KTable.Core;

namespace KTable.Converter;

public static partial class TtsFolders
{
    const string Game = "Tabletop Simulator";

    [GeneratedRegex("\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\"")] private static partial Regex LibraryPath();

    public static IEnumerable<string> HomeFolders()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(docs, "My Games", Game);
        yield return Path.Combine(home, "Documents", "My Games", Game);
        yield return Path.Combine(home, "OneDrive", "Documents", "My Games", Game);
        yield return Path.Combine(home, "OneDrive", "Documentos", "My Games", Game);
        yield return Path.Combine(home, "Library", Game);
        yield return Path.Combine(home, ".local", "share", Game);
    }

    public static IEnumerable<string> ModsFolders(string? steamRoot) =>
        HomeFolders().Select(h => Path.Combine(h, "Mods"))
            .Concat(SteamLibraries(steamRoot).Select(l => Path.Combine(l, "steamapps", "common", Game, Game + "_Data", "Mods")));

    public static IReadOnlyList<string> SteamLibraries(string? steamRoot)
    {
        if (string.IsNullOrWhiteSpace(steamRoot)) return Array.Empty<string>();
        var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        var text = BestEffort.Read(() => File.Exists(vdf) ? File.ReadAllText(vdf) : "", fallback: "");
        return LibraryPath().Matches(text).Select(m => m.Groups[1].Value.Replace("\\\\", "\\")).Prepend(steamRoot)
            .Select(p => p.TrimEnd('\\', '/')).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? SteamRoot()
    {
        if (OperatingSystem.IsWindows() && SteamPathInRegistry() is { Length: > 0 } fromRegistry)
            return fromRegistry.Replace('/', Path.DirectorySeparatorChar);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { Path.Combine(programs, "Steam"), Path.Combine(home, ".steam", "steam"), Path.Combine(home, "Library", "Application Support", "Steam") }
            .FirstOrDefault(Directory.Exists);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    static string? SteamPathInRegistry()
    {
        try { return Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string; }
        catch (Exception e) when (e is System.Security.SecurityException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static string? MostModules(IEnumerable<string> modsFolders) => modsFolders
        .Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
        .Select(m => (m, n: BestEffort.Read(() => Directory.EnumerateFiles(Path.Combine(m, "Workshop"), "*.json").Count(), fallback: 0)))
        .OrderByDescending(x => x.n).Select(x => x.m).FirstOrDefault();
}
