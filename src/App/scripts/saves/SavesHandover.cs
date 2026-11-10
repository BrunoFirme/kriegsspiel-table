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
        if (mode == SavesMode.None || saves.CurrentSave(scenarioDir) is not { } current) return null;
        var root = saves.SavesRoot(scenarioDir);
        WriteWhole(store, saves, current, Path.Combine(dst, WorkshopContent.GameFile));
        if (mode == SavesMode.All)
            foreach (var save in saves.SaveFiles(scenarioDir).Where(f => !SaveStore.SamePath(f, current)))
                WriteWhole(store, saves, save, Path.Combine(dst, SavesFolder, Path.GetRelativePath(root, save)));
        return Path.GetRelativePath(root, current);
    }

    static void WriteWhole(PackageStore store, SaveStore saves, string save, string to)
    {
        var game = SaveLoader.Open(saves, save, store, out _);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Json.SaveBig(to, game.ToSave(copy: false));
    }

    public static string? Install(SaveStore saves, string scenarioDir, string folder, string? gameAt, DateTime nowUtc)
    {
        var game = Path.Combine(folder, WorkshopContent.GameFile);
        var savesIn = Path.Combine(folder, SavesFolder);
        var runsIn = Path.Combine(folder, RunsFolder);
        bool old = Directory.Exists(runsIn) || (Directory.Exists(savesIn) && Directory.EnumerateFiles(savesIn, RunMarkInPack, SearchOption.AllDirectories).Any())
            || (gameAt is { Length: > 0 } && !gameAt.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase));
        if (old) return InstallOld(saves, scenarioDir, game, savesIn, runsIn, gameAt, nowUtc);
        saves.AddSaves(scenarioDir, savesIn);
        return File.Exists(game) ? saves.AddGame(scenarioDir, game, "Handed over", gameAt) : saves.CurrentSave(scenarioDir);
    }

    static string? InstallOld(SaveStore saves, string scenarioDir, string game, string savesIn, string runsIn, string? gameRun, DateTime nowUtc)
    {
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
            else if (File.Exists(game) && !Directory.Exists(savesIn) && !Directory.Exists(runsIn)) return saves.AddGame(scenarioDir, game, "Handed over");
            saves.AddLegacySaves(scenarioDir, savesTo, liveTo, nowUtc);
            return saves.CurrentSave(scenarioDir);
        }
        finally { BestEffort.DeleteFolder(staging); }
    }
}
