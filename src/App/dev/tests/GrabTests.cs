using System.Linq;
using System.Threading.Tasks;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class GrabTests
{
    const string U = Layers.Umpire;
    const double OnTop = 0.17;

    static int Moves(OfflineTable t) =>
        t.Server.Log.SelectMany(c => c is Batch b ? b.Items : new() { c }).Count(c => c is MovePiece);

    [NodeTest]
    static async Task A_pressed_piece_lifts_at_once_and_a_click_sets_it_back_where_it_lay(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(3);
        var node = t.Node("p1");
        var at = t.S.View.Find(U, "p1")!.Pos;
        float restY = node.Position.Y;
        int moves = Moves(t);
        t.Table.Selection.Set(new[] { "p1" });
        t.Table.Hand.Lift(at);
        await r.Frames(2);
        Check.That(t.Table.Hand.Holding, "the press takes the piece in hand");
        Check.That(node.Position.Y > restY + 1, $"lifted before any move: {node.Position.Y} over {restY}");
        t.Table.Hand.Drop();
        await r.Seconds(0.5);
        Check.That(!t.Table.Hand.Holding, "the release lets it go");
        Check.Equal(at, t.S.View.Find(U, "p1")!.Pos, "a click leaves the piece where it lay");
        Check.That(System.Math.Abs(node.Position.Y - restY) < 0.01f, $"set back down: {node.Position.Y} vs {restY}");
        Check.Equal(moves, Moves(t), "a click sends no move");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_marker_carried_with_the_piece_under_it_in_one_selection_lands_on_top_of_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 2);
        await r.Frame();
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(1, 0), Stack = 1, Y = OnTop });
        await r.Frames(5);
        t.Table.Selection.Set(new[] { "p2", "p1" });
        t.Table.Hand.Start(new Vec2(1, 0));
        t.Table.Hand.Move(new Vec2(1, 4));
        await r.Seconds(0.25);
        t.Table.Hand.Drop();
        await r.Frames(10);
        var top = t.S.View.Find(U, "p2")!;
        Check.That((top.Pos - new Vec2(1, 4)).Length < 0.3, $"the marker came along: {top.Pos}");
        Check.That(System.Math.Abs(top.Y - OnTop) < 0.05, $"the marker still sits on top: y {top.Y}");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
