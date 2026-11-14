using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public sealed class ScriptHearingTests
{
    const string U = Layers.Umpire, P = "p-000002", M = "p-000003";

    [Fact]
    public void A_script_that_starts_listening_part_way_through_a_batch_hears_every_drop_after()
    {
        using var t = new Table();
        t.Ok(Table.Host, new SetScript
        {
            Layer = U, Id = P,
            Text = "heard = 0\nfunction on_drop() function __module_any_on_drop() heard = heard + 1 self.set_counter('heard', heard) end end",
        });
        var moves = new Batch
        {
            Items =
            {
                new MovePiece { Layer = U, Id = M, Pos = new Vec2(1, 1) }, new MovePiece { Layer = U, Id = P, Pos = new Vec2(2, 2) },
                new MovePiece { Layer = U, Id = M, Pos = new Vec2(3, 3) }, new MovePiece { Layer = U, Id = M, Pos = new Vec2(4, 4) },
            },
        };
        t.Ok(Table.Ump, moves);
        Assert.Equal(3, t.Server.State.Find(U, P)!.Counters["heard"]);
    }
}
