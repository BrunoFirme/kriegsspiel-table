namespace KTable.Core;

public static class DataFolder
{
    public const string PointerName = "data-folder.txt", AgentLive = "agent.json", AgentTools = "agent-tools.json";

    public static bool StaysBehind(string name) => name is PointerName or AgentLive or AgentTools;

    sealed record Kept(string Default, string Chosen);

    static Kept? _kept;

    static StringComparison PathCase => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string Of(string defaultFolder)
    {
        var kept = _kept;
        if (kept != null && kept.Default == defaultFolder) return kept.Chosen;
        var chosen = Chosen(defaultFolder) ?? defaultFolder;
        _kept = new Kept(defaultFolder, chosen);
        return chosen;
    }

    public static string PointerPath(string defaultFolder) => Path.Combine(defaultFolder, PointerName);

    public static string? Chosen(string defaultFolder)
    {
        var file = PointerPath(defaultFolder);
        if (!File.Exists(file)) return null;
        var named = File.ReadAllText(file).Trim();
        return named.Length > 0 && Path.IsPathFullyQualified(named) && !Same(named, defaultFolder) ? Path.GetFullPath(named) : null;
    }

    public static string? Missing(string defaultFolder) => Chosen(defaultFolder) is { } chosen && !Directory.Exists(chosen) ? chosen : null;

    public static void Choose(string defaultFolder, string? folder)
    {
        var file = PointerPath(defaultFolder);
        if (folder == null || Same(folder, defaultFolder))
        {
            if (File.Exists(file)) File.Delete(file);
        }
        else
        {
            Directory.CreateDirectory(defaultFolder);
            Json.WriteAtomic(file, Path.GetFullPath(folder));
        }
        _kept = null;
    }

    public static string Whole(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool Same(string a, string b) => string.Equals(Whole(a), Whole(b), PathCase);

    public static bool Inside(string path, string folder) => Whole(path).StartsWith(Whole(folder) + Path.DirectorySeparatorChar, PathCase);
}
