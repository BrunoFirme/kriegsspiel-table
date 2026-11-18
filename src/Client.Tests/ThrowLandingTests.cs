using KTable.Client.Hand;
using KTable.Core.Model;
using static KTable.Client.Tests.HandLayouts;

namespace KTable.Client.Tests;

public class ThrowLandingTests
{
    static string Land(FakePiece[] table, params Thrown<FakePiece>[] thrown)
    {
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        return Describe(ThrowLanding.Land(footing, thrown).OrderBy(x => x.n.Piece.Id).Select(x => x.m));
    }

    static Thrown<FakePiece> At(FakePiece p, double x, double z, double bottom) => new(p, new Vec2(x, z), 180, bottom);

    [Fact]
    public void A_flicked_piece_left_on_the_edge_of_a_block_slides_off_it_to_the_table()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var a = new FakePiece("a", 9, 9, 2, 1);
        Assert.Equal("move a 3.0599999845027925,0 rot 180 y 0", Land([block, a], At(a, 2.7, 0, 0.4)));
    }

    [Fact]
    public void A_flicked_piece_physics_left_on_the_table_stays_where_it_lies()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var a = new FakePiece("a", 9, 9, 2, 1);
        Assert.Equal("move a 3.01,0 rot 180 y 0", Land([block, a], At(a, 3.01, 0, 0)));
    }

    [Fact]
    public void A_flicked_piece_that_comes_down_on_a_block_rests_on_its_top()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var a = new FakePiece("a", 9, 9, 1, 1);
        Assert.Equal("move a 0.5,0.5 rot 180 y 0.4000000059604645", Land([block, a], At(a, 0.5, 0.5, 0.42)));
    }

    [Fact]
    public void A_flicked_piece_still_in_the_air_when_the_settling_ends_comes_down_on_what_is_under_it()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var a = new FakePiece("a", 9, 9, 1, 1);
        var b = new FakePiece("b", 9, 9, 1, 1);
        Assert.Equal("move a 0.5,0.5 rot 180 y 0.4000000059604645\nmove b 8,0 rot 180 y 0", Land([block, a, b], At(a, 0.5, 0.5, 2), At(b, 8, 0, 2)));
    }

    [Fact]
    public void A_piece_that_lands_on_one_pushed_in_the_same_flick_rides_it_where_it_was_pushed_to()
    {
        var pushed = new FakePiece("pushed", 0, 0, 4, 4, thickness: 0.3f);
        var a = new FakePiece("a", 9, 9, 1, 1);
        Assert.Equal("move a 10,0 rot 180 y 0.30000001192092896\nmove pushed 10,0 rot 180 y 0", Land([pushed, a], At(a, 10, 0, 0.31), At(pushed, 10, 0, 0)));
    }

    [Fact]
    public void Where_a_pushed_piece_lay_before_the_flick_holds_nothing_up()
    {
        var pushed = new FakePiece("pushed", 0, 0, 4, 4, thickness: 0.3f);
        var a = new FakePiece("a", 9, 9, 1, 1);
        Assert.Equal("move a 0.5,0 rot 180 y 0\nmove pushed 10,0 rot 180 y 0", Land([pushed, a], At(pushed, 10, 0, 0), At(a, 0.5, 0, 0.31)));
    }

    [Fact]
    public void A_figure_flicked_onto_the_rim_of_a_block_stands_on_what_is_under_its_middle()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.4f);
        var figure = new FakePiece("figure", 9, 9, 2, 2, prop: true);
        Assert.Equal("move figure 2.8,0 rot 180 y 0", Land([block, figure], At(figure, 2.8, 0, 0.4)));
    }
}
