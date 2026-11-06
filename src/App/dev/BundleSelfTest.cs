using System.IO;
using System.Linq;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using Json = KTable.Core.Json;

namespace KTable.App;

public static class BundleSelfTest
{
    public static void Run()
    {
        var store = new PackageStore(PackageStore.DefaultRoot);
        var tmp = Path.Combine(Path.GetTempPath(), "ktable-bundle-test");
        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        const string sc = "miranda-de-azan";
        foreach (var mode in new[] { WorkshopContent.PackMode.Used, WorkshopContent.PackMode.Whole, WorkshopContent.PackMode.None })
        {
            var file = Path.Combine(tmp, $"{sc}-{mode}.ktpack");
            var bytes = WorkshopContent.ExportFile(store, "scenario", sc, mode, file);
            var clean = new PackageStore(Path.Combine(tmp, "store-" + mode));
            var meta = WorkshopContent.ImportFile(clean, file);
            var scenario = Json.Load<Scenario>(Path.Combine(clean.Root, "scenarios", sc, "scenario.json"));
            clean.LoadPacks(scenario.Packs);
            var defs = scenario.UmpireBoard.Select(p => p.Def).Distinct().ToList();
            int resolved = defs.Count(d => clean.Def(d) != null);
            int imagesOk = defs.Select(clean.Def).Where(d => d?.Image != null).Count(d => clean.AssetPath(clean.PackOfDef(d!.Id)!, d.Image) is { } p && File.Exists(p));
            GD.Print($"[bundle] {mode,-5} {bytes / (1 << 20),5} MB  packs carried {meta.InstalledPacks.Count}/{scenario.Packs.Count}, missing {WorkshopContent.MissingPacks(clean, scenario).Count}, " +
                     $"pieces resolve {resolved}/{defs.Count}, images on disk {imagesOk}, partial packs: {string.Join(",", scenario.Packs.Where(p => clean.Pack(p)?.Partial == true))}");
        }

        var guard = new PackageStore(Path.Combine(tmp, "store-guard"));
        var full = "iks.scripted-war-game-pieces";
        Directory.CreateDirectory(guard.PackageDir(full));
        File.Copy(Path.Combine(store.PackageDir(full), "pack.json"), Path.Combine(guard.PackageDir(full), "pack.json"));
        WorkshopContent.ImportFile(guard, Path.Combine(tmp, $"{sc}-Used.ktpack"));
        guard.Forget(full);
        GD.Print($"[bundle] full pack kept over trimmed one: {guard.Pack(full)!.Partial == false && guard.Pack(full)!.Pieces.Count == store.Pack(full)!.Pieces.Count}");

        var legacy = Path.Combine(tmp, "legacy.ktpack");
        UserPacks.Export(store, "example.miranda-de-azan-1812.extras", legacy);
        var lm = WorkshopContent.ImportFile(new PackageStore(Path.Combine(tmp, "store-legacy")), legacy);
        GD.Print($"[bundle] legacy pack file: {lm.Kind} {lm.Id}");
        GD.Print($"[bundle] clean store for hosting: {Path.Combine(tmp, "store-Used")}");
    }
}
