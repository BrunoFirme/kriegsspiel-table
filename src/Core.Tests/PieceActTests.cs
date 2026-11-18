using KTable.Core.Model;
using KTable.Core.Protocol;
using Xunit;

namespace KTable.Core.Tests;

public class PieceActTests
{
    const string U = Layers.Umpire;

    [Fact]
    public void An_act_is_stored_on_the_piece_and_undone_in_one_step()
    {
        var g = CoreSamples.State();
        var p = g.Find(U, "p-000001")!;
        Assert.Null(p.Act);
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = p.Id, Act = RigMotion.Kneel }, out var back));
        Assert.Equal(RigMotion.Kneel, p.Act);
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = p.Id, Act = RigMotion.Cheer }, out var toKneel));
        Assert.Null(StateMachine.Apply(g, toKneel!, out _));
        Assert.Equal(RigMotion.Kneel, p.Act);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Null(p.Act);
    }

    [Fact]
    public void An_unknown_act_or_piece_is_refused_and_changes_nothing()
    {
        var g = CoreSamples.State();
        Assert.NotNull(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = "dance" }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "nobody", Act = RigMotion.Sit }, out _));
        Assert.Null(g.Find(U, "p-000001")!.Act);
    }

    [Fact]
    public void A_piece_keeps_its_act_through_a_save_and_a_copy()
    {
        var g = CoreSamples.State();
        Assert.Null(StateMachine.Apply(g, new SetPieceAct { Layer = U, Id = "p-000001", Act = RigMotion.Lie }, out _));
        var again = Json.Deserialize<GameState>(Json.Serialize(g));
        Assert.Equal(RigMotion.Lie, again.Find(U, "p-000001")!.Act);
        Assert.Equal(RigMotion.Lie, g.Find(U, "p-000001")!.Clone().Act);
        Assert.DoesNotContain("\"act\"", Json.Serialize(g.Find(U, "p-000002")!));
    }
}
