using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace KTable.App;

sealed class ModelTextures : IDisposable
{
    const int MostDepth = 3, MostFiles = 4000;
    const long MostModelBytes = 300L * 1024 * 1024;
    static readonly Regex Named = new(@"[^\x00-\x1f""<>|*?]{1,260}\.(?:png|jpe?g|tga|bmp|webp|tiff?|dds|bin)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    readonly string? _temp;

    public string Model { get; }

    ModelTextures(string model, string? temp) => (Model, _temp) = (model, temp);

    public static ModelTextures For(string model)
    {
        var dir = Path.GetDirectoryName(model) ?? "";
        var refs = References(model);
        var missing = refs.Where(r => !Resolves(dir, r)).ToList();
        if (missing.Count == 0) return new(model, null);
        var near = Near(dir);
        var found = missing.Select(r => (Ref: r, File: near.GetValueOrDefault(Leaf(r)))).Where(f => f.File != null).ToList();
        if (found.Count == 0) return new(model, null);
        var temp = Path.Combine(Path.GetTempPath(), "ktable-model-" + Guid.NewGuid().ToString("N"));
        var home = temp;
        for (int i = 0, up = refs.Where(r => !Rooted(r)).Select(Ups).DefaultIfEmpty(0).Max(); i <= up; i++) home = Path.Combine(home, "m");
        Directory.CreateDirectory(home);
        var staged = Path.Combine(home, Path.GetFileName(model));
        File.Copy(model, staged);
        foreach (var r in refs.Where(r => !Rooted(r) && Resolves(dir, r))) Place(temp, Path.Combine(home, Norm(r)), Path.Combine(dir, Norm(r)));
        foreach (var (r, file) in found)
        {
            if (!Rooted(r)) Place(temp, Path.Combine(home, Norm(r)), file!);
            Place(temp, Path.Combine(home, Leaf(r)), file!);
        }
        return new(staged, temp);
    }

    public void Dispose()
    {
        if (_temp == null) return;
        try { Directory.Delete(_temp, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    internal static List<string> References(string model)
    {
        var info = new FileInfo(model);
        if (!info.Exists || info.Length > MostModelBytes) return [];
        var text = Encoding.Latin1.GetString(File.ReadAllBytes(model));
        return Named.Matches(text).Select(m => m.Value.Trim()).Where(v => Leaf(v).Length > 4).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    static Dictionary<string, string> Near(string dir)
    {
        var found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int seen = 0;
        void Look(string d, int depth)
        {
            if (depth > MostDepth || seen > MostFiles || !Directory.Exists(d)) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(d))
                {
                    if (++seen > MostFiles) return;
                    found.TryAdd(Path.GetFileName(f), f);
                }
                foreach (var sub in Directory.EnumerateDirectories(d)) Look(sub, depth + 1);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Look(dir, 0);
        if (Path.GetDirectoryName(dir) is { Length: > 0 } parent) Look(parent, 1);
        return found;
    }

    static void Place(string temp, string target, string source)
    {
        var full = Path.GetFullPath(target);
        if (!full.StartsWith(Path.GetFullPath(temp) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || File.Exists(full) || !File.Exists(source)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Copy(source, full);
    }

    static bool Resolves(string dir, string r) => Rooted(r) ? File.Exists(r) : File.Exists(Path.Combine(dir, Norm(r)));

    static string Norm(string r) => r.Replace('\\', '/');

    static string Leaf(string r) => Norm(r).Split('/').Last();

    static bool Rooted(string r) => r.Length > 1 && r[1] == ':' || r.StartsWith('/') || r.StartsWith('\\');

    static int Ups(string r) => Norm(r).Split('/').Count(s => s == "..");
}
