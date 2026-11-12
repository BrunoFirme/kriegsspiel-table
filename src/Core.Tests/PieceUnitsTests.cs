using KTable.Core.Model;

namespace KTable.Core.Tests;

public class PieceUnitsTests
{
    const double TokenArea = 14;

    static (PackageStore Store, PieceInstance Piece) With(PieceDef def, int pictureW = 0, int pictureH = 0)
    {
        var store = new PackageStore(Path.GetTempPath());
        var pack = new PackManifest { Id = "units", Name = "Units" };
        if (pictureW > 0) pack.Assets["face"] = new AssetEntry { File = "face.png", W = pictureW, H = pictureH };
        store.RegisterDef(pack, def);
        return (store, new PieceInstance { Id = "p1", Def = def.Id });
    }

    [Fact]
    public void A_module_tile_counts_one_object_unit_as_half_its_depth_on_both_axes()
    {
        var (store, p) = With(new PieceDef { Id = "tile", ModuleType = "Custom_Tile", Size = new Size2(0.856, 0.6129) });
        var u = PieceUnits.Of(store, p, store.Def("tile"));
        Assert.Equal(0.30645, u.W, 5);
        Assert.Equal(0.30645, u.H, 5);
    }

    [Fact]
    public void A_cut_out_token_spreads_its_area_over_its_picture_s_shape_and_a_model_keeps_its_module_unit()
    {
        var (store, p) = With(new PieceDef { Id = "token", Shape = "alpha", Image = "face", Size = new Size2(2, 1) }, pictureW: 400, pictureH: 200);
        var u = PieceUnits.Of(store, p, store.Def("token"));
        Assert.Equal(2 / Math.Sqrt(TokenArea * 2), u.W, 6);
        Assert.Equal(1 / Math.Sqrt(TokenArea / 2), u.H, 6);
        var (bare, q) = With(new PieceDef { Id = "blank", Shape = "alpha", Size = new Size2(2, 1) });
        Assert.Equal(Math.Sqrt(2 / TokenArea), PieceUnits.Of(bare, q, bare.Def("blank")).W, 6);
        var (models, m) = With(new PieceDef { Id = "model", Kind = "model", LocalUnit = new Size2(0.2, 0.3), Size = new Size2(5, 5) });
        Assert.Equal(new Size2(0.2, 0.3), PieceUnits.Of(models, m, models.Def("model")));
    }
}
