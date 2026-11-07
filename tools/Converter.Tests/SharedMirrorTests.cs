using System.Text.Json.Nodes;
using KTable.Client;
using KTable.Client.Effects;
using KTable.Core;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class SharedMirrorTests : ConverterHarness
{
    static JsonObject Across(JsonObject o, double sx)
    {
        o["Transform"]!["scaleX"] = sx;
        return o;
    }

    [Fact]
    public void A_stock_die_scaled_negative_across_is_a_mirrored_copy_in_the_module_s_pack_with_its_faces_turned_with_it()
    {
        var cfg = TempConfig();
        var rep = new Report();
        var defs = new DefFactory(cfg, new AssetStore(cfg, rep), rep);
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);

        var plain = defs.DefFor(new ModuleObj(Obj("Die_6", "", 1)), pack)!;
        var mirrored = defs.DefFor(new ModuleObj(Across(Obj("Die_6", "", 1), -1)), pack)!;
        var again = defs.DefFor(new ModuleObj(Across(Obj("Die_6", "", 1), -1)), pack)!;

        Assert.False(plain.Copied);
        Assert.Equal((DefFactory.StdDicePack, true), (mirrored.FromPack, mirrored.Copied));
        Assert.StartsWith("t.", mirrored.Def.Id);
        Assert.Contains(mirrored.Def, pack.M.Pieces);
        Assert.Same(mirrored.Def, again.Def);
        Assert.NotEqual(plain.Def.Mesh, mirrored.Def.Mesh);
        Assert.Equal(plain.Def.Texture, mirrored.Def.Texture);
        Assert.All(Naming.AssetsOf(mirrored.Def), sha => Assert.True(File.Exists(Path.Combine(pack.Dir, pack.M.Assets[sha].File)), sha));
        Assert.Equal(plain.Def.Faces.Select(f => -f.Up[0]), mirrored.Def.Faces.Select(f => f.Up[0]));
        Assert.Equal(plain.Def.Faces.Select(f => f.Up[2]), mirrored.Def.Faces.Select(f => f.Up[2]));
    }

    [Fact]
    public void A_shared_tree_scaled_negative_across_is_placed_from_a_mirrored_copy_at_the_same_size()
    {
        TempConfig();
        var dir = Path.Combine(Packages, EffectKinds.StandardPack);
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        File.WriteAllLines(Path.Combine(dir, "assets", "tree.obj"), new[] { "v 1 0 0", "v 0 1 0", "v 0 0 1", "f 1 2 3" });
        var shared = new PackManifest { Id = EffectKinds.StandardPack, Name = "Effects" };
        shared.Assets["tree"] = new AssetEntry { File = "assets/tree.obj", Kind = "mesh" };
        shared.Pieces.Add(new PieceDef { Id = EffectKinds.StandardPack + ".tree", Name = "Tree", Kind = "model", Mesh = "tree", Size = new Size2(3.62 / 2, 3.4 / 2), Thickness = 1.25 });
        Json.Save(Path.Combine(dir, "pack.json"), shared);
        var main = typeof(Config).Assembly.EntryPoint!;

        var code = (int)main.Invoke(null, new object[]
        {
            new[] { "import", Module("grove", Across(Obj("Tileset_Tree", "Oak", 1.5, new JsonObject()), -1.5)), "--config", Path.Combine(_root, "converter.json"), "--out", Packages },
        })!;

        Assert.Equal(0, code);
        var oak = Assert.Single(Json.Load<Scenario>(Path.Combine(Packages, "scenarios", "grove", "scenario.json")).UmpireBoard);
        var pack = Json.Load<PackManifest>(Path.Combine(Packages, "tts.grove", "pack.json"));
        var tree = pack.Pieces.Single(d => d.Id == oak.Def);
        Assert.Equal(3.0, oak.Scale);
        Assert.Equal("v -1 0 0", File.ReadLines(Path.Combine(Packages, "tts.grove", pack.Assets[tree.Mesh!].File)).First());
    }
}
