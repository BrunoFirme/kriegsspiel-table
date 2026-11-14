using System;
using System.IO;
using System.Threading.Tasks;
using KTable.Client;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class SharedTilesTests
{
    const string Whole = "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", Partial = "1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e1e";
    const string Tile = "0/0_0.webp";

    static string Scratch() => Path.Combine(Path.GetTempPath(), "ktable-node-tiles", Guid.NewGuid().ToString("N"));

    static string Put(string root, string package, string map, string rel, string text)
    {
        var path = Path.Combine(root, package, "tiles", map, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    static bool OneCopy(string a, string b)
    {
        int before = File.ReadAllBytes(b).Length;
        long own = new FileInfo(a).Length;
        using (var grown = new FileStream(a, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) grown.WriteByte(7);
        bool one = File.ReadAllBytes(b).Length == before + 1;
        using (var back = new FileStream(a, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete)) back.SetLength(own);
        return one;
    }

    [NodeTest]
    static Task A_table_saved_as_a_scenario_shares_a_whole_map_s_tiles_with_the_library_and_copies_a_map_still_being_made(TestRunner r)
    {
        var root = Scratch();
        var library = Put(root, SheetImporter.Package, Whole, Tile, "tile");
        Put(root, SheetImporter.Package, Whole, "index.json", "{}");
        var unfinished = Put(root, SheetImporter.Package, Partial, Tile, "half");
        var store = new PackageStore(root);
        var game = new GameState { Name = "Haiti" };
        game.Sheets.Add(new SheetDef { Id = "s-1", Name = "Island", Image = Whole, Package = SheetImporter.Package });
        game.Sheets.Add(new SheetDef { Id = "s-2", Name = "Sketch", Image = Partial, Package = SheetImporter.Package });

        var dir = ScenarioFiles.SaveTable(store, game, "Haiti", null);

        var folder = Path.Combine(root, "scenarios", dir);
        string own = Path.Combine(folder, "tiles", Whole, Tile), half = Path.Combine(folder, "tiles", Partial, Tile);
        Check.Equal("tile", File.ReadAllText(own), "the scenario has the map's tile in its own folder");
        Check.That(File.Exists(Path.Combine(folder, "tiles", Whole, "index.json")), "and the map's index");
        Check.That(OneCopy(library, own), "the scenario's tile and the library's are one copy on disk");
        Check.That(!File.Exists(half) || !OneCopy(unfinished, half), "a map the library holds only part of is never shared");
        var saved = Json.Load<Scenario>(Path.Combine(folder, "scenario.json"));
        Check.Equal("scenarios/" + dir, saved.Sheets[0].Package, "the saved sheet points at the scenario's own folder");
        File.Delete(library);
        Check.Equal("tile", File.ReadAllText(own), "the scenario's tile outlives the library's");
        BestEffort.DeleteFolder(root);
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task The_sweep_at_start_joins_a_map_held_twice_into_one_copy(TestRunner r)
    {
        var root = Scratch();
        string packages = Path.Combine(root, "packages"), empty = Path.Combine(root, "none");
        var library = Put(packages, SheetImporter.Package, Whole, Tile, "tile");
        Put(packages, SheetImporter.Package, Whole, "index.json", "{}");
        var own = Put(packages, "scenarios/haiti", Whole, Tile, "tile");
        Put(packages, "scenarios/haiti", Whole, "index.json", "{}");
        StaleFiles.Settle();

        await StaleFiles.Sweep(empty, Array.Empty<string>(), empty, empty, Array.Empty<string>(), DateTime.UtcNow, packages);
        await r.Frame();

        Check.Equal("tile", File.ReadAllText(own), "the scenario still reads its tile");
        Check.That(OneCopy(library, own), "and it is the library's copy");
        Check.That(File.Exists(SharedMaps.RecordBeside(packages)), "what was joined is noted beside the packages, not inside them");
        BestEffort.DeleteFolder(root);
    }
}
