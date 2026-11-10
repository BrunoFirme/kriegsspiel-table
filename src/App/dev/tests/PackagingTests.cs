using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.App;

static class PackagingTests
{
    static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    static (PackageStore store, string packId, PieceDef def) PackWithAPiece(string root)
    {
        var packId = PackEdit.CreatePack(new PackageStore(root), "Shared pieces", "Tester");
        var store = new PackageStore(root);
        var pack = store.Pack(packId)!;
        var sha = PackEdit.AddImage(store, pack, Godot.Image.CreateEmpty(8, 8, false, Godot.Image.Format.Rgba8));
        var def = PackEdit.NewDef(store, pack, "Hussars", sha, 8, 8, 1.0, "box");
        PackEdit.Save(store, pack);
        return (new PackageStore(root), packId, def);
    }

    [NodeTest]
    static Task A_pack_exported_and_installed_elsewhere_has_its_pieces_and_their_pictures(TestRunner r)
    {
        var (store, packId, def) = PackWithAPiece(NewRoot());
        var file = Path.Combine(NewRoot(), "shared.ktpack");
        Check.That(WorkshopContent.ExportFile(store, "pack", packId, WorkshopContent.PackMode.None, file) > 0, "a file written");
        var elsewhere = new PackageStore(NewRoot());
        var meta = WorkshopContent.ImportFile(elsewhere, file);
        Check.Equal(("pack", packId), (meta.Kind, meta.Id), "what was installed");
        var got = new PackageStore(elsewhere.Root).Pack(packId)!;
        Check.Equal(def.Id, got.Pieces.Single().Id, "its piece");
        Check.That(File.Exists(Path.Combine(elsewhere.PackageDir(packId), "assets", def.Image + ".png")), "its picture");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_scenario_exported_with_its_packs_installs_whole_where_they_were_missing(TestRunner r)
    {
        var root = NewRoot();
        var (store, packId, def) = PackWithAPiece(root);
        var dir = ScenarioFiles.CreateBlank(store, "Feud", "1812-07-22T06:00", 15);
        var scPath = Path.Combine(root, "scenarios", dir, "scenario.json");
        var sc = Json.Load<Scenario>(scPath);
        sc.Packs = new() { packId };
        sc.UmpireBoard = new() { new PieceInstance { Id = "p1", Def = def.Id, Pos = new Vec2(3, 4) } };
        Json.Save(scPath, sc);

        var file = Path.Combine(NewRoot(), "feud.ktpack");
        WorkshopContent.ExportFile(new PackageStore(root), "scenario", dir, WorkshopContent.PackMode.Whole, file);
        var elsewhere = new PackageStore(NewRoot());
        Check.Equal(packId, string.Join(",", WorkshopContent.PacksItWouldPutInPlace(elsewhere, file)), "what installing it would bring");
        var meta = WorkshopContent.ImportFile(elsewhere, file);
        Check.Equal("scenario", meta.Kind, "a scenario");
        var reread = new PackageStore(elsewhere.Root);
        Check.That(reread.Pack(packId) != null, "its pack came with it");
        var back = Json.Load<Scenario>(Path.Combine(elsewhere.Root, "scenarios", meta.Id, "scenario.json"));
        Check.Equal(new Vec2(3, 4), back.UmpireBoard.Single().Pos, "its piece where it was");
        Check.Equal(0, WorkshopContent.MissingPacks(reread, back).Count, "nothing missing");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_scenario_exported_as_the_map_only_or_as_its_setup_gives_away_nothing_of_the_game_in_play(TestRunner r)
    {
        var root = NewRoot();
        var (store, packId, def) = PackWithAPiece(root);
        var dir = ScenarioFiles.CreateBlank(store, "Feud", "1812-07-22T06:00", 15);
        var folder = Path.Combine(root, "scenarios", dir);
        var sc = Json.Load<Scenario>(Path.Combine(folder, "scenario.json"));
        sc.Packs = new() { packId };
        sc.Sheets = new() { new SheetDef { Id = "s-000001", Name = "Ground", Package = "scenarios/" + dir } };
        sc.UmpireBoard = new() { new PieceInstance { Id = "p1", Def = def.Id, Pos = new Vec2(1, 1) } };
        Json.Save(Path.Combine(folder, ScenarioFiles.StartFile), sc);
        sc.UmpireBoard[0].Pos = new Vec2(40, 40);
        sc.UmpireBoard[0].Note = "ambush here";
        sc.Briefing = "Blue does not know Red crossed at night";
        sc.Script = "function on_load() end";
        sc.Clock.Turn = 12;
        Json.Save(Path.Combine(folder, "scenario.json"), sc);

        Scenario Installed(WorkshopContent.TableMode table, out string there)
        {
            var file = Path.Combine(NewRoot(), "feud.ktpack");
            WorkshopContent.ExportFile(new PackageStore(root), "scenario", dir, WorkshopContent.PackMode.Used, file, SavesMode.None, table);
            var elsewhere = new PackageStore(NewRoot());
            var meta = WorkshopContent.ImportFile(elsewhere, file);
            there = Path.Combine(elsewhere.Root, "scenarios", meta.Id);
            return Json.Load<Scenario>(Path.Combine(there, "scenario.json"));
        }

        var map = Installed(WorkshopContent.TableMode.MapOnly, out var mapFolder);
        Check.Equal((0, (string?)null, (string?)null, 0), (map.UmpireBoard.Count, map.Briefing, map.Script, map.Packs.Count), "the map only: no pieces, briefing, script or packs");
        Check.Equal(("Ground", 12), (map.Sheets.Single().Name, map.Clock.Turn), "its sheets and clock");
        Check.That(!File.Exists(Path.Combine(mapFolder, ScenarioFiles.StartFile)), "nor the setup beside it");
        var whole = string.Concat(Directory.EnumerateFiles(mapFolder, "*.json", SearchOption.AllDirectories).Select(File.ReadAllText));
        Check.That(!whole.Contains("ambush") && !whole.Contains("crossed at night"), "no word of the game anywhere in the file");

        var setup = Installed(WorkshopContent.TableMode.Setup, out _);
        Check.Equal((new Vec2(1, 1), (string?)null, (string?)null), (setup.UmpireBoard.Single().Pos, setup.UmpireBoard.Single().Note, setup.Briefing), "the setup: pieces where they began");

        var live = Installed(WorkshopContent.TableMode.AsItStands, out _);
        Check.Equal((new Vec2(40, 40), "ambush here"), (live.UmpireBoard.Single().Pos, live.UmpireBoard.Single().Note), "as it stands: the table as it is");
        return Task.CompletedTask;
    }

    static string FeudFile(string id)
    {
        var root = NewRoot();
        var store = new PackageStore(root);
        var dir = ScenarioFiles.CreateBlank(store, "Feud", "1812-07-22T06:00", 15);
        var scPath = Path.Combine(root, "scenarios", dir, "scenario.json");
        var sc = Json.Load<Scenario>(scPath);
        sc.Id = id;
        sc.Briefing = "the newcomer";
        Json.Save(scPath, sc);
        var file = Path.Combine(NewRoot(), "feud.ktpack");
        WorkshopContent.ExportFile(new PackageStore(root), "scenario", dir, WorkshopContent.PackMode.None, file);
        return file;
    }

    [NodeTest]
    static Task Another_scenario_of_the_same_name_installs_beside_the_one_here_and_never_over_it(TestRunner r)
    {
        var here = new PackageStore(NewRoot());
        var mine = ScenarioFiles.CreateBlank(here, "Feud", "1880-05-01T06:00", 15);
        var file = FeudFile("tts.feud");
        Check.Equal(null, WorkshopContent.ScenarioItWouldReplace(here, file), "nothing to ask about");
        var meta = WorkshopContent.ImportFile(here, file);
        Check.Equal(mine + "-2", meta.Id, "the newcomer's folder");
        Check.Equal(0, meta.Trashed.Count, "nothing sent to the trash");
        Check.Equal("local." + mine, Json.Load<Scenario>(Path.Combine(here.Root, "scenarios", mine, "scenario.json")).Id, "the one here as it was");
        Check.Equal("the newcomer", Json.Load<Scenario>(Path.Combine(here.Root, "scenarios", meta.Id, "scenario.json")).Briefing, "the newcomer beside it");
        Check.Equal(mine + "-2", WorkshopContent.ImportFile(here, file).Id, "installed again, it takes its own folder again");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task The_same_scenario_installed_again_is_asked_about_and_replaces_the_one_here_through_the_trash(TestRunner r)
    {
        var here = new PackageStore(NewRoot());
        var mine = ScenarioFiles.CreateBlank(here, "Feud", "1880-05-01T06:00", 15);
        var file = FeudFile("local." + mine);
        Check.Equal("Feud", WorkshopContent.ScenarioItWouldReplace(here, file), "the scenario the person is asked about");
        var meta = WorkshopContent.ImportFile(here, file);
        Check.Equal(mine, meta.Id, "the same folder");
        Check.Equal(1, meta.Trashed.Count, "the one that was here is in the trash");
        Check.Equal("the newcomer", Json.Load<Scenario>(Path.Combine(here.Root, "scenarios", mine, "scenario.json")).Briefing, "the installed one in its place");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_scenario_exported_and_installed_elsewhere_keeps_its_relief_and_painted_ground(TestRunner r)
    {
        const float HillHeight = 12.5f;
        const int HillCell = 5;
        var root = NewRoot();
        var store = new PackageStore(root);
        var dir = ScenarioFiles.CreateBlank(store, "Ridge", "1866-07-03T08:00", 2);
        var folder = Path.Combine(root, "scenarios", dir);
        var grid = new ReliefGrid(40, 30);
        grid.H[HillCell] = HillHeight;
        grid.Kind[HillCell] = (byte)(GroundKinds.All.Count - 1);
        var (sha, _) = ReliefFiles.Save(folder, grid);
        var scPath = Path.Combine(folder, "scenario.json");
        var sc = Json.Load<Scenario>(scPath);
        sc.Sheets = new() { new SheetDef { Id = "s-000001", Name = "Ground", Package = "scenarios/" + dir, Relief = sha } };
        Json.Save(scPath, sc);

        var file = Path.Combine(NewRoot(), "ridge.ktpack");
        WorkshopContent.ExportFile(new PackageStore(root), "scenario", dir, WorkshopContent.PackMode.None, file);
        var elsewhere = new PackageStore(NewRoot());
        var meta = WorkshopContent.ImportFile(elsewhere, file);

        var there = Path.Combine(elsewhere.Root, "scenarios", meta.Id);
        var index = ReliefFiles.LoadIndex(Path.Combine(there, ReliefFiles.HeaderRel(sha)));
        var back = ReliefFiles.Load(index, rel => Path.Combine(there, rel) is var p && File.Exists(p) ? p : null);
        Check.Equal(HillHeight, back.H[HillCell], "the hill's height");
        Check.Equal(grid.Kind[HillCell], back.Kind[HillCell], "the ground painted on it");
        return Task.CompletedTask;
    }
}
