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

    sealed record Hosted(Hud Hud, Table Table, Session S, Net Net, Settings Cfg, string Root, string Pack, string Hussars, string Uhlans);

    static async Task<Hosted> Host(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var maker = new PackageStore(root);
        var packId = PackEdit.CreatePack(maker, "Test pieces", "Tester");
        var pack = maker.Pack(packId)!;
        var sha = PackEdit.AddImage(maker, pack, Image.CreateEmpty(8, 8, false, Image.Format.Rgba8));
        var hussars = PackEdit.NewDef(maker, pack, "Hussars", sha, 8, 8, 1.0, "box").Id;
        var uhlans = PackEdit.NewDef(maker, pack, "Uhlans", sha, 8, 8, 1.0, "box").Id;
        PackEdit.Save(maker, pack);
        var store = new PackageStore(root);
        store.RegisterDef(new PackManifest { Id = "std.blocks", Name = "Blocks" }, new PieceDef { Id = "std.blocks.block", Name = "Block" });
        var g = new GameState { Name = "In place test", ScenarioId = "in-place-test", Packs = new() { packId } };
        g.Layers[U] = new()
        {
            new PieceInstance { Id = "p1", Def = hussars, Pos = new Vec2(0, 0) },
            new PieceInstance { Id = "p2", Def = "std.blocks.block", Pos = new Vec2(3, 0) },
            new PieceInstance { Id = "p3", Def = hussars, Pos = new Vec2(6, 0) },
        };
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
        return new Hosted(hud, table, s, net, cfg, root, packId, hussars, uhlans);
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
        var place = new StudioInPlace(h.S, h.Table, h.Net, h.Hussars, U, "p1", restored);
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
        Check.Equal("model", st.Ed.View, "the studio opens in 3D");
        Check.That(!h.Table.Resting, "with the scenario behind the piece");
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
    static async Task Save_as_a_copy_gives_only_the_edited_piece_a_new_kind_and_leaves_the_others_as_they_were(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        var buttons = st.Ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().Select(b => b.Text).ToList();
        Check.That(buttons.Contains("Save as a copy"), "the in-place studio offers a copy: " + string.Join(", ", buttons));
        var manifest = PackEdit.ManifestPath(st.Store, h.Pack);
        await Rename(r, st, h.Hussars, "Swedish hussars");
        int sent = AddPacks(h, st.Ed.AskConfirmAsCopy);
        for (int i = 0; i < 60 && !st.Back(); i++) await r.Frames(1);
        Check.That(st.Back(), "Save as a copy goes back to the table");
        Check.Equal(1, sent, "the pack is sent to everyone once");
        Check.Equal("Hussars", h.S.Store.Def(h.Hussars)?.Name, "the kind edited keeps its old self");
        var copy = h.S.View.Find(U, "p1")?.Def;
        Check.That(copy != null && copy != h.Hussars, "the edited piece takes the new kind: " + copy);
        Check.Equal("Swedish hussars", h.S.Store.Def(copy!)?.Name, "the new kind carries the changes");
        Check.Equal(h.Hussars, h.S.View.Find(U, "p3")?.Def, "the other pieces of the kind stay as they were");
        var onDisk = File.ReadAllText(manifest);
        Check.That(onDisk.Contains("Swedish hussars") && onDisk.Contains("\"Hussars\""), "the pack on disk holds both kinds");
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
        Check.That(h.Net.PictureHeld?.Invoke() == true, "a turn's picture waits while the editor has the table's camera");
        st.Place.ToggleBackdrop();
        Check.That(h.Net.PictureHeld?.Invoke() == true, "and while the editor's plain background covers the table");
        st.Place.ToggleBackdrop();
        await st.Ed.SetViewAsync("table");
        Check.That(h.Net.PictureHeld?.Invoke() == true, "Try it covers the table too");
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
    [NodeTest]
    static async Task In_place_only_the_kind_right_clicked_can_be_edited(TestRunner r)
    {
        var h = await Host(r);
        var st = await Open(r, h);
        var uhlans = st.Ed.Pack.Pieces.Single(d => d.Id == h.Uhlans);
        st.Ed.Select(uhlans, frame: false);
        await r.Frames(3);
        Check.Equal(h.Hussars, st.Ed.Sel?.Id, "another kind of the pack cannot be picked");
        Check.Equal(h.Hussars, string.Join(",", st.Ed.Kinds.Select(d => d.Id)), "the studio works on that kind alone");
        var shown = st.Ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text).ToList();
        Check.That(!shown.Contains("＋ New piece"), "no new pieces: " + string.Join(", ", shown));
        int kinds = st.Ed.Pack.Pieces.Count;
        st.Ed.DeleteSel();
        await st.Ed.DuplicateSelAsync();
        st.Ed.NewPieces.NewPiece(0);
        await r.Frames(3);
        Check.Equal(kinds, st.Ed.Pack.Pieces.Count, "nothing is added, copied or deleted");
    }
    [NodeTest]
    static async Task The_piece_is_edited_where_it_stands_and_the_table_s_camera_comes_back_working(TestRunner r)
    {
        var h = await Host(r);
        var before = h.Table.Rig.Cam.GlobalTransform;
        var st = await Open(r, h);
        for (int i = 0; i < 30 && h.Table.Pieces.NodeOf(U, "p1") is { Visible: true }; i++) await r.Frames(1);
        var node = h.Table.Pieces.NodeOf(U, "p1")!;
        Check.That(!node.Visible, "the editor draws the piece in its place");
        var cam = h.Table.Rig.Cam;
        Check.That(cam.GlobalPosition.DistanceTo(node.GlobalPosition) < before.Origin.DistanceTo(node.GlobalPosition), "the camera comes to the piece");
        Check.That(h.Table.Lent && !h.Table.IsProcessingUnhandledInput(), "the table takes no input while the editor has its camera");
        st.Ed.AskDiscard();
        for (int i = 0; i < 60 && !st.Back(); i++) await r.Frames(1);
        h.Table.Rest(false, pause: true);
        await r.Frames(2);
        Check.That(h.Table.IsProcessingUnhandledInput() && h.Table.ProcessMode == Node.ProcessModeEnum.Inherit, "the camera takes input again after the studio closes");
        Check.That(!h.Table.Lent && h.Table.Pieces.NodeOf(U, "p1") is { Visible: true }, "the piece shows again");
        Check.That(h.Table.Rig.Cam.GlobalTransform.Origin.DistanceTo(before.Origin) < 0.01f, "the view is as it was");
        Check.That(h.Net.PictureHeld?.Invoke() == false, "and a turn's picture can be taken");
    }

    [ScreenTest]
    static async Task A_gif_being_recorded_ends_and_is_kept_before_the_studio_covers_the_table(TestRunner r)
    {
        var h = await Host(r);
        h.Cfg.CaptureGif = true;
        h.Cfg.CaptureToClipboard = false;
        h.Cfg.CaptureToFolder = true;
        h.Cfg.CaptureFolder = Path.Combine(h.Root, "captures");
        var run = h.Hud.Captures.Capture(new Rect2(20, 20, 200, 120));
        for (int i = 0; i < 30 && h.Hud.Captures.RegionRecording == null; i++) await r.Frames(1);
        Check.That(h.Hud.Captures.RegionRecording != null, "a GIF is recording");
        await r.Frames(5);
        await h.Hud.Captures.StopRecordings();
        Check.That(run.IsCompleted && h.Hud.Captures.RegionRecording == null, "stopping waits until the recording has ended");
        Check.That(Directory.Exists(h.Cfg.CaptureFolder) && Directory.GetFiles(h.Cfg.CaptureFolder, "*.gif").Length == 1, "and the GIF is kept");
    }
}
