using KTable.Core.Model;

namespace KTable.Core.Tests;

public sealed class ModuleTurnTests
{
    static void Near((double X, double Y, double Z) want, (double X, double Y, double Z) got)
    {
        const int Digits = 9;
        Assert.Equal(want.X, got.X, Digits);
        Assert.Equal(want.Y, got.Y, Digits);
        Assert.Equal(want.Z, got.Z, Digits);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(10, 200, -30)]
    [InlineData(-45, 90, 180)]
    [InlineData(89.9, 15, 0)]
    public void A_turn_read_back_as_angles_turns_every_axis_as_the_original_did(double x, double y, double z)
    {
        var turn = ModuleTurn.Of(x, y, z);
        var (ax, ay, az) = turn.Euler();
        var again = ModuleTurn.Of(ax, ay, az);
        Near(turn.Apply(1, 0, 0), again.Apply(1, 0, 0));
        Near(turn.Apply(0, 1, 0), again.Apply(0, 1, 0));
        Near(turn.Apply(0, 0, 1), again.Apply(0, 0, 1));
    }

    [Fact]
    public void A_turn_about_the_vertical_axis_takes_forward_to_the_right_as_the_game_turns_it()
    {
        Near((1, 0, 0), ModuleTurn.Of(0, 90, 0).Apply(0, 0, 1));
        Near((0, 0, -1), ModuleTurn.Of(0, 90, 0).Apply(1, 0, 0));
    }

    [Fact]
    public void A_piece_turned_over_about_its_forward_axis_mirrors_across_and_up()
    {
        var over = ModuleTurn.Of(0, 0, 180);
        Near((-1, 0, 0), over.Apply(1, 0, 0));
        Near((0, -1, 0), over.Apply(0, 1, 0));
        Near((0, 0, 1), over.Apply(0, 0, 1));
        Assert.True(over.UpY < 0);
    }

    [Fact]
    public void The_game_turns_about_z_first_then_x_then_y()
    {
        var aboutZ = ModuleTurn.Of(0, 0, 50).Apply(1, 2, 3);
        var thenX = ModuleTurn.Of(40, 0, 0).Apply(aboutZ.X, aboutZ.Y, aboutZ.Z);
        var thenY = ModuleTurn.Of(0, 30, 0).Apply(thenX.X, thenX.Y, thenX.Z);
        Near(thenY, ModuleTurn.Of(40, 30, 50).Apply(1, 2, 3));
    }

    [Fact]
    public void Undoing_a_turn_brings_a_point_back_and_turns_chain_through_parents()
    {
        var t = ModuleTurn.Of(17, 123, -64);
        var (x, y, z) = t.Apply(0.3, -1.2, 2.5);
        Near((0.3, -1.2, 2.5), t.Undo(x, y, z));
        var parent = ModuleTurn.Of(0, 0, 180);
        var child = ModuleTurn.Of(0, 180, 0);
        Near(parent.Apply(child.Apply(1, 0, 0).X, child.Apply(1, 0, 0).Y, child.Apply(1, 0, 0).Z), parent.Then(child).Apply(1, 0, 0));
    }
}
