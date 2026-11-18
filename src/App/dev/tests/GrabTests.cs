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

    const double SlabThickness = 0.5, Snug = 0.05;

    static void Slabs(PackageStore store) =>
        store.RegisterDef(new PackManifest { Id = "slabs", Name = "Slabs" }, new PieceDef { Id = "slab", Name = "Slab", Size = new Size2(2, 2), Thickness = SlabThickness });

    static double MiddleSurface(OfflineTable t, PieceNode n)
    {
        var p = n.Piece;
        double rad = p.RotDeg * System.Math.PI / 180, w = n.Footprint.X, d = n.Footprint.Y;
        var along = new Vec2(-System.Math.Cos(rad), System.Math.Sin(rad));
        var back = new Vec2(-System.Math.Sin(rad), -System.Math.Cos(rad));
        var middle = n.MiddleAt(p.Pos, p.RotDeg);
        var above = t.S.View.Layer(U).Where(o => o.Y > p.Y + Snug).Select(o => t.Node(o.Id)).ToHashSet();
        double top = 0;
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                top = System.Math.Max(top, t.Table.Hand.SurfaceY(middle + new Vec2(along.X * i * w * 0.25 + back.X * j * d * 0.25, along.Z * i * w * 0.25 + back.Z * j * d * 0.25), U, n, above));
        return top;
    }

    [NodeTest]
    static async Task A_flicked_piece_and_the_ones_it_shoves_rest_on_what_is_under_their_middles(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, prepare: Slabs, arrange: g => g.Layers[U].AddRange(Enumerable.Range(0, 6)
            .Select(i => new PieceInstance { Id = "s" + i, Def = "slab", Pos = new Vec2(4 + i * 2.05, i % 2 * 0.9) })));
        await r.Frames(3);
        t.Table.Selection.Set(new[] { "p1" });
        const double FlickSpeed = 20, FlickFor = 0.06;
        double from = Godot.Time.GetTicksMsec() / 1000.0, gone;
        int moves = 0;
        t.Table.Hand.Start(new Vec2(1, 0));
        do
        {
            await r.Frame();
            gone = Godot.Time.GetTicksMsec() / 1000.0 - from;
            t.Table.Hand.Move(new Vec2(1 + FlickSpeed * gone, 0));
            moves++;
        } while (gone < FlickFor || moves < 3);
        t.Table.Hand.Drop();
        await r.Seconds(2.5);
        var flicked = t.S.View.Find(U, "p1")!;
        Check.That(flicked.Pos.X > 3, $"the flick carried the piece on: {flicked.Pos}");
        foreach (var p in t.S.View.Layer(U))
        {
            var n = t.Node(p.Id);
            Check.That(System.Math.Abs(p.Y - MiddleSurface(t, n)) < Snug, $"{p.Id} at y {p.Y} rests on what is under its middle, {MiddleSurface(t, n)}");
            Check.That(System.Math.Abs(n.Position.Y - n.BaseY(p)) < 0.01f, $"{p.Id} is drawn where it lies: {n.Position.Y} against {n.BaseY(p)}");
        }
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }
}
