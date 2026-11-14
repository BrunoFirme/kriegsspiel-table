using System;
using System.IO;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client;

public static class ScenarioFolder
{
    public const string ScenarioFile = "scenario.json", PictureFile = "picture.png";
    public const int PictureWidth = 1400;

    public static string Dir(string packagesRoot, string id) => Path.Combine(packagesRoot, "scenarios", id);

    public static string? Picture(string folder) => Path.Combine(folder, PictureFile) is var p && File.Exists(p) ? p : null;

    public static string Rename(string folder, string name)
    {
        var tidy = name.Trim();
        if (tidy.Length == 0) throw new ArgumentException("A scenario needs a name.");
        var file = Path.Combine(folder, ScenarioFile);
        var sc = Json.Load<Scenario>(file);
        if (sc.Name == tidy) return tidy;
        sc.Name = tidy;
        Json.SaveBig(file, sc);
        return tidy;
    }

    public static void SetPicture(string folder, byte[] png) => Json.WriteAtomic(Path.Combine(folder, PictureFile), png);

    public static void DropPicture(string folder)
    {
        var p = Path.Combine(folder, PictureFile);
        if (File.Exists(p)) File.Delete(p);
    }
}
