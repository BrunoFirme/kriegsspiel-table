using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client.Prefs;
using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;
using KTable.Server;

namespace KTable.App;

static class StudioInPlaceTests
{
    const string U = Layers.Umpire;

    sealed record Hosted(Hud Hud, Table Table, Session S, Net Net, Settings Cfg, string Root, string Pack, string Hussars);

    static async Task<Hosted> Host(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var maker = new PackageStore(root);
        var packId = PackEdit.CreatePack(maker, "Test pieces", "Tester");
        var pack = maker.Pack(packId)!;
        var sha = PackEdit.AddImage(maker, pack, Image.CreateEmpty(8, 8, false, Image.Format.Rgba8));
        var hussars = PackEdit.NewDef(maker, pack, "Hussars", sha, 8, 8, 1.0, "box").Id;
        PackEdit.Save(maker, pack);
        var store = new PackageStore(root);
        store.RegisterDef(new PackManifest { Id = "std.blocks", Name = "Blocks" }, new PieceDef { Id = "std.blocks.block", Name = "Block" });
        var g = new GameState { Name = "In place test", ScenarioId = "in-place-test", Packs = new() { packId } };
        g.Layers[U] = new() { new PieceInstance { Id = "p1", Def = hussars, Pos = new Vec2(0, 0) }, new PieceInstance { Id = "p2", Def = "std.blocks.block", Pos = new Vec2(3, 0) } };
        var s = new Session(new Assets(store));
        var net = new Net { Name = "Net" };
        r.AddChild(net);
        net.Init(s);
        net.HostOffline(new GameServer(g, store), "Bruno");
        var cfg = new Settings();
        var table = new Table { Name = "Table" };
        r.AddChild(table);
        table.Bind(s, cfg);
        var hud = new Hud { Name = "Hud" };
        r.AddChild(hud);
        hud.Bind(s, table, net, cfg);
        await r.Frames(2);
        return new Hosted(hud, table, s, net, cfg, root, packId, hussars);
    }

    sealed record Studio(PieceStudio Ed, StudioInPlace Place, PackageStore Store, System.Func<bool> Back);

    static async Task<Studio> Open(TestRunner r, Hosted h, StudioDraft? restored = null)
    {
        var store = new PackageStore(h.Root);
        if (restored != null) store.Adopt(h.Pack, restored.Pack);
        var s = new Session(new Assets(store));
        var g = new GameState { Name = "Test pieces", Packs = store.ListPackIds().ToList() };
        g.Layers[U] = new();
        var server = new GameServer(g, store);
        void Deliver(System.Collections.Generic.List<Outgoing> outs)
        {
            foreach (var o in outs)
                if (o.Message is CommandMessage cm) s.OnCommand(cm.Command);
                else if (o.Message is JoinResult jr) s.OnJoined(jr);
        }
        s.Send = c => Deliver(server.Handle(1, c));
        s.Request = m => Deliver(server.Request(1, m));
        Deliver(server.Join(1, new JoinRequest { Name = "Tester" }));
        var table = new Table { Name = "StudioTable" };
        r.AddChild(table);
        table.Bind(s, h.Cfg);
        h.Table.Rest(true, pause: true);
        var place = new StudioInPlace(h.S, h.Table, h.Net, restored);
        bool back = false;
        var ed = new PieceStudio { Name = "Studio", InPlace = place };
        r.AddChild(ed);
        ed.Bind(s, table, store, h.Pack, h.Cfg, () => back = true, h.Hussars);
        await r.Frames(5);
        return new Studio(ed, place, store, () => back);
    }

    static int AddPacks(Hosted h, System.Action act)
    {
        int n = 0;
        void Count(Command c) { if (c is AddPack) n++; }
        h.S.Applied += Count;
        act();
        h.S.Applied -= Count;
        return n;
    }

    static async Task Rename(TestRunner r, Studio st, string kind, string name)
    {
        st.Ed.Pack.Pieces.Single(d => d.Id == kind).Name = name;
        st.Ed.SavePack();
        await st.Ed.FlushAllAsync();
        await r.Frames(2);
    }

