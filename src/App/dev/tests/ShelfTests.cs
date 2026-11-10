using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;

namespace KTable.App;

static class ShelfTests
{
    static readonly string[] Three = { "a", "b", "c" };

    static ShelfShown Shown(ShelfView view, Control host, bool pack, IReadOnlyList<string> present, Action refresh) =>
        view.Begin(host, pack, present, view.Lib.Arranged(pack, view.At(pack), present), refresh, new TitlePicks(), id => id.ToUpperInvariant());

    static string Row(Library lib, IReadOnlyList<string> present, string folder = "") => string.Join(",", lib.Arranged(true, folder, present).Select(e => e.Key));

    [NodeTest]
    static async Task Cards_fill_each_row_evenly_and_a_row_is_as_tall_as_its_tallest_card(TestRunner r)
    {
        var grid = TitleStyle.Grid();
        r.AddChild(grid);
        grid.Size = new Vector2(1032, 0);
        var cards = Enumerable.Range(0, 5).Select(i => new PanelContainer { CustomMinimumSize = new Vector2(TitleStyle.CardWidth, i == 1 ? 180 : 100) }).ToList();
        foreach (var c in cards) grid.AddChild(c);
        await r.Frames(3);
        Check.Equal(4, grid.Columns, "four cards of 220 fit across 1032");
        Check.That(cards.Take(4).All(c => Math.Abs(c.Size.X - 249) < 0.5f), $"each takes a quarter of the row: {string.Join(", ", cards.Select(c => c.Size.X))}");
        Check.That(cards.Take(4).All(c => c.Size.Y == 180), "the first row is as tall as its tallest card");
        Check.Equal(180f + Ui.Gap.Block, cards[4].Position.Y, "the second row starts below it");
        Check.Equal(0f, cards[4].Position.X, "and at the left edge");
        grid.Size = new Vector2(600, 0);
        await r.Frames(3);
        Check.Equal(2, grid.Columns, "two fit across 600");
        Check.That(cards.All(c => Math.Abs(c.Size.X - 294) < 0.5f), "each takes half");
        grid.QueueFree();
        await r.Frame();
    }

    [NodeTest]
    static async Task A_mosaic_lays_one_to_four_pictures_without_overlap(TestRunner r)
    {
        var size = new Vector2(226, 130);
        for (int n = 1; n <= Mosaic.Most; n++)
        {
            var cells = Mosaic.Cells(n, size);
            Check.Equal(n, cells.Length, $"{n} cells for {n} pictures");
            for (int i = 0; i < cells.Length; i++)
                for (int j = i + 1; j < cells.Length; j++)
                    Check.That(!cells[i].Intersects(cells[j]), $"cells {i} and {j} of {n} do not overlap");
            Check.That(cells.All(c => new Rect2(Vector2.Zero, size).Encloses(c)), $"all {n} stay on the plate");
        }
        Check.Equal(130f, Mosaic.Cells(3, size)[0].Size.Y, "with three, the first stands the full height beside two");
        Check.That(Mosaic.Cells(0, size).Length == 0, "none for none");
        await r.Frame();
    }

    [NodeTest]
    static async Task A_card_dropped_on_a_folder_goes_in_and_beside_a_card_takes_that_place(TestRunner r)
    {
        var host = new VBoxContainer();
        r.AddChild(host);
        var lib = new Library();
        lib.AddFolder(true, "", "F");
        var view = new ShelfView(null!, lib);
        int refreshed = 0;
        ShelfShown Now() => Shown(view, host, pack: true, Three, () => refreshed++);
        Check.Equal("/F,a,b,c", Row(lib, Three), "a new folder leads");
        view.Drop(Now(), "/F", "F", 0.5f, new[] { "c" });
        Check.Equal("/F,a,b", Row(lib, Three), "the middle of a folder takes the card in");
        Check.Equal("c", Row(lib, Three, "F"), "into the folder");
        view.Drop(Now(), "a", null, 0.1f, new[] { "b" });
        Check.Equal("/F,b,a", Row(lib, Three), "the left half of a card puts it before");
        view.Drop(Now(), "/F", "F", 0.1f, new[] { "a" });
        Check.Equal("a,/F,b", Row(lib, Three), "the left edge of a folder puts it before the folder");
        view.Drop(Now(), "/F", "F", 0.5f, new[] { "/F" });
        Check.Equal("a,/F,b", Row(lib, Three), "a folder dropped on itself stays where it is");
        view.Drop(Now(), null, null, 0.9f, new[] { "b" });
        Check.Equal("b,a,/F", Row(lib, Three), "dropped on the new card it goes first");
        await r.Frame();
        Check.Equal(5, refreshed, "the page is drawn again after each drop");
        host.QueueFree();
        await r.Frame();
    }

