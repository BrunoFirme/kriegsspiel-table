using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceHighlightTests
{
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
