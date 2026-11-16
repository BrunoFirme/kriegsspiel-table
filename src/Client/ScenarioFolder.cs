using System;
using System.IO;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class ScenarioFolder
{
    public const string ScenarioFile = "scenario.json", StartFile = "scenario.start.json", PictureFile = "picture.png", OwnPrefix = "local.";
    public const int PictureWidth = 2560;

    public static string Dir(string packagesRoot, string id) => Path.Combine(packagesRoot, "scenarios", id);

    public static string? Picture(string folder) => Path.Combine(folder, PictureFile) is var p && File.Exists(p) ? p : null;

    static string Named(string name) => name.Trim() is { Length: > 0 } tidy ? tidy : throw new ArgumentException("A scenario needs a name.");

    public static string Rename(string folder, string name)
    {
        var tidy = Named(name);
        foreach (var file in new[] { Path.Combine(folder, StartFile), Path.Combine(folder, ScenarioFile) })
            if (File.Exists(file) && Json.Load<Scenario>(file) is { } sc && sc.Name != tidy)
            {
                sc.Name = tidy;
                Json.SaveBig(file, sc);
            }
        return tidy;
    }

    public static string Duplicate(string folder, string name)
    {
        var tidy = Named(name);
        var scenarios = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder)) ?? throw new ArgumentException("not a scenario folder");
        var from = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var source = Json.Load<Scenario>(Path.Combine(folder, ScenarioFile));
        var dir = Naming.ScenarioDir(tidy);
        for (int i = 2; Directory.Exists(Path.Combine(scenarios, dir)); i++) dir = $"{Naming.ScenarioDir(tidy)}-{i}";
        var target = Path.Combine(scenarios, dir);
        bool made = false;
        try
        {
            CopyAllBut(folder, target, Path.Combine(folder, ScenarioFile));
            var start = Path.Combine(target, StartFile);
            if (File.Exists(start)) Json.SaveBig(start, Retargeted(Json.Load<Scenario>(start), from, dir, tidy));
            Json.SaveBig(Path.Combine(target, ScenarioFile), Retargeted(source, from, dir, tidy));
            made = true;
        }
        finally
        {
            if (!made) BestEffort.Try(() => Directory.Delete(target, true));
        }
        return dir;
    }

    static Scenario Retargeted(Scenario sc, string from, string to, string name)
    {
        sc.Id = OwnPrefix + to;
        sc.Name = name;
        foreach (var sheet in sc.Sheets)
            if (sheet.Package == "scenarios/" + from) sheet.Package = "scenarios/" + to;
        return sc;
    }

    const string Tiles = "tiles";

    static bool Tiled(string rel) => rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0] == Tiles;

    static void CopyAllBut(string from, string to, string left)
    {
        Directory.CreateDirectory(to);
        foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            if (string.Equals(file, left, StringComparison.OrdinalIgnoreCase)) continue;
            var rel = Path.GetRelativePath(from, file);
            var copy = Path.Combine(to, rel);
            if (!(Tiled(rel) && FileLinks.Link(file, copy))) File.Copy(file, copy);
        }
    }

    public static void SetPicture(string folder, byte[] png) => Json.WriteAtomic(Path.Combine(folder, PictureFile), png);

    public static void DropPicture(string folder)
    {
        var p = Path.Combine(folder, PictureFile);
        if (File.Exists(p)) File.Delete(p);
    }
}
