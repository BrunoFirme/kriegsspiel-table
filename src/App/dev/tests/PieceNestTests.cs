using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceNestTests
{
    const string U = Layers.Umpire;

    static bool Under(PieceIndex index, string child, string parent) => index.ItemOf(U, child)?.GetParent() is { } up && up == index.ItemOf(U, parent);

    [NodeTest]
    static async Task Rows_dragged_onto_a_row_nest_under_it_and_ride_with_it_down_the_whole_branch(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        var index = h.Hud.Pieces.Index;
        index.Rebuild();
        index.Nest(index.NestCommands(new() { (U, "p2") }, index.ItemOf(U, "p1"), between: false));
        await r.Frames(2);
        index.Rebuild();
        index.Nest(index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p2"), between: false));
        await r.Frames(2);
        index.Rebuild();
        Check.That(Under(index, "p2", "p1") && Under(index, "p3", "p2"), "p3 under p2 under p1");
        Check.Equal(0, index.NestCommands(new() { (U, "p1") }, index.ItemOf(U, "p3"), between: false).Count, "nothing nests under what it carries");
        Check.Equal(0, index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p3"), between: true).Count, "dropped beside itself it stays put");

        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = new Vec2(11, 0) });
        await r.Frames(2);
        Check.Equal(new Vec2(13, 0), h.S.View.Find(U, "p3")!.Pos, "the grandchild rode with p1");
        h.S.Do(new MovePiece { Layer = U, Id = "p3", Pos = new Vec2(13, 6) });
        await r.Frames(2);
        Check.Equal(new Vec2(11, 0), h.S.View.Find(U, "p1")!.Pos, "a child moved alone leaves its parent where it is");

        index.Nest(index.NestCommands(new() { (U, "p3") }, index.ItemOf(U, "p2"), between: true));
        await r.Frames(2);
        index.Rebuild();
        Check.That(Under(index, "p3", "p1"), "dropped between rows, it joins p2's parent");
        index.Nest(index.NestCommands(new() { (U, "p2"), (U, "p3") }, null, between: false));
        await r.Frames(2);
        index.Rebuild();
        Check.That(index.ItemOf(U, "p2")?.GetParent() == index.Rows.GetRoot() && index.ItemOf(U, "p3")?.GetParent() == index.Rows.GetRoot(), "dropped clear of the rows, both stand alone");
        Check.Equal(0, h.S.View.Find(U, "p1")!.Attached.Count, "p1 carries nothing");
    }

    [NodeTest]
    static async Task A_piece_attached_from_afar_moved_by_hand_alone_stays_attached_and_its_parent_still_carries_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2, arrange: g => g.Find(U, "p2")!.Pos = new Vec2(8, 0));
        await r.Frame();
        t.S.Do(new Attach { Layer = U, Parent = "p1", Child = "p2" });
        await r.Frames(5);
        await Carry(r, t, "p2", new Vec2(8, 0), new Vec2(8, 5));
        Check.Equal(new Vec2(1, 0), t.S.View.Find(U, "p1")!.Pos, "the parent stays");
        Check.That(t.S.View.Find(U, "p1")!.Attached.Contains("p2"), "still attached after moving alone");
        await Carry(r, t, "p1", new Vec2(1, 0), new Vec2(1, 3));
        Check.That((t.S.View.Find(U, "p2")!.Pos - new Vec2(8, 8)).Length < 0.3, $"the parent carries it: {t.S.View.Find(U, "p2")!.Pos}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    static async Task Carry(TestRunner r, OfflineTable t, string id, Vec2 from, Vec2 to)
    {
        t.Table.Selection.Set(new[] { id });
        t.Table.Hand.Start(from);
        t.Table.Hand.Move(to);
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(10);
    }
}
