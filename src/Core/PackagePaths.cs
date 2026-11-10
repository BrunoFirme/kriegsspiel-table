namespace KTable.Core;

public static class PackagePaths
{
    const int MostNameChars = 200;
    const int MostPathChars = 400;
    static readonly char[] NotInNames = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];
    public static readonly IReadOnlySet<string> HostFileTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { ".json", ".bin", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".bmp", ".obj" };

    public static bool IsHostFileType(string? rel) => rel != null && HostFileTypes.Contains(Path.GetExtension(rel));

    public static bool IsName(string? name) =>
        name is { Length: > 0 and <= MostNameChars } && name.Trim('.', ' ').Length > 0 && !name.Contains("..", StringComparison.Ordinal)
        && name.IndexOfAny(NotInNames) < 0 && !name.Any(char.IsControl);

    public const string ScenarioPackagePrefix = "scenarios/";

    public static bool IsPackage(string? package) =>
        IsName(package) || package != null && package.StartsWith(ScenarioPackagePrefix, StringComparison.Ordinal) && IsName(package[ScenarioPackagePrefix.Length..]);

    public static bool IsRelative(string? rel) =>
        rel is { Length: > 0 and <= MostPathChars } && !Path.IsPathRooted(rel) && rel.IndexOf(':') < 0 && !rel.Any(char.IsControl)
        && rel.Split('/', '\\').All(part => part.Trim('.', ' ').Length > 0);

    public static string? Inside(string root, string? package, string? rel)
    {
        if (!IsPackage(package) || !IsRelative(rel)) return null;
        var top = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var dir = Path.GetFullPath(Path.Combine(top, package!)) + Path.DirectorySeparatorChar;
        var abs = Path.GetFullPath(Path.Combine(dir, rel!));
        return dir.StartsWith(top, StringComparison.OrdinalIgnoreCase) && abs.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ? abs : null;
    }
}