    [NodeTest]
    static async Task Cards_dropped_on_the_trash_are_asked_about_and_folders_never_go_in(TestRunner r)
    {
        var host = new VBoxContainer();
        r.AddChild(host);
        var lib = new Library();
        lib.AddFolder(true, "", "F");
        var view = new ShelfView(null!, lib);
        var shelved = Three.Append(Shelf.TrashKey).ToList();
        IReadOnlyList<string>? asked = null;
        var s = Shown(view, host, pack: true, shelved, () => { }) with { Trash = keys => asked = keys };
        view.Drop(s, Shelf.TrashKey, Shelf.TrashKey, 0.5f, new[] { "a", "b" });
        await r.Frame();
        Check.Equal("a,b", string.Join(",", asked ?? []), "the delete question gets the cards");
        Check.Equal("/F,a,b,c,:trash", Row(lib, shelved), "nothing moved before the answer");
        asked = null;
        Check.That(ShelfView.Aim(s, Shelf.TrashKey, Shelf.TrashKey, 0.5f, new[] { "/F" }).into == null, "a folder is not taken by the trash");
        view.Drop(s, Shelf.TrashKey, Shelf.TrashKey, 0.5f, new[] { "/F" });
        await r.Frame();
        Check.That(asked == null, "so nothing is asked");
        host.QueueFree();
        await r.Frame();
    }

    [NodeTest]
    static async Task A_folder_tile_shows_up_to_four_pictures_of_what_is_inside_and_says_what_it_holds(TestRunner r)
    {
        var host = new VBoxContainer();
        r.AddChild(host);
        var lib = new Library();
        var five = new[] { "p1", "p2", "p3", "p4", "p5" };
        lib.AddFolder(true, "", "Full");
        lib.AddFolder(true, "", "Empty");
        lib.AddFolder(true, "", "Nest");
        lib.AddFolder(true, "Nest", "Inner");
        lib.MoveInto(true, "Full", five, five);
        var view = new ShelfView(null!, lib);
        var s = Shown(view, host, pack: true, five, () => { });
        var picture = ImageTexture.CreateFromImage(Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
        var asked = new List<string>();
        PanelContainer Tile(string folder)
        {
            var tile = view.FolderTile(s, new ShelfEntry(Shelf.KeyOf(folder), true), (id, show) => { asked.Add(id); show(picture); }, _ => { });
            host.AddChild(tile);
            return tile;
        }
        static string Line(Control tile) => tile.FindChildren("*", nameof(Label), true, false).OfType<Label>().Last().Text;
        var full = Tile("Full");
        var mosaic = full.FindChildren("*", "", true, false).OfType<Mosaic>().Single();
        Check.Equal(Mosaic.Most, mosaic.Count, "four of the five are shown");
        Check.Equal("p1,p2,p3,p4", string.Join(",", asked), "the first four in their order");
        Check.That(Enumerable.Range(0, Mosaic.Most).All(i => mosaic[i] == picture), "each cell got its picture");
        Check.Equal("5 packs", Line(full), "it counts what it holds");
        var empty = Tile("Empty");
        Check.That(!empty.FindChildren("*", "", true, false).OfType<Mosaic>().Any(), "an empty folder has no mosaic");
        Check.Equal("Empty", Line(empty), "and says so");
        Check.Equal("1 folder", Line(Tile("Nest")), "a folder of folders counts them");
        host.QueueFree();
        await r.Frame();
    }
}
