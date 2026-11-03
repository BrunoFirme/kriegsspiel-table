using KTable.Core;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class PieceFilesTests
{
    [Fact]
    public void A_piece_s_decal_pictures_are_fetched_with_its_own()
    {
        var root = TestFolders.New();
        Json.Save(Path.Combine(root, "pk", "pack.json"), new PackManifest
        {
            Id = "pk", Name = "Pack",
            Pieces = { new PieceDef { Id = "pk.block", Name = "Block", Image = "face" } },
            Assets = { ["face"] = new AssetEntry { File = "assets/face.png" }, ["arms"] = new AssetEntry { File = "assets/arms.png" }, ["unused"] = new AssetEntry { File = "assets/unused.png" } },
        });
        var store = new PackageStore(root);
        store.LoadPacks(new[] { "pk" });
        var view = new GameState { Packs = { "pk" } };
        view.Layers[Layers.Umpire] = new() { new PieceInstance { Id = "p-1", Def = "pk.block", Decals = { new DecalDef { Picture = "arms" }, new DecalDef { Picture = "nowhere" } } } };

        Assert.Equal(new[] { ("pk", "assets/arms.png"), ("pk", "assets/face.png") }, PieceFiles.Of(view, store).Order());
    }
}
