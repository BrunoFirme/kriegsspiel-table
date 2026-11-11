using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GridSnapTests
{
    const string U = Layers.Umpire;
    const double Root3 = 1.7320508075688772;

    static async Task Carry(TestRunner r, OfflineTable t, string[] ids, Vec2 from, Vec2 to)
    {
        t.Table.Selection.Set(ids);
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(10);
    }

    static Vec2 At(OfflineTable t, string id) => t.S.View.Find(U, id)!.Pos;

    static bool Close(Vec2 a, Vec2 b) => (a - b).Length < 0.01;

    [NodeTest]
    static async Task With_snap_ticked_a_piece_set_down_lands_on_its_cell_centre_and_a_group_keeps_its_shape(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 10, Snap = true } });
        await r.Frames(2);
        await Carry(r, t, new[] { "p1" }, new Vec2(1, 0), new Vec2(13.2, 27.9));
        Check.That(Close(At(t, "p1"), new Vec2(15, 25)), $"on the cell's centre: {At(t, "p1")}");

        await Carry(r, t, new[] { "p1", "p2" }, new Vec2(15, 25), new Vec2(33, 44));
        Check.That(Close(At(t, "p1"), new Vec2(35, 45)), $"the grabbed piece on a centre: {At(t, "p1")}");
        Check.That(Close(At(t, "p2"), new Vec2(22, 20)), $"the other one moves by as much, off the centres: {At(t, "p2")}");

        t.S.Do(new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = 10, Snap = true } });
        await r.Frames(2);
        await Carry(r, t, new[] { "p1" }, new Vec2(35, 45), new Vec2(44, 47));
        Check.That(Close(At(t, "p1"), new Vec2(45, 25 * Root3)), $"on the nearest hex centre: {At(t, "p1")}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task Without_snap_a_piece_lands_where_it_is_set_down(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frame();
        t.S.Do(new SetGrid { Grid = new GridDef { Kind = "square", Pitch = 10 } });
        await r.Frames(2);
        await Carry(r, t, new[] { "p1" }, new Vec2(1, 0), new Vec2(13.2, 27.9));
        Check.That(Close(At(t, "p1"), new Vec2(13.2, 27.9)), $"where it was let go: {At(t, "p1")}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
