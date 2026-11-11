using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PieceTypeListTests
{
    const string U = Layers.Umpire;
    const string Tree = "scenery.tree";

    static void Pick(PieceIndex index, string id)
    {
        var at = Enumerable.Range(0, index.Types.ItemCount).First(i => index.Types.GetItemMetadata(i).AsString() == id);
        index.Types.Select(at);
        index.Types.EmitSignal(OptionButton.SignalName.ItemSelected, at);
    }

    static string Choices(PieceIndex index) =>
        string.Join("|", Enumerable.Range(0, index.Types.ItemCount).Select(index.Types.GetItemText).Where(t => t.Length > 0));

    [NodeTest]
    static async Task On_the_table_shows_one_type_of_piece_or_every_type_under_its_own_heading(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3,
            prepare: store => store.RegisterDef(new PackManifest { Id = "scenery", Name = "Scenery" }, new PieceDef { Id = Tree, Name = "Tree", Tags = new() { "prop" } }));
        IHudShell shell = h.Hud;
        shell.TogglePanel("pieces");
        h.Hud.Pieces.ShowIndex();
        await r.Frames(3);
        var index = h.Hud.Pieces.Index;
        index.Rebuild();
        Check.That(!index.Types.Visible, "with one type on the table there is nothing to pick");

        h.S.Do(new SetDef { Layer = U, Id = "p3", Def = Tree });
        await r.Frames(2);
        index.Rebuild();
        index.Nest(index.NestCommands(new() { (U, "p2") }, index.ItemOf(U, "p3"), between: false));
        await r.Frames(2);
        index.Rebuild();
        Check.That(index.Types.Visible, "a prop beside the pieces brings the type picker");
        Check.Equal("Every type (3)|Grouped by type|Pieces (2)|Props (1)", Choices(index), "the choices count each type");

        Pick(index, "Prop");
        await r.Frames(2);
        Check.That(index.ItemOf(U, "p3") != null && index.ItemOf(U, "p1") == null && index.ItemOf(U, "p2") == null, "only the tree is listed");

        Pick(index, "grouped");
        await r.Frames(2);
        var props = index.ItemOf(U, "p3")?.GetParent();
        var pieces = index.ItemOf(U, "p1")?.GetParent();
        Check.Equal("PROPS  ·  2", props?.GetText(0), "the tree heads its branch under Props");
        Check.Equal("PIECES  ·  1", pieces?.GetText(0), "the lone piece is under Pieces");
        Check.That(props!.GetCustomColor(0) == Ui.Muted && props.GetCustomFontSize(0) < index.Rows.GetThemeFontSize("font_size"), "a heading is drawn as a section head, small and muted");
        Check.That(index.ItemOf(U, "p2")?.GetParent() == index.ItemOf(U, "p3"), "the piece riding on the tree stays under it");
        Check.That(!props!.IsSelectable(0), "a heading is not a piece to pick");
        Check.That(!index.CanDrop(index.Rows.GetItemAreaRect(pieces!).GetCenter(), PieceIndex.DragData(new[] { (U, "p2") })), "a row dropped on a heading does nothing");

        props.Collapsed = true;
        await r.Frames(1);
        index.Rebuild();
        Check.That(index.ItemOf(U, "p3")?.GetParent()?.Collapsed == true, "a folded heading stays folded");

        h.S.Do(new SetDef { Layer = U, Id = "p3", Def = "x" });
        await r.Frames(2);
        index.Rebuild();
        Check.That(!index.Types.Visible && index.ItemOf(U, "p2")?.GetParent() == index.ItemOf(U, "p3"), "back to one type, the plain list returns");
    }
}
