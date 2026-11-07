using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class StickyTests
{
    const string U = Layers.Umpire;
    const double OnTop = 0.17;

    static async Task Carry(TestRunner r, OfflineTable t, string id, Vec2 from, Vec2 to)
    {
        t.Table.Selection.Set(new[] { id });
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(10);
    }

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    [NodeTest]
    static async Task A_piece_resting_on_another_rides_with_it_only_while_that_one_is_sticky(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(1, 0), Stack = 1, Y = OnTop });
        await r.Frames(5);
        await Carry(r, t, "p1", new Vec2(1, 0), new Vec2(1, 4));
        Check.That((At(t, "p2") - new Vec2(1, 4)).Length < 0.3, $"rides along while sticky: {At(t, "p2")}");

        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.NotSticky, Value = true });
        await r.Frames(2);
        await Carry(r, t, "p1", new Vec2(1, 4), new Vec2(6, 4));
        Check.That((At(t, "p1") - new Vec2(6, 4)).Length < 0.3, $"the lower one moved: {At(t, "p1")}");
        Check.That((At(t, "p2") - new Vec2(1, 4)).Length < 0.3, $"stays where it lay once the lower one is not sticky: {At(t, "p2")}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