    static async Task<string> PieceCard(TestRunner r, Hosted h, string id)
    {
        foreach (var p in h.Hud.FindChildren("*", nameof(PopupPanel), true, false).OfType<PopupPanel>()) p.Hide();
        h.Table.Selection.Set(new[] { id });
        h.Hud.Menus.Show(new Vector2(300, 300), h.Table.Pieces.NodeOf(U, id));
        await r.Frames(2);
        var card = h.Hud.FindChildren("*", nameof(PopupPanel), true, false).OfType<PopupPanel>().First(p => p.Visible);
        return string.Join(" | ", card.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).Where(t => t.Length > 0));
    }

    [NodeTest]
    static async Task The_host_s_piece_card_offers_the_piece_studio_for_a_kind_of_their_own_pack_and_not_for_a_standard_one(TestRunner r)
    {
        var h = await Host(r);
        var mine = await PieceCard(r, h, "p1");
        Check.That(mine.Contains("Edit in the piece studio…"), "a kind of the host's own pack: " + mine);
        var standard = await PieceCard(r, h, "p2");
        Check.That(!standard.Contains("Edit in the piece studio"), "a standard kind is never edited in place: " + standard);
    }

    [NodeTest]
    static async Task An_edit_in_place_shows_on_the_host_s_own_table_only_until_confirmed_and_then_goes_to_everyone_once(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        Check.Equal(h.Hussars, st.Ed.Sel?.Id, "the kind right-clicked at the table is the one shown");
        Check.Equal("scenario", st.Ed.View, "the studio opens on the scenario");
        var buttons = st.Ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().Select(b => b.Text).ToList();
        Check.That(buttons.Contains("Confirm") && buttons.Contains("Discard") && !buttons.Contains("Done"), "Confirm and Discard replace Done: " + string.Join(", ", buttons));
        var manifest = PackEdit.ManifestPath(st.Store, h.Pack);
        var onDisk = File.ReadAllText(manifest);
        var drawn = h.Table.Pieces.NodeOf(U, "p1");
        await Rename(r, st, h.Hussars, "Hussars of the Guard");
        Check.Equal("Hussars of the Guard", h.S.Def(h.Hussars)?.Name, "the host's own table shows the draft");
        Check.Equal("Hussars", h.S.Store.Def(h.Hussars)?.Name, "the game's own pieces, which the server and joiners read, are untouched");
        Check.Equal(onDisk, File.ReadAllText(manifest), "the pack on disk is untouched while editing");
        Check.That(h.Table.Pieces.NodeOf(U, "p1") != drawn, "the edited kind's pieces are drawn again");
        int sent = AddPacks(h, st.Ed.AskConfirm);
        for (int i = 0; i < 60 && !st.Back(); i++) await r.Frames(1);
        Check.That(st.Back(), "Confirm goes back to the table");
        Check.Equal(1, sent, "the pack is sent to everyone once");
        Check.Equal("Hussars of the Guard", h.S.Store.Def(h.Hussars)?.Name, "the game's pieces now carry the change");
        Check.That(File.ReadAllText(manifest).Contains("Hussars of the Guard"), "the pack on disk carries the change");
    }

    [NodeTest]
    static async Task Discarding_puts_the_pieces_back_removes_what_the_draft_added_and_sends_nothing(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        var manifest = PackEdit.ManifestPath(st.Store, h.Pack);
        var onDisk = File.ReadAllText(manifest);
        var added = PackEdit.AddImage(st.Store, st.Ed.Pack, Image.CreateEmpty(4, 4, false, Image.Format.Rgba8));
        var addedFile = Path.Combine(st.Store.PackageDir(h.Pack), st.Ed.Pack.Assets[added].File);
        await Rename(r, st, h.Hussars, "Hussars of the Line");
        Check.Equal("Hussars of the Line", h.S.Def(h.Hussars)?.Name, "the draft shows first");
        Check.That(File.Exists(addedFile), "the draft's picture is on disk while editing");
        int sent = AddPacks(h, () => st.Place.Discard(st.Store, st.Ed.Pack));
        await r.Frames(2);
        Check.Equal(0, sent, "nothing is sent");
        Check.Equal("Hussars", h.S.Def(h.Hussars)?.Name, "the pieces snap back");
        Check.That(!File.Exists(addedFile), "the picture made in the draft is gone");
        Check.Equal(onDisk, File.ReadAllText(manifest), "the pack on disk is as it was");
    }

    [NodeTest]
    static async Task Leaving_untouched_sends_nothing_and_a_turn_picture_waits_while_the_table_rests(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        Check.That(h.Net.PictureHeld?.Invoke() == false, "on the scenario view the table is there to picture");
        await st.Ed.SetViewAsync("model");
        Check.That(h.Net.PictureHeld?.Invoke() == true, "a turn's picture waits while the 3D view covers the table");
        await st.Ed.SetViewAsync("scenario");
        Check.That(h.Net.PictureHeld?.Invoke() == false, "and is taken once the table shows again");
        bool quit = false;
        int sent = 0;
        void Count(Command c) { if (c is AddPack) sent++; }
        h.S.Applied += Count;
        await st.Ed.LeaveForQuit(() => quit = true);
        h.S.Applied -= Count;
        Check.That(quit && st.Place.Ended, "quitting with nothing changed goes straight on");
        Check.Equal(0, sent, "an untouched pack is not sent");
    }
    [NodeTest]
    static async Task Unconfirmed_changes_outlive_a_studio_closed_without_Confirm_and_come_back_to_be_confirmed(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        var manifest = PackEdit.ManifestPath(st.Store, h.Pack);
        await Rename(r, st, h.Hussars, "Hussars of the Reserve");
        var labels = st.Ed.Root.FindChildren("*", nameof(Label), true, false).OfType<Label>().Select(l => l.Text).ToList();
        Check.That(labels.Contains("not confirmed") && !labels.Contains("saved"), "the top bar says the change is not confirmed, never saved");
        st.Ed.T.QueueFree();
        st.Ed.QueueFree();
        await r.Frames(2);
        h.S.ShowDraft(null);
        var kept = StudioDraft.Find(h.Pack);
        Check.That(kept?.Pack.Pieces.Any(d => d.Name == "Hussars of the Reserve") == true, "the draft outlives the studio");
        Check.That(!File.ReadAllText(manifest).Contains("Hussars of the Reserve"), "the pack itself is untouched");
        var again = await Open(r, h, kept);
        Check.Equal("Hussars of the Reserve", again.Ed.Sel?.Name, "the studio opens on the draft");
        Check.Equal("Hussars of the Reserve", h.S.Def(h.Hussars)?.Name, "the table shows it again, to the host alone");
        again.Ed.AskConfirm();
        for (int i = 0; i < 60 && !again.Back(); i++) await r.Frames(1);
        Check.That(File.ReadAllText(manifest).Contains("Hussars of the Reserve"), "Confirm lands it in the pack");
        Check.That(StudioDraft.Find(h.Pack) == null, "and the draft is gone");
    }
}
