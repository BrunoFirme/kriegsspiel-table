using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceHighlightTests
{
    const int Crowd = 300;

    [NodeTest]
    static async Task Picked_pieces_wear_a_ring_only_where_it_shows(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: Crowd);
        await r.Frames(2);
        if (t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        await r.Frames(2);
        t.Table.SetSelection(t.S.View.Layer(Layers.Umpire).Select(p => p.Id));
        var nodes = t.Table.Pieces.Nodes.ToList();
        Check.That(t.Table.RingedCount() == Crowd && nodes.All(n => n.Masked), $"every piece picked is in the aura's mask: {t.Table.RingedCount()} of {Crowd} picked");
        Check.That(nodes.All(n => n.SelectionMesh == null), "seen in 3D none carries a ring that would not show");
        t.Table.Rig.ToggleTopDown();
        await r.Frames(2);
        Check.That(nodes.All(n => n.SelectionMesh != null), "seen from above each has its ring");
        t.Table.SetSelection([]);
        Check.That(t.Table.RingedCount() == 0 && nodes.All(n => n.SelectionMesh == null && !n.Masked), "and none is left when nothing is picked");
        t.Table.QueueFree();
    }

    [NodeTest]
    static async Task A_highlight_rings_the_piece_and_a_piece_hands_cannot_take_is_passed_over_by_the_pointer(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1);
        await r.Frames(2);
        var node = t.Table.Pieces.Nodes.First();
        var id = node.Piece.Id;
        t.S.Do(new SetHighlight { Layer = Layers.Umpire, Id = id, Color = "#DA1A18" });
        await r.Frames(1);
        Check.That(node.Lit, "a highlighted piece wears a ring");
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos) == node, "the pointer finds a piece hands can take");
        t.S.Do(new SetPieceOption { Layer = Layers.Umpire, Id = id, Name = PieceOptions.NotInteractable, Value = true });
        await r.Frames(1);
        Check.That(t.Table.Pieces.Pick(node.Piece.Pos) == null, "the pointer passes over a piece hands cannot take");
        t.S.Do(new SetHighlight { Layer = Layers.Umpire, Id = id, Color = null });
        await r.Frames(1);
        Check.That(!node.Lit, "the ring goes when the highlight does");
        t.Table.QueueFree();
    }
}
