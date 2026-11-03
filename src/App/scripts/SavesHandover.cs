using System;
using System.IO;
using System.Linq;
using KTable.Core;
using KTable.Core.Model;
using KTable.Server;

namespace KTable.App;

public enum SavesMode
{
    None,
    InPlay,
    All,
}

public static class SavesHandover
{
    public const string SavesFolder = "saves", RunsFolder = "runs", RunMarkInPack = "ktable-run.json";

    public static string? Stage(PackageStore store, SaveStore saves, string scenarioDir, SavesMode mode, string dst)
    {
        if (mode == SavesMode.None || saves.CurrentRun(scenarioDir) is not { } current) return null;
        var root = saves.SavesRoot(scenarioDir);
        WriteWhole(store, saves, saves.LivePath(scenarioDir, current), Path.Combine(dst, WorkshopContent.GameFile));
        if (mode == SavesMode.InPlay)
        {
            if (saves.RunFolderOf(scenarioDir, current) is { } folder) CopyTree(folder, Path.Combine(dst, SavesFolder, Path.GetRelativePath(root, folder)));
            return current;
        }
        foreach (var run in saves.Runs(scenarioDir).Where(r => r.Id != current))
        {
            var live = saves.LivePath(scenarioDir, run.Id);
            if (File.Exists(live) || saves.Checkpoints(live).Count > 0) WriteWhole(store, saves, live, Path.Combine(dst, RunsFolder, run.Id + SaveStore.Ext));
        }
        if (Directory.Exists(root)) CopyTree(root, Path.Combine(dst, SavesFolder));
        return current;
    }

    static void WriteWhole(PackageStore store, SaveStore saves, string live, string to)
    {
        var game = SaveLoader.Open(saves, live, store, out _);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Json.SaveBig(to, game.ToSave(copy: false));
    }

    static void CopyTree(string from, string to)
    {
        if (!Directory.Exists(from)) return;
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            if (SaveStore.IsHidden(from, Path.GetDirectoryName(f)!)) continue;
            var name = Path.GetFileName(f);
            var rel = Path.GetRelativePath(from, Path.GetDirectoryName(f)!);
            string target;
            if (name == SaveStore.RunMarkFile) target = Path.Combine(to, rel, RunMarkInPack);
            else if (name.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase)) target = Path.Combine(to, rel, name);
            else continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(f, target, true);
        }
    }

    public static string? Install(SaveStore saves, string scenarioDir, string folder, string? gameRun, DateTime nowUtc)
    {
        var game = Path.Combine(folder, WorkshopContent.GameFile);
        var savesIn = Path.Combine(folder, SavesFolder);
        var runsIn = Path.Combine(folder, RunsFolder);
        if (!Directory.Exists(savesIn) && !Directory.Exists(runsIn))
            return File.Exists(game) ? saves.AddGame(scenarioDir, game, "Handed over", nowUtc) : null;
        var staging = Path.Combine(Path.GetTempPath(), "ktable-saves-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var liveTo = Path.Combine(staging, "live");
            var savesTo = Path.Combine(staging, SavesFolder);
            Directory.CreateDirectory(liveTo);
            if (Directory.Exists(savesIn))
                foreach (var f in Directory.EnumerateFiles(savesIn, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(savesIn, f);
                    var to = Path.GetFullPath(Path.Combine(savesTo, Path.GetFileName(f) == RunMarkInPack ? Path.Combine(Path.GetDirectoryName(rel)!, SaveStore.RunMarkFile) : rel));
                    if (!to.StartsWith(Path.GetFullPath(savesTo) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                    File.Copy(f, to, true);
                }
            if (Directory.Exists(runsIn))
                foreach (var f in Directory.GetFiles(runsIn, "*" + SaveStore.Ext)) File.Copy(f, Path.Combine(liveTo, Path.GetFileName(f)), true);
            if (File.Exists(game) && gameRun is { Length: > 0 } && !gameRun.Contains('.') && gameRun.All(c => char.IsLetterOrDigit(c) || c == '-'))
            {
                File.Copy(game, Path.Combine(liveTo, gameRun + SaveStore.Ext), true);
                File.WriteAllText(Path.Combine(liveTo, SaveStore.CurrentFile), gameRun);
            }
            saves.AddSaves(scenarioDir, savesTo, liveTo, nowUtc);
            return saves.CurrentRun(scenarioDir);
        }
        finally { BestEffort.DeleteFolder(staging); }
    }
}
