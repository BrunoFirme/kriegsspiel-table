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
}
