namespace KTable.Converter;

public static class ModuleListing
{
    public const string WorkshopKind = "workshop", SaveKind = "save", SavedObjects = "Saved Objects";
    static readonly string[] Indexes = { "SaveFileInfos", "WorkshopFileInfos" };

    public sealed record Found(string Kind, string File, string Folder);

    public static IEnumerable<Found> Files(string workshopDir, string savesDir) =>
        In(workshopDir, WorkshopKind, false).Concat(In(savesDir, SaveKind, true));

    static IEnumerable<Found> In(string dir, string kind, bool deep)
    {
        if (!Directory.Exists(dir)) return Array.Empty<Found>();
        var options = new EnumerationOptions { RecurseSubdirectories = deep, IgnoreInaccessible = true };
        return Directory.EnumerateFiles(dir, "*.json", options)
            .Where(f => !Indexes.Any(Path.GetFileName(f).StartsWith))
            .Select(f => new Found(kind, f, FolderOf(dir, f)))
            .Where(f => !IsSavedObject(f.Folder))
            .OrderByDescending(f => File.GetLastWriteTime(f.File))
            .ToList();
    }

    static string FolderOf(string root, string file) =>
        Path.GetRelativePath(root, Path.GetDirectoryName(file)!) is var rel && rel != "." ? rel.Replace('\\', '/') : "";

    static bool IsSavedObject(string folder) =>
        string.Equals(folder.Split('/')[0], SavedObjects, StringComparison.OrdinalIgnoreCase);
}
