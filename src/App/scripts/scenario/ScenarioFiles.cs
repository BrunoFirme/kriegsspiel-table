using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

public static class ScenarioFiles
{
    public static string Slug(string name) => Naming.ScenarioDir(name);

    static string FreeDir(PackageStore store, string name)
    {
        var baseSlug = Slug(name);
        var dir = baseSlug;
        for (int i = 2; Directory.Exists(Path.Combine(store.Root, "scenarios", dir)); i++) dir = $"{baseSlug}-{i}";
        return dir;
    }

    public static string CreateBlank(PackageStore store, string name, string gameTime, int minutesPerTurn, Lighting? look = null)
    {
        var dir = FreeDir(store, name);
        var sc = new Scenario
        {
            Id = "local." + dir, Name = name.Trim().Length > 0 ? name.Trim() : "New scenario",
            Clock = new GameClock { Turn = 0, GameTime = gameTime, IncrementMinutes = Math.Max(1, minutesPerTurn) },
            Lighting = look ?? new Lighting(),
        };
        sc.Drawings["umpire"] = new();
        Json.Save(Path.Combine(store.Root, "scenarios", dir, "scenario.json"), sc);
        return dir;
    }

    public const string StartFile = "scenario.start.json";

    public static bool HasId(string path, string id)
    {
        try { return Json.Load<Scenario>(path).Id == id; }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return false; }
    }

    public static Scenario? TryLoad(string path)
    {
        try { return Json.Load<Scenario>(path) is { Name: not null, Packs: not null, Sheets: not null, UmpireBoard: not null } s ? s : null; }
        catch (Exception e) when (SaveStore.IsFileTrouble(e)) { return null; }
    }

    public static string? StartPath(PackageStore store, string dir)
    {
        var p = Path.Combine(store.Root, "scenarios", dir, StartFile);
        return File.Exists(p) ? p : null;
    }

    public static bool SheetsPending(PackageStore store, GameState g, string dir)
    {
        var pkg = "scenarios/" + dir;
        var target = Path.Combine(store.Root, "scenarios", dir);
        return g.Sheets.Any(s => s.Package != pkg && (s.HasPicture && !File.Exists(Path.Combine(target, "tiles", s.Image, "index.json"))
            || s.Relief is { Length: > 0 } r && !File.Exists(Path.Combine(target, ReliefFiles.HeaderRel(r)))));
    }

    public static string SaveTable(PackageStore store, GameState g, string name, string? dir, System.Threading.CancellationToken stop = default)
    {
        if (dir == null)
        {
            dir = FreeDir(store, name);
            Json.SaveBig(Path.Combine(store.Root, "scenarios", dir, "scenario.json"), AsFolderScenario(store, dir, g, "local." + dir, name));
        }
        var (where, file, sc) = PrepareTable(store, g, name, dir, stop);
        Json.SaveBig(file, sc);
        return where;
    }

    public static (string dir, string file, Scenario scenario) PrepareTable(PackageStore store, GameState g, string name, string? dir, System.Threading.CancellationToken stop = default)
    {
        dir ??= FreeDir(store, name);
        var pkg = "scenarios/" + dir;
        var target = Path.Combine(store.Root, "scenarios", dir);
        var existing = Path.Combine(target, "scenario.json");
        var id = File.Exists(existing) ? Json.PeekString(existing, "id") ?? "local." + dir : "local." + dir;
        var startFile = Path.Combine(target, StartFile);
        if (!id.StartsWith("local.") && File.Exists(existing) && !File.Exists(startFile))
            Json.CopyAtomic(existing, startFile, overwrite: false);
        var sc = g.ToScenario(id, name);
        foreach (var sheet in sc.Sheets)
        {
            if (sheet.Package == pkg) continue;
            if (!CopyRelief(store, sheet, target)) continue;
            if (!sheet.HasPicture) { sheet.Package = pkg; continue; }
            var fromTiles = store.HasTiles(sheet) || store.FindTilesPackage(sheet.Image) is not { } found
                ? store.TilesDir(sheet) : Path.Combine(store.PackageDir(found), "tiles", sheet.Image);
            var toTiles = Path.Combine(target, "tiles", sheet.Image);
            if (!File.Exists(Path.Combine(toTiles, "index.json"))) CopyDir(fromTiles, toTiles, stop);
            if (!File.Exists(Path.Combine(toTiles, "index.json"))) continue;
            var fromAssets = Path.Combine(store.PackageDir(sheet.Package), "assets");
            if (Directory.Exists(fromAssets))
                foreach (var f in Directory.GetFiles(fromAssets, sheet.Image + ".*"))
                {
                    var dst = Path.Combine(target, "assets", Path.GetFileName(f));
                    if (!File.Exists(dst)) Json.CopyAtomic(f, dst);
                }
            sheet.Package = pkg;
        }
        sc.Packs = sc.Packs.Where(p => p != SheetImporter.Package).ToList();
        return (dir, Path.Combine(target, "scenario.json"), sc);
    }

    static bool CopyRelief(PackageStore store, SheetDef sheet, string target)
    {
        if (sheet.Relief is not { Length: > 0 } sha) return true;
        string? From(string rel) =>
            new[] { sheet.Package, KTable.Core.Protocol.UploadAsset.ReliefPackage }.Select(p => Path.Combine(store.PackageDir(p), rel)).FirstOrDefault(File.Exists)
            ?? (store.FindPackageWith(rel) is { } found ? Path.Combine(store.PackageDir(found), rel) : null);
        var header = ReliefFiles.HeaderRel(sha);
        if (From(header) is not { } headerPath) return false;
        ReliefIndex index;
        try { index = ReliefFiles.LoadIndex(headerPath); }
        catch (Exception e) when (e is IOException or InvalidDataException or System.Text.Json.JsonException) { Godot.GD.PrintErr($"[relief] {sheet.Name}: {e.Message}"); return false; }
        foreach (var rel in ReliefFiles.Files(sha, index))
        {
            var dst = Path.Combine(target, rel);
            if (File.Exists(dst)) continue;
            if (From(rel) is not { } src) return false;
            Json.CopyAtomic(src, dst);
        }
        return true;
    }

    public static void KeepSetup(PackageStore store, string dir, TurnRecord first, string? id = null, string? name = null)
    {
        var file = Path.Combine(store.Root, "scenarios", dir, "scenario.json");
        var g = first.Unpack();
        var sc = AsFolderScenario(store, dir, g, id ?? Json.PeekString(file, "id") ?? "local." + dir, name ?? Json.PeekString(file, "name") ?? g.Name);
        Json.SaveBig(Path.Combine(store.Root, "scenarios", dir, StartFile), sc);
    }

    static Scenario AsFolderScenario(PackageStore store, string dir, GameState g, string id, string name)
    {
        var target = Path.Combine(store.Root, "scenarios", dir);
        var pkg = "scenarios/" + dir;
        var sc = g.ToScenario(id, name);
        foreach (var sheet in sc.Sheets)
            if (sheet.Package != pkg && (sheet.HasPicture ? File.Exists(Path.Combine(target, "tiles", sheet.Image, "index.json")) : sheet.Relief is { Length: > 0 } r && File.Exists(Path.Combine(target,
                ReliefFiles.HeaderRel(r))))) sheet.Package =
                pkg;
        sc.Packs = sc.Packs.Where(p => p != SheetImporter.Package).ToList();
        return sc;
    }

    public static KTable.Server.GameServer? GameInPlay(SaveStore saves, string dir)
    {
        var live = saves.LivePath(dir);
        var files = new List<string>();
        if (File.Exists(live)) files.Add(live);
        files.AddRange(saves.Checkpoints(live).Select(c => c.Path));
        foreach (var f in files)
        {
            try { return KTable.Server.SaveLoader.Load(f); }
            catch (SaveTooNewException) { return null; }
            catch (Exception e) when (SaveStore.IsFileTrouble(e)) { continue; }
        }
        return null;
    }

    public static TurnRecord? FirstTurn(KTable.Server.GameServer game) =>
        game.Turns.Count > 0 && game.Turns[0].HasTable ? game.Turns[0] : null;

    public static void WriteFromGame(PackageStore store, string dir, KTable.Server.GameServer game)
    {
        var g = game.State;
        var id = !string.IsNullOrWhiteSpace(g.ScenarioId) ? g.ScenarioId : "local." + dir;
        Json.SaveBig(Path.Combine(store.Root, "scenarios", dir, "scenario.json"), AsFolderScenario(store, dir, g, id, string.IsNullOrWhiteSpace(g.Name) ? dir : g.Name));
    }

    public static (List<string> rebuilt, List<string> failed) RebuildMissing(PackageStore store, SaveStore saves)
    {
        var rebuilt = new List<string>();
        var failed = new List<string>();
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Directory.Exists(saves.LiveDir))
                foreach (var f in Directory.GetFiles(saves.LiveDir, "*" + SaveStore.Ext))
                    if (f.EndsWith(SaveStore.Ext, StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(f).Contains(".unreadable-")) dirs.Add(Path.GetFileNameWithoutExtension(f));
            if (Directory.Exists(saves.LiveDir))
                foreach (var d in Directory.GetDirectories(saves.LiveDir))
                    if (!string.Equals(Path.GetFileName(d), "mirrors", StringComparison.OrdinalIgnoreCase)) dirs.Add(Path.GetFileName(d));
            if (Directory.Exists(saves.HistoryDir))
                foreach (var d in Directory.GetDirectories(saves.HistoryDir))
                    if (!string.Equals(Path.GetFileName(d), "mirrors", StringComparison.OrdinalIgnoreCase)) dirs.Add(Path.GetFileName(d));
        }
        catch (Exception e) { Godot.GD.PushWarning("could not look for games without a scenario: " + e.Message); }
        foreach (var dir in dirs)
        {
            if (File.Exists(Path.Combine(store.Root, "scenarios", dir, "scenario.json")) || !saves.HasGame(dir)) continue;
            if (InUse.BeingReplaced(InUse.Scenario(store.Root, dir)) != null) continue;
            try
            {
                if (GameInPlay(saves, dir) is not { } game) { failed.Add(dir); continue; }
                WriteFromGame(store, dir, game);
                rebuilt.Add(string.IsNullOrWhiteSpace(game.State.Name) ? dir : game.State.Name);
            }
            catch (Exception e) { failed.Add(dir); Godot.GD.PushWarning($"could not give the game {dir} its scenario file back: {e.Message}"); }
        }
        return (rebuilt, failed);
    }

    static void CopyDir(string from, string to, System.Threading.CancellationToken stop = default)
    {
        if (!Directory.Exists(from)) return;
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories).OrderBy(f => Path.GetFileName(f) == "index.json" ? 1 : 0))
        {
            stop.ThrowIfCancellationRequested();
            var dst = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (File.Exists(dst)) continue;
            File.Copy(f, dst + ".part", true);
            File.Move(dst + ".part", dst);
        }
    }
}
