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
    public void Released_it_rides_its_holder_through_moves_and_turns_and_comes_back_to_the_same_spot()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Assert.Contains(Smoke, Holder(g).Attached);
        Ok(g, new MovePiece { Layer = U, Id = Battery, Pos = new Vec2(-20, 5), RotDeg = 90 });
        var smoke = g.Find(U, Smoke)!;
        var expected = new Vec2(-20, 5) + ReadyPiece.Turned(new Vec2(ReadyPiece.Beside, 0), 90);
        Assert.Equal(expected.X, smoke.Pos.X, 6);
        Assert.Equal(expected.Z, smoke.Pos.Z, 6);
        Assert.Equal(90, smoke.RotDeg, 6);
        Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Assert.DoesNotContain(Smoke, Holder(g).Attached);
        Assert.Equal(ReadyPiece.Beside, Holder(g).Ready!.Offset!.Value.X, 6);
        Assert.Equal(0, Holder(g).Ready!.Offset!.Value.Z, 6);
        Assert.Equal(0, Holder(g).Ready!.Turn, 6);
    }

    [Fact]
    public void Undoing_a_release_unhooks_it_and_undoing_a_recall_hooks_it_on_again()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        var unrelease = Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, unrelease!);
        Assert.DoesNotContain(Smoke, Holder(g).Attached);
        Assert.Equal(Readiness.In, State(g));
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        var unrecall = Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Ok(g, unrecall!);
        Assert.Equal(Readiness.Out, State(g));
        Assert.Contains(Smoke, Holder(g).Attached);
    }

    [Fact]
    public void Only_ready_pieces_out_on_the_table_are_named_with_their_holders()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Assert.Empty(ReadyPiece.OutHolders(g.Layer(U)));
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Assert.Equal(Battery, Assert.Single(ReadyPiece.OutHolders(g.Layer(U))).Value);
        Assert.True(ReadyPiece.OutHolders(g.Layer(U)).ContainsKey(Smoke));
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

    [Fact]
    public void Taken_out_by_hand_the_ready_piece_rides_its_holder_and_put_back_it_is_unhooked()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        var back = Ok(g, new Unstow { Layer = U, Holder = Battery, Id = Smoke, Pos = new Vec2(30, 30) });
        Assert.Contains(Smoke, Holder(g).Attached);
        Assert.Equal(Readiness.Out, State(g));
        Ok(g, new MovePiece { Layer = U, Id = Battery, Pos = new Vec2(20, 10), RotDeg = 0 });
        Assert.Equal(40, g.Find(U, Smoke)!.Pos.X, 6);
        Ok(g, back!);
        Assert.DoesNotContain(Smoke, Holder(g).Attached);
        Assert.Equal(Readiness.In, State(g));
        Ok(g, new Unstow { Layer = U, Holder = Battery, Id = Flag, Pos = new Vec2(-5, -5) });
        Assert.DoesNotContain(Flag, Holder(g).Attached);
    }

    [Fact]
    public void An_out_ready_piece_cannot_be_detached_or_put_on_another_piece_but_others_still_ride_on_it()
    {
        const string Cart = "p-000104";
        var g = Table();
        g.Layer(U).Add(CoreSamples.Piece(Cart, x: 30, z: 30));
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Assert.NotNull(StateMachine.Apply(g, new Detach { Layer = U, Child = Smoke }, out _));
        Assert.NotNull(StateMachine.Apply(g, new Attach { Layer = U, Parent = Cart, Child = Smoke }, out _));
        Assert.NotNull(StateMachine.Apply(g, new Batch { Items = [new MovePiece { Layer = U, Id = Cart, Pos = new Vec2(31, 30) }, new Detach { Layer = U, Child = Smoke }] }, out _));
        Assert.Equal(30, g.Find(U, Cart)!.Pos.X, 6);
        Assert.Contains(Smoke, Holder(g).Attached);
        Ok(g, new Attach { Layer = U, Parent = Battery, Child = Smoke });
        Ok(g, new Attach { Layer = U, Parent = Smoke, Child = Cart });
        Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Assert.Null(g.Find(U, Cart));
    }

    [Fact]
    public void Opening_a_game_hooks_a_ready_piece_an_older_version_left_loose_on_its_holder()
    {
        var g = Table();
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Holder(g).Attached.Remove(Smoke);
        StateMachine.HookReadyPieces(g);
        Assert.Contains(Smoke, Holder(g).Attached);
        Assert.Single(g.Layer(U), p => p.Attached.Contains(Smoke));
    }

    static Command Look(int n) => new SetState { Layer = U, Id = Battery, State = n };

    static void At(GameState g, string id, double x, double z)
    {
        var p = g.Find(U, id);
        Assert.NotNull(p);
        Assert.Equal(x, p!.Pos.X, 6);
        Assert.Equal(z, p.Pos.Z, 6);
        Assert.Contains(id, Holder(g).Attached);
    }

    [Fact]
    public void Each_look_brings_out_its_own_ready_piece_where_it_stood_when_that_look_was_left()
    {
        var g = Table();
        Holder(g).State = 0;
        Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 0, Ready = new ReadyPiece { Id = Smoke } });
        Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 1, Ready = new ReadyPiece { Id = Flag } });
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Smoke, Pos = new Vec2(14, 10) });
        Ok(g, Look(1));
        Assert.Null(g.Find(U, Smoke));
        Assert.Null(g.Find(U, Flag));
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Flag, Pos = new Vec2(10, 13), RotDeg = 45 });
        var undo = Ok(g, Look(0));
        At(g, Smoke, 14, 10);
        Assert.Null(g.Find(U, Flag));
        Ok(g, new MovePiece { Layer = U, Id = Battery, Pos = new Vec2(0, 0), RotDeg = 90 });
        Ok(g, Look(1));
        Assert.Null(g.Find(U, Smoke));
        var flag = ReadyPiece.Turned(new Vec2(0, 3), 90);
        At(g, Flag, flag.X, flag.Z);
        Assert.Equal(135, g.Find(U, Flag)!.RotDeg, 6);
        Ok(g, Look(0));
        Ok(g, new MovePiece { Layer = U, Id = Battery, Pos = new Vec2(10, 10), RotDeg = 0 });
        Ok(g, Look(1));
        Ok(g, undo!);
        At(g, Flag, 10, 13);
    }

    [Fact]
    public void Looks_without_their_own_ready_piece_share_the_plain_one_and_a_held_one_stays_held()
    {
        var g = Table();
        Holder(g).State = 0;
        Ok(g, MarkSmoke);
        Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 2, Ready = new ReadyPiece { Id = Flag } });
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, Look(1));
        At(g, Smoke, 12, 10);
        Ok(g, Look(2));
        Assert.Null(g.Find(U, Smoke));
        Assert.Null(g.Find(U, Flag));
        Ok(g, Look(0));
        At(g, Smoke, 12, 10);
        Ok(g, new RecallReady { Layer = U, Holder = Battery });
        Ok(g, Look(2));
        Ok(g, Look(1));
        Assert.Null(g.Find(U, Smoke));
    }

    [Fact]
    public void A_look_s_ready_mark_is_set_and_cleared_on_its_own_and_undone()
    {
        var g = Table();
        Assert.NotNull(StateMachine.Apply(g, new SetReady { Layer = U, Holder = Battery, ForState = -1, Ready = new ReadyPiece { Id = Smoke } }, out _));
        var unmark = Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 3, Ready = new ReadyPiece { Id = Smoke } });
        Assert.Null(Holder(g).Ready);
        Assert.Equal(Smoke, Holder(g).ReadyFor(3)!.Id);
        Assert.Null(Holder(g).ReadyNow);
        var restore = Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 3 });
        Assert.Null(Holder(g).ReadyIn);
        Ok(g, restore!);
        Assert.Equal(Smoke, Holder(g).ReadyIn![3].Id);
        Ok(g, unmark!);
        Assert.Null(Holder(g).ReadyIn);
        var copy = Holder(g).Clone();
        copy.ReadyIn = new() { [1] = new ReadyPiece { Id = Flag, WasOut = true } };
        Assert.True(Json.Clone(copy).ReadyIn![1].WasOut);
    }

    [Fact]
    public void Undoing_a_move_that_pressed_a_carried_holder_down_puts_its_out_ready_piece_back_where_it_was()
    {
        const string Cart = "p-000104";
        var g = Table();
        g.Layer(U).Add(CoreSamples.Piece(Cart, x: 10, z: 10));
        var cart = g.Find(U, Cart)!;
        cart.Y = 2;
        Ok(g, MarkSmoke);
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Holder(g).Y = 1;
        g.Find(U, Smoke)!.Y = 3;
        var smokeAt = g.Find(U, Smoke)!.Pos;
        Ok(g, new Attach { Layer = U, Parent = Cart, Child = Battery });
        var undo = Ok(g, new MovePiece { Layer = U, Id = Cart, Pos = cart.Pos, RotDeg = cart.RotDeg, Y = 0 });
        Assert.Equal(0, Holder(g).Y, 6);
        Assert.Equal(1, g.Find(U, Smoke)!.Y, 6);
        Ok(g, undo!);
        Assert.Equal(2, g.Find(U, Cart)!.Y, 6);
        Assert.Equal(1, Holder(g).Y, 6);
        Assert.Equal(3, g.Find(U, Smoke)!.Y, 6);
        At(g, Smoke, smokeAt.X, smokeAt.Z);
    }

    [Fact]
    public void One_piece_ready_in_two_looks_comes_out_at_each_look_s_own_spot()
    {
        var g = Table();
        Holder(g).State = 0;
        Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 0, Ready = new ReadyPiece { Id = Smoke } });
        Ok(g, new SetReady { Layer = U, Holder = Battery, ForState = 1, Ready = new ReadyPiece { Id = Smoke } });
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, Look(1));
        Ok(g, new ReleaseReady { Layer = U, Holder = Battery });
        Ok(g, new MovePiece { Layer = U, Id = Smoke, Pos = new Vec2(10, 14) });
        Ok(g, Look(0));
        At(g, Smoke, 12, 10);
        Ok(g, Look(1));
        At(g, Smoke, 10, 14);
        var undo = Ok(g, Look(0));
        At(g, Smoke, 12, 10);
        Ok(g, undo!);
        At(g, Smoke, 10, 14);
    }

    static double Distance(Vec2 a, Vec2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
