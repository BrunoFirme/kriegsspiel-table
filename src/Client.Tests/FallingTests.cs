using KTable.Client.Hand;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class FallingTests
{
    static Falling<FakePiece> On(params FakePiece[] table)
    {
        var footing = new Footing<FakePiece>((_, _) => table, _ => false);
        return new Falling<FakePiece>(footing, (_, _) => table, (_, id) => table.FirstOrDefault(p => p.Piece.Id == id), _ => true);
    }

    static Dictionary<string, double> Drops(Command c) =>
        (c as Batch)?.Items.OfType<MovePiece>().ToDictionary(m => m.Id, m => Math.Round(m.Y, 3)) ?? new();

    static MovePiece Move(FakePiece p, double x, double z) => new() { Layer = p.Layer, Id = p.Piece.Id, Pos = new Vec2(x, z), RotDeg = p.Piece.RotDeg };

    [Fact]
    public void A_piece_left_in_the_air_when_the_one_under_it_is_deleted_comes_down_to_the_table()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var rider = new FakePiece("rider", 0, 0, 1, 1, y: 0.3);
        var sent = On(block, rider).With(new DeletePiece { Layer = Layers.Umpire, Id = "base" });
        Assert.Equal(new Dictionary<string, double> { ["rider"] = 0 }, Drops(sent));
        Assert.IsType<DeletePiece>(((Batch)sent).Items[0]);
    }

    [Fact]
    public void A_piece_left_behind_when_the_one_under_it_moves_away_comes_down()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var rider = new FakePiece("rider", 1.5, 0, 1, 1, y: 0.3);
        var sent = On(block, rider).With(Move(block, 10, 0));
        Assert.Equal(0, Drops(sent)["rider"]);
    }

    [Fact]
    public void A_piece_still_over_the_moved_one_stays_where_it_is()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var rider = new FakePiece("rider", 0, 0, 1, 1, y: 0.3);
        var nudge = Move(block, 0.2, 0);
        Assert.Same(nudge, On(block, rider).With(nudge));
    }

    [Fact]
    public void A_locked_piece_stays_put()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var rider = new FakePiece("rider", 0, 0, 1, 1, y: 0.3);
        rider.Piece.Locked = true;
        var delete = new DeletePiece { Layer = Layers.Umpire, Id = "base" };
        Assert.Same(delete, On(block, rider).With(delete));
    }

    [Fact]
    public void A_piece_falls_only_as_far_as_the_next_piece_below()
    {
        var bottom = new FakePiece("bottom", 0, 0, 6, 6, thickness: 0.2f);
        var middle = new FakePiece("middle", 0, 0, 3, 3, y: 0.2, thickness: 0.3f);
        var top = new FakePiece("top", 0, 0, 1, 1, y: 0.5);
        var sent = On(bottom, middle, top).With(new DeletePiece { Layer = Layers.Umpire, Id = "middle" });
        Assert.Equal(0.2, Drops(sent)["top"]);
    }

    [Fact]
    public void A_stack_left_in_the_air_comes_down_together()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var lower = new FakePiece("lower", 0, 0, 2, 2, y: 0.3, thickness: 0.2f);
        var upper = new FakePiece("upper", 0, 0, 1, 1, y: 0.5);
        var sent = On(block, lower, upper).With(new Stow { Layer = Layers.Umpire, Holder = "bag", Id = "base" });
        Assert.Equal(new Dictionary<string, double> { ["lower"] = 0, ["upper"] = 0.2 }, Drops(sent));
    }

    [Fact]
    public void A_piece_attached_to_the_moved_one_is_left_to_travel_with_it()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var flag = new FakePiece("flag", 0, 0, 1, 1, y: 0.3);
        block.Piece.Attached.Add("flag");
        var move = Move(block, 10, 0);
        Assert.Same(move, On(block, flag).With(move));
    }

    [Fact]
    public void A_piece_carried_in_the_same_batch_is_not_dropped()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var rider = new FakePiece("rider", 0, 0, 1, 1, y: 0.3);
        var batch = new Batch { Items = { Move(block, 10, 0), new MovePiece { Layer = Layers.Umpire, Id = "rider", Pos = new Vec2(10, 0), Y = 0.3 } } };
        Assert.Same(batch, On(block, rider).With(batch));
    }

    [Fact]
    public void Commands_that_move_nothing_pass_through()
    {
        var block = new FakePiece("base", 0, 0, 4, 4, thickness: 0.3f);
        var c = new SetLabel { Layer = Layers.Umpire, Id = "base", Value = "x" };
        Assert.Same(c, On(block).With(c));
    }
}
