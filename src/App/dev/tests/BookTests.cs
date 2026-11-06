using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BookTests
{
    const string U = Layers.Umpire;
    const string BookDef = "books.manual";

    static void WriteBook(PackageStore store, int pages)
    {
        var pack = new PackManifest { Id = "books", Name = "Books" };
        var dir = store.PackageDir("books");
        Directory.CreateDirectory(Path.Combine(dir, "assets"));
        var def = new PieceDef { Id = BookDef, Name = "Manual", Kind = "flat", Size = new Size2(3, 4), Thickness = 0.12, Tags = new() { "book" } };
        for (int i = 1; i <= pages; i++)
        {
            var img = Image.CreateEmpty(8, 10, false, Image.Format.Rgba8);
            img.Fill(new Color(i / 10f, 0.5f, 0.5f));
            var sha = $"page{i}";
            img.SavePng(Path.Combine(dir, "assets", sha + ".png"));
            pack.Assets[sha] = new AssetEntry { File = $"assets/{sha}.png", Kind = "image", W = 8, H = 10 };
            def.States.Add(new PieceStateDef { Name = $"Page {i}", Image = sha });
        }
        def.Image = def.States[0].Image;
        pack.Pieces.Add(def);
        KTable.Core.Json.Save(Path.Combine(dir, "pack.json"), pack);
        store.RegisterDef(pack, def);
    }

    static Window? Reader(Node parent) => parent.GetChildren().OfType<Window>().FirstOrDefault();

    static Label? Where(Node w) => w.GetChildren().OfType<Node>()
        .SelectMany(Descendants).OfType<Label>().FirstOrDefault(l => l.Text.StartsWith("Page ", System.StringComparison.Ordinal));

    static System.Collections.Generic.IEnumerable<Node> Descendants(Node n)
    {
        yield return n;
        foreach (var c in n.GetChildren()) foreach (var d in Descendants(c)) yield return d;
    }

    static Button? Named(Node w, string text) => Descendants(w).OfType<Button>().FirstOrDefault(b => b.Text == text);

    [NodeTest]
    static async Task A_book_opens_where_the_piece_is_open_turns_its_pages_and_can_show_one_at_the_table(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, packs: new[] { "books" }, prepare: store => WriteBook(store, 4));
        t.S.Do(new SetDef { Layer = U, Id = "p1", Def = BookDef });
        t.S.Do(new SetState { Layer = U, Id = "p1", State = 1 });
        await r.Frames(2);
        var node = t.Node("p1");
        Check.That(BookWindow.Is(node.Def), "the piece is read as a book");

        BookWindow.Open(t.Table, t.S, node);
        await r.Frames(2);
        var w = Reader(t.Table) ?? throw new TestFailure("no reading window opened");
        Check.Equal("Page 2 of 4", Where(w)?.Text ?? "", "it opens on the page the piece is showing");

        var on = Named(w, "▶") ?? throw new TestFailure("no next-page button");
        var back = Named(w, "◀") ?? throw new TestFailure("no previous-page button");
        on.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frame();
        Check.Equal("Page 3 of 4", Where(w)?.Text ?? "", "▶ turns a page");
        Check.Equal(1, t.Table.Pieces.NodeOf(U, "p1")!.Piece.State, "reading alone does not turn the piece for everyone");

        for (int i = 0; i < 5; i++) { on.EmitSignal(BaseButton.SignalName.Pressed); await r.Frame(); }
        Check.Equal("Page 4 of 4", Where(w)?.Text ?? "", "it stops at the last page");
        Check.That(on.Disabled && !back.Disabled, "the last page offers only the way back");

        (Named(w, "Show on the table") ?? throw new TestFailure("no way to show the page at the table")).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(3, t.Table.Pieces.NodeOf(U, "p1")!.Piece.State, "the piece itself turns to the page being read");

        BookWindow.Open(t.Table, t.S, node);
        await r.Frames(2);
        Check.Equal(1, t.Table.GetChildren().OfType<Window>().Count(), "asking twice reopens the same window");
        w.QueueFree();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task A_book_carries_its_own_page_buttons_and_no_list_of_pages(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, packs: new[] { "books" }, prepare: store => WriteBook(store, 4));
        t.S.Do(new SetDef { Layer = U, Id = "p1", Def = BookDef });
        await r.Frames(2);
        var node = t.Node("p1");
        var w = node.Footprint.X;
        var h = node.Footprint.Y;
        float bs = Mathf.Min(h * 0.055f, w * 0.34f / 3), gap = bs * 0.35f, vw = bs * 2.4f;
        float z = h / 2 - bs * 0.75f - h * 0.05f;
        var on = Coords.FromLocal(new Vector2(vw / 2 + gap + bs / 2, z), node.Piece.Pos, node.Piece.RotDeg);
        var back = Coords.FromLocal(new Vector2(-(vw / 2 + gap + bs / 2), z), node.Piece.Pos, node.Piece.RotDeg);
        Check.Equal(1, node.PageButtonAt(on) ?? -1, "the forward button turns to page 2");
        Check.That(node.PageButtonAt(back) == null, "on page 1 there is nothing to turn back to");
        t.S.Do(new SetState { Layer = U, Id = "p1", State = 1 });
        await r.Frames(2);
        Check.Equal(0, node.PageButtonAt(back) ?? -1, "from page 2 the back button turns to page 1");
        Check.Equal(2, node.PageButtonAt(on) ?? -1, "and the forward button to page 3");
        Check.That(node.PageButtonAt(node.Piece.Pos) == null, "the middle of the page is not a button");
    }
}
