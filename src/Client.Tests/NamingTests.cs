using KTable.Core.Model;

namespace KTable.Client.Tests;

public class NamingTests
{
    [Fact]
    public void Names_become_lower_case_ascii_with_single_dashes_cut_to_length()
    {
        Assert.Equal("the-feud-pogue-vs-boggs", Naming.ScenarioDir("  The Feud: Pogue vs. Boggs!  "));
        Assert.Equal("scenario", Naming.ScenarioDir("¡¿ — ?!"));
        Assert.Equal("caf-r-sum", Naming.ScenarioDir("Café Résumé"));
        Assert.Equal(new string('a', 60), Naming.ScenarioDir(new string('a', 70)));
        Assert.Equal(new string('a', 59), Naming.ScenarioDir(new string('a', 59) + "-b"));
        Assert.Equal("user.my-pieces", Naming.UserPackId("***"));
        Assert.Equal("user.prussian-blocks-1866", Naming.UserPackId("Prussian blocks (1866)"));
        Assert.Equal(PackManifest.UserPrefix + new string('x', 50), Naming.UserPackId(new string('X', 51)));
        Assert.Equal("piece", Naming.PieceSlug(""));
        Assert.Equal(new string('z', 39), Naming.PieceSlug(new string('z', 39) + " z"));
    }

    [Fact]
    public void A_new_piece_id_is_the_packs_the_name_and_eight_hex_no_other_piece_has()
    {
        var pack = new PackManifest { Id = "user.mine" };
        var id = Naming.NewPieceId(pack, "2/IR 14");
        Assert.Matches(@"^user\.mine\.2-ir-14-[0-9a-f]{8}$", id);
        pack.Pieces.Add(new PieceDef { Id = id });
        Assert.NotEqual(id, Naming.NewPieceId(pack, "2/IR 14"));
    }

    [Fact]
    public void A_pieces_name_from_a_file_name_has_spaces_for_dashes_and_a_capital()
    {
        Assert.Equal("2nd bn left", Naming.PieceName("2nd_bn--left"));
        Assert.Equal("Uhlan squadron", Naming.PieceName(" uhlan_ squadron "));
        Assert.Equal("Piece", Naming.PieceName("_-_"));
    }

    [Fact]
    public void A_pieces_assets_are_its_own_then_its_states_in_order()
    {
        var d = new PieceDef { Image = "i", Back = null, Texture = "t", Mesh = "m" };
        d.States.Add(new PieceStateDef { Image = "si", Back = "sb" });
        d.States.Add(new PieceStateDef { Mesh = "sm" });
        Assert.Equal(new[] { "i", "t", "m", "si", "sb", "sm" }, Naming.AssetsOf(d));
    }

    [Fact]
    public void The_shapes_built_onto_a_block_are_among_its_assets()
    {
        var d = new PieceDef { Image = "i", AddOnMesh = "am", AddOnTexture = "at" };
        Assert.Equal(new[] { "i", "am", "at" }, Naming.AssetsOf(d));
    }
}
