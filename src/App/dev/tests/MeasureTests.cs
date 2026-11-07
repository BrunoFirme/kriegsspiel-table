using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class MeasureTests
{
    const string U = Layers.Umpire;

    [NodeTest]
    static async Task A_piece_that_measures_its_move_shows_how_far_it_is_carried_until_it_is_set_down(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.MeasuresMoves, Value = true });
        await r.Frames(2);
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.Hand.Start(new Vec2(1, 0));
        t.Table.Hand.Move(new Vec2(4, 4));
        await r.Frames(3);
        var shown = t.Table.Labels.TextOf("moved:p1");
        Check.That(shown is { Length: > 0 } && shown.Contains('5'), $"the distance carried, 5 units: {shown}");
        Check.That(t.Table.Labels.TextOf("moved:p2") == null, "a piece not held shows nothing");
        t.Table.Hand.Drop();
        await r.Frames(10);
        Check.That(t.Table.Labels.TextOf("moved:p1") == null, "gone once it is set down");
    }
}
