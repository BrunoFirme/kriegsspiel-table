using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;

namespace KTable.App;

static class ShelfFindTests
{
    static readonly string[] Three = { "a", "b", "c" };
    static readonly string[] Five = { "austerlitz", "borodino", "jena", "ulm", "wagram" };
    const string Napoleonic = "Napoleonic";

    static ShelfShown Shown(ShelfView view, Control host, bool pack, IReadOnlyList<string> present, Action refresh) =>
        view.Begin(host, pack, present, view.Lib.Arranged(pack, view.At(pack), present), refresh, new TitlePicks(), id => id.ToUpperInvariant());

    static string Row(Library lib, IReadOnlyList<string> present, string folder = "") => string.Join(",", lib.Arranged(true, folder, present).Select(e => e.Key));

    static string Seen(Control grid) => string.Join(",", grid.GetChildren().OfType<Label>().Where(l => l.Visible).Select(l => l.Text));

    static void Type(ShelfFind find, string text)
    {
        find.Box.Text = text;
        find.Box.EmitSignal(LineEdit.SignalName.TextChanged, text);
    }

    static (ShelfFind find, Control grid) Page(TestRunner r, ShelfView view, ShelfShown s, IEnumerable<string> home, List<(string id, string where)> made, List<Node> hosts)
    {
        var host = new VBoxContainer();
        r.AddChild(host);
        hosts.Add(host);
        var grid = TitleStyle.Grid();
        var find = new ShelfFind(view, s, ShelfSort.Yours, grid, (id, where) =>
        {
            made.Add((id, where));
            return new Label { Text = id };
        });
        host.AddChild(find.Box);
        host.AddChild(find.None);
        host.AddChild(grid);
        foreach (var key in home)
        {
            var card = new Label { Text = key };
            grid.AddChild(card);
            if (Five.Contains(key)) find.Card(key, card);
        }
        find.Ready();
        return (find, grid);
    }

    [NodeTest]
    static async Task Finding_shows_matches_from_every_folder_in_one_grid_without_drawing_the_page_again(TestRunner r)
    {
        var lib = new Library();
        lib.AddFolder(true, "", Napoleonic);
        lib.MoveInto(true, Napoleonic, Five, new[] { "borodino", "wagram" });
        var view = new ShelfView(null!, lib, new Settings());
        var shelved = Five.Append(Shelf.TrashKey).ToList();
        int refreshed = 0;
        var holder = new VBoxContainer();
        r.AddChild(holder);
        var hosts = new List<Node> { holder };
        var s = Shown(view, holder, pack: true, shelved, () => refreshed++);
        var made = new List<(string id, string where)>();
        var home = new[] { "+", "/" + Napoleonic, "austerlitz", "jena", "ulm", Shelf.TrashKey };
        var (find, grid) = Page(r, view, s, home, made, hosts);
        Check.Equal("+,/Napoleonic,austerlitz,jena,ulm,:trash", Seen(grid), "with nothing to find the page is as it was built");
        Check.Equal("Find among 5", find.Box.PlaceholderText, "the box says how many it looks through");

        Type(find, "napoleonic");
        Check.Equal("borodino,wagram", Seen(grid), "a folder's name finds what it holds, though that folder is not open");
        Check.Equal("borodino Napoleonic,wagram Napoleonic", string.Join(",", made.Select(m => $"{m.id} {m.where}")), "their cards are made once, saying where they are");
        Check.That(!s.ByHand(), "while finding, cards are not reordered by hand");

        Type(find, "JENA");
        Check.Equal("jena", Seen(grid), "a card of the open folder is found as it is");
        Type(find, "borodino");
        Check.Equal("borodino", Seen(grid), "a card made before is shown again");
        Check.Equal(2, made.Count, "and not made twice");

        Type(find, "zzz");
        Check.Equal("", Seen(grid), "nothing matches");
        Check.That(find.None.Visible, "and the page says so");

        Type(find, " ");
        Check.Equal("+,/Napoleonic,austerlitz,jena,ulm,:trash", Seen(grid), "an empty box puts the page back as it was, in its order");
        Check.That(!find.None.Visible && s.ByHand(), "with the hand order back");

        Type(find, "jena");
        var (again, regrid) = Page(r, view, Shown(view, holder, pack: true, shelved, () => refreshed++), home, made, hosts);
        Check.Equal("jena", again.Box.Text, "a page drawn again keeps the search");
        Check.Equal("jena", Seen(regrid), "and shows what it finds at once");

        view.Find(true, "");
        view.Go(true, Napoleonic);
        var inside = Shown(view, holder, pack: true, Five, () => refreshed++);
        var (fromFolder, _) = Page(r, view, inside, new[] { "borodino", "wagram" }, made, hosts);
        Type(fromFolder, "ulm");
        Check.Equal("ulm Packs", $"{made[^1].id} {made[^1].where}", "from inside a folder a card at the top says it is in Packs");

        await r.Frame();
        Check.Equal(0, refreshed, "typing never draws the page again");
        view.Find(true, "");
        view.Go(true, "");
        foreach (var h in hosts) h.QueueFree();
        await r.Frame();
    }

