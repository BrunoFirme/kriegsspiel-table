using System.Globalization;
using KTable.Converter.Bundles;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class PackedBundleTests : ConverterHarness
{
    static readonly double[] Verts = [0, 0.25, 0, 1, 4.01418, 0, 1, 0, 1.00098, 0, 0, 1];
    static readonly double[] Normals = [0, 1, 0, 0.6, 0.8, 0, 0, 1, 0, 0, 1, 0];
    static readonly double[] Uvs = [0.1, 0.1, 0.963867, 0.1, 0.9, 0.443848, 0.1, 0.9];
    static readonly int[] Triangles = [0, 1, 2, 0, 2, 3];

    static string Field()
    {
        var parts = new List<ModelPart> { new(Verts, Uvs, Triangles, null, Normals: Normals), new(Verts, Uvs, Triangles, null, Colour: [0.5, 0.25, 0]) };
        return ModelAtlas.Obj(new BundleModel("s", "Field", parts, [0, 0, 0], [1, 4.01418, 1.00098], 4, [], 0), new Dictionary<string, AtlasPlace>(), 10, 10);
    }

    static List<string> Read(string line) =>
        line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number.ToString("R", CultureInfo.InvariantCulture) : word).ToList();

    static IEnumerable<List<string>> Said(IEnumerable<string> lines) => lines.Where(line => line.Trim().Length > 0).Select(Read);

    [Fact]
    public void A_model_read_from_an_asset_bundle_is_kept_packed_and_reads_back_statement_for_statement_as_the_text_it_was_written_from()
    {
        var obj = Field();

        var (bytes, type) = BundleBake.Stored(obj);

        Assert.Equal(PackedModel.Type, type);
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "field" + PackedModel.Ext);
        File.WriteAllBytes(file, bytes);
        Assert.Equal(Said(obj.Split('\n')), Said(PackedModel.Lines(file)));
    }

    [Fact]
    public void A_model_that_cannot_be_packed_is_kept_as_text_unchanged()
    {
        const string Odd = "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 1 1 0\nf 1 2 4 3\n";

        var (bytes, type) = BundleBake.Stored(Odd);

        Assert.Equal("obj", type);
        Assert.Equal(Odd, System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void A_bundle_whose_model_was_kept_as_text_is_read_again_and_one_already_packed_is_not()
    {
        var pack = new PackManifest { Id = BundleBake.PackId, Name = "Models" };
        pack.Assets["old"] = new AssetEntry { File = "assets/old.obj", Kind = "mesh" };
        pack.Assets["new"] = new AssetEntry { File = "assets/new" + PackedModel.Ext, Kind = "mesh" };

        Assert.False(BundleBake.Packed(pack, new PieceDef { Kind = "model", Mesh = "old" }));
        Assert.True(BundleBake.Packed(pack, new PieceDef { Kind = "model", Mesh = "new" }));
        Assert.False(BundleBake.Packed(pack, new PieceDef { Kind = "model", Mesh = "gone" }));
        Assert.False(BundleBake.Packed(pack, new PieceDef { Kind = "model" }));
    }

    [Fact]
    public void A_table_exported_as_a_module_save_points_a_packed_model_at_a_text_copy_kept_beside_the_save()
    {
        var cfg = TempConfig();
        var dir = Path.Combine(Packages, "user.guns");
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        var obj = Field();
        File.WriteAllBytes(Path.Combine(dir, "assets", "gun" + PackedModel.Ext), BundleBake.Stored(obj).Bytes);
        var pack = new PackManifest { Id = "user.guns", Name = "Guns", Pieces = { new PieceDef { Id = "user.guns.gun", Name = "Gun", Kind = "model", Mesh = "gun", Size = new Size2(1, 1), Thickness = 1 } } };
        pack.Assets["gun"] = new AssetEntry { File = "assets/gun" + PackedModel.Ext, Kind = "mesh" };
        KTable.Core.Json.Save(Path.Combine(dir, "pack.json"), pack);
        var scenario = new Scenario { Id = "range", Name = "Range", Packs = { "user.guns" } };
        scenario.UmpireBoard.Add(new PieceInstance { Id = "p1", Def = "user.guns.gun" });
        var source = Path.Combine(_root, "range.json");
        KTable.Core.Json.Save(source, scenario);

        var saved = new ModuleExport(cfg).Export(source, Path.Combine(_root, "modules", "range.json"));

        var model = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(saved))!["ObjectStates"]!.AsArray().Single(o => o!["Name"]!.GetValue<string>() == "Custom_Model")!;
        var file = new Uri(model["CustomMesh"]!["MeshURL"]!.GetValue<string>()).LocalPath;
        Assert.Equal(Path.Combine(_root, "modules", "range models", "gun.obj"), Path.GetFullPath(file));
        Assert.Equal(Said(obj.Split('\n')), Said(File.ReadLines(file)));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void A_packed_model_scaled_negative_is_mirrored_as_its_text_would_be_and_stays_packed(bool x, bool y, bool z)
    {
        var cfg = TempConfig();
        var pack = new PackOut(cfg.Output, "t", "T", "t", null);
        var obj = Field();
        var source = Path.Combine(_root, "field" + PackedModel.Ext);
        File.WriteAllBytes(source, BundleBake.Stored(obj).Bytes);
        DefFactory.Put(pack, "field", PackedModel.Type, source, "mesh", 0, 0);
        var def = new PieceDef { Id = "t.field", Name = "Field", Kind = "model", Mesh = "field" };
        var mirror = new Mirror(x, y, z);

        new PieceMirror(cfg.Cache).Apply(def, pack, mirror);

        Assert.NotEqual("field", def.Mesh);
        var kept = pack.M.Assets[def.Mesh!].File;
        Assert.True(PackedModel.Is(kept), kept);
        Assert.Equal(Said(PieceMirror.MirroredObj(obj.Split('\n'), mirror)), Said(PackedModel.Lines(Path.Combine(pack.Dir, kept))));
    }
}
