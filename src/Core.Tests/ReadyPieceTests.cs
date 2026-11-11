using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class ReadyPieceTests
{
    const string U = Layers.Umpire;
    const string Battery = "p-000101", Smoke = "p-000102", Flag = "p-000103";

    static GameState Table()
    {
        var g = CoreSamples.State();
        var layer = g.Layer(U);
        layer.Add(CoreSamples.Piece(Battery, x: 10, z: 10));
        layer.Add(CoreSamples.Piece(Smoke, x: 0, z: 0));
        layer.Add(CoreSamples.Piece(Flag, x: 1, z: 0));
        g.Find(U, Battery)!.RotDeg = 0;
        Ok(g, new Stow { Layer = U, Holder = Battery, Id = Flag });
        Ok(g, new Stow { Layer = U, Holder = Battery, Id = Smoke });
        return g;
    }

    static Command? Ok(GameState g, Command c)
    {
        Assert.Null(StateMachine.Apply(g, c, out var inverse));
        return inverse;
    }

    static PieceInstance Holder(GameState g) => g.Find(U, Battery)!;

    static Readiness State(GameState g) => ReadyPiece.Of(Holder(g), id => g.Find(U, id));

    static Command MarkSmoke => new SetReady { Layer = U, Holder = Battery, Ready = new ReadyPiece { Id = Smoke }, Move = Smoke };

    [Fact]
    public void Marking_a_held_piece_ready_puts_it_first_and_undo_puts_it_back()
    {
        var g = Table();
        Assert.Equal([Flag, Smoke], Holder(g).Stowed!.Select(s => s.Id));
        var undo = Ok(g, MarkSmoke);
        Assert.Equal([Smoke, Flag], Holder(g).Stowed!.Select(s => s.Id));
        Assert.Equal(Readiness.In, State(g));
        Ok(g, undo!);
        Assert.Equal([Flag, Smoke], Holder(g).Stowed!.Select(s => s.Id));
        Assert.Equal(Readiness.None, State(g));
    }

    [Fact]
    public void A_piece_not_held_there_cannot_be_marked()
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetReady { Layer = U, Holder = Battery, Ready = new ReadyPiece { Id = "p-999999" } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetReady { Layer = U, Holder = Battery, Ready = new ReadyPiece { Id = Smoke, Turn = double.NaN } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetReady { Layer = U, Holder = Battery, Move = "p-999999" }, out _));
    }

    [Fact]
    public void Released_the_first_time_it_comes_out_beside_the_holder_facing_as_it_does()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Holder(g).RotDeg = 90;
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        var smoke = g.Find(U, Smoke)!;
        Assert.Equal(Readiness.Out, State(g));
        Assert.Equal(ReadyPiece.Beside, Distance(smoke.Pos, Holder(g).Pos), 6);
        Assert.Equal(90, smoke.RotDeg, 6);
        Assert.Equal([Flag], Holder(g).Stowed!.Select(s => s.Id));
    }

    [Fact]
    public void Recalled_it_remembers_where_it_stood_and_comes_out_there_again_after_the_holder_moves_and_turns()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Smoke, Pos = new Vec2(10, 14), RotDeg = 30 });
        Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Assert.Equal(Readiness.In, State(g));
        Assert.Equal(Smoke, Holder(g).Stowed![0].Id);
        Ok(g, new MovePiece { Layer = U, Id = Battery, Pos = new Vec2(50, 50), RotDeg = 90 });
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        var smoke = g.Find(U, Smoke)!;
        var expected = new Vec2(50, 50) + ReadyPiece.Turned(new Vec2(0, 4), 90);
        Assert.Equal(expected.X, smoke.Pos.X, 6);
        Assert.Equal(expected.Z, smoke.Pos.Z, 6);
        Assert.Equal(120, smoke.RotDeg, 6);
        Assert.Equal(4, Distance(smoke.Pos, Holder(g).Pos), 6);
    }

    [Fact]
    public void Undoing_a_recall_puts_the_piece_back_out_and_forgets_the_new_place()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Smoke, Pos = new Vec2(10, 14), RotDeg = 30 });
        var undo = Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Ok(g, undo!);
        Assert.Equal(Readiness.Out, State(g));
        Assert.Equal(new Vec2(10, 14), g.Find(U, Smoke)!.Pos);
        Assert.Null(Holder(g).Ready!.Offset);
    }

    [Fact]
    public void Undoing_a_release_puts_it_back_first_in_the_holder()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        var undo = Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, undo!);
        Assert.Equal(Readiness.In, State(g));
        Assert.Equal([Smoke, Flag], Holder(g).Stowed!.Select(s => s.Id));
    }

    [Fact]
    public void A_released_piece_deleted_or_put_in_another_holder_is_gone_until_undone()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        var undo = Ok(g, new DeletePiece { Layer = U, Id = Smoke });
        Assert.Equal(Readiness.Gone, State(g));
        Assert.NotNull(StateMachine.Apply(g, new RecallReady { Layer = U, Holder = Battery }, out _));
        Assert.NotNull(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = Battery }, out _));
        Ok(g, undo!);
        Assert.Equal(Readiness.Out, State(g));
        Ok(g, new Stow { Layer = U, Holder = "p-000001", Id = Smoke });
        Assert.Equal(Readiness.Gone, State(g));
    }

    [Fact]
    public void Releasing_twice_or_recalling_what_is_in_is_refused()
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = Battery }, out _));
        Ok(g, MarkSmoke);
        Assert.NotNull(StateMachine.Apply(g, new RecallReady { Layer = U, Holder = Battery }, out _));
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Assert.NotNull(StateMachine.Apply(g, new ReleaseReady { Layer = U, Holder = Battery }, out _));
    }

    [Fact]
    public void The_ready_piece_is_saved_and_cloned_with_its_holder()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Smoke, Pos = new Vec2(10, 14), RotDeg = 30 });
        Ok(g, new RecallReady { Layer = U, Holder = Battery });
        var copy = Holder(g).Clone();
        Holder(g).Ready!.Turn = 0;
        Assert.Equal(30, copy.Ready!.Turn, 6);
        var back = Json.Deserialize<PieceInstance>(Json.Serialize(copy))!;
        Assert.Equal(Smoke, back.Ready!.Id);
        Assert.Equal(4, back.Ready.Offset!.Value.Z, 6);
        Assert.DoesNotContain("\"ready\"", Json.Serialize(CoreSamples.Piece("p-000200")), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copying_a_holder_takes_its_released_ready_piece_and_what_rides_on_it()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new Unstow { Layer = U, Holder = Battery, Id = Flag });
        Ok(g, new Attach { Layer = U, Parent = Smoke, Child = Flag });
        var copied = PieceCopy.WithContents([Holder(g), g.Find(U, Smoke)!], id => g.Find(U, id));
        Assert.Equal([Battery, Smoke, Flag], copied.Select(p => p.Id));
        Assert.NotSame(Holder(g), copied[0]);
    }

    [Fact]
    public void A_pasted_holder_holds_its_own_pieces_and_both_ready_pieces_come_out()
    {
        const string Outer = "p-000001";
        var g = Table();
        Ok(g, new Stow { Layer = U, Holder = Outer, Id = Battery });
        var outer = g.Find(U, Outer)!;
        Ok(g, new SetReady { Layer = U, Holder = Outer, Ready = new ReadyPiece { Id = Battery } });
        outer.Stowed![0].Ready = new ReadyPiece { Id = Smoke, Turn = 15 };
        outer.Stowed[0].Stowed![0].Attached.Add(Smoke);
        int n = 0;
        var copy = outer.Clone();
        copy.Id = "x-copy";
        PieceCopy.FreshHeld(copy, () => $"x-{++n}");
        var battery = copy.Stowed!.Single();
        Assert.Equal("x-1", battery.Id);
        Assert.Equal("x-1", copy.Ready!.Id);
        Assert.Equal(["x-2", "x-3"], battery.Stowed!.Select(s => s.Id));
        Assert.Equal("x-3", battery.Ready!.Id);
        Assert.Equal(15, battery.Ready.Turn);
        Assert.Equal(["x-3"], battery.Stowed![0].Attached);
        Assert.Equal(Battery, outer.Stowed[0].Id);
        Assert.Equal(Smoke, outer.Stowed[0].Ready!.Id);

        Ok(g, new RestorePiece { Layer = U, Piece = copy });
        Ok(g, new ReleaseReady { Layer = U, Holder = Outer });
        Ok(g, new ReleaseReady { Layer = U, Holder = "x-copy" });
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new ReleaseReady { Layer = U, Holder = "x-1" });
        Assert.NotNull(g.Find(U, Smoke));
        Assert.NotNull(g.Find(U, "x-3"));
    }

    [Fact]
    public void A_held_holder_copied_drops_a_ready_piece_it_had_let_out()
    {
        const string Outer = "p-000001";
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new Stow { Layer = U, Holder = Outer, Id = Battery });
        var copy = g.Find(U, Outer)!.Clone();
        int n = 0;
        PieceCopy.FreshHeld(copy, () => $"x-{++n}");
        Assert.Null(copy.Stowed![0].Ready);
        Assert.Equal(Smoke, g.Find(U, Outer)!.Stowed![0].Ready!.Id);
    }

    static double Distance(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