    [NodeTest]
    static async Task The_module_list_finds_by_name_or_id_and_lists_newest_first_or_by_name(TestRunner r)
    {
        var newestFirst = new[] { ("Wagram 1809", "300"), ("austerlitz", "100"), ("Borodino", "200") }.Select(m => new ModuleEntry { Name = m.Item1, Id = m.Item2 }).ToList();
        static string Names(IEnumerable<ModuleEntry> list) => string.Join(",", list.Select(e => e.Name));
        Check.Equal("Wagram 1809,austerlitz,Borodino", Names(ImportPage.Listed(newestFirst, "", ShelfOrder.Newest)), "newest first is the order the modules came in");
        Check.Equal("austerlitz,Borodino,Wagram 1809", Names(ImportPage.Listed(newestFirst, "", ShelfOrder.Name)), "by name ignores case");
        Check.Equal("Borodino", Names(ImportPage.Listed(newestFirst, "200", ShelfOrder.Name)), "a Workshop id finds its module");
        Check.Equal("Wagram 1809", Names(ImportPage.Listed(newestFirst, "wagram 18", ShelfOrder.Newest)), "every word must be found");
        newestFirst.Add(new ModuleEntry { Name = "Lodi", Id = "TS_Save_7", Kind = "save", Folder = "Finished Games" });
        Check.Equal("Lodi", Names(ImportPage.Listed(newestFirst, "finished", ShelfOrder.Newest)), "a save's folder finds it");
        await r.Frame();
    }

    [NodeTest]
    static async Task Under_a_set_order_cards_still_go_into_folders_but_never_between_cards(TestRunner r)
    {
        var host = new VBoxContainer();
        r.AddChild(host);
        var lib = new Library();
        lib.AddFolder(true, "", "F");
        var cfg = new Settings { PackOrder = ShelfOrder.Name };
        var view = new ShelfView(null!, lib, cfg);
        int refreshed = 0;
        ShelfShown Now() => Shown(view, host, pack: true, Three, () => refreshed++);
        Check.That(!Now().ByHand(), "a set order is not the hand order");
        var aim = ShelfView.Aim(Now(), "a", null, 0.1f, new[] { "c" });
        Check.That(aim.into == null && aim.slot < 0, $"beside a card there is no place to drop: {aim}");
        view.Drop(Now(), "a", null, 0.1f, new[] { "c" });
        Check.Equal("/F,a,b,c", Row(lib, Three), "so a card dropped there stays where it was");
        view.Drop(Now(), "/F", "F", 0.1f, new[] { "c" });
        Check.Equal("c", Row(lib, Three, "F"), "the edge of a folder takes a card in under a set order");
        cfg.PackOrder = ShelfOrder.Yours;
        Check.That(Now().ByHand(), "back to your order, cards are reordered by hand again");
        view.Drop(Now(), "a", null, 0.1f, new[] { "b" });
        Check.Equal("/F,b,a", Row(lib, Three), "and a card dropped beside another takes that place");
        await r.Frame();
        Check.Equal(2, refreshed, "only the drops that moved something draw the page again");
        host.QueueFree();
        await r.Frame();
    }
}
