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

static class StudioTripTests
{
    const string U = Layers.Umpire;

    sealed record Hosted(Hud Hud, Table Table, Session S, Net Net, Settings Cfg, string Root);

    static async Task<Hosted> Host(TestRunner r, bool saved = true)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new PackageStore(root);
        store.RegisterDef(new PackManifest { Id = "user.mine", Name = "Mine" }, new PieceDef { Id = "user.mine.hussars", Name = "Hussars" });
        store.RegisterDef(new PackManifest { Id = "std.blocks", Name = "Blocks" }, new PieceDef { Id = "std.blocks.block", Name = "Block" });
        var g = new GameState { Name = "Trip test", ScenarioId = "trip-test" };
        g.Layers[U] = new() { new PieceInstance { Id = "p1", Def = "user.mine.hussars", Pos = new Vec2(0, 0) }, new PieceInstance { Id = "p2", Def = "std.blocks.block", Pos = new Vec2(3, 0) } };
        var s = new Session(new Assets(store));
        if (saved) s.SavePath = Path.Combine(root, "game.kstable");
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
        return new Hosted(hud, table, s, net, cfg, root);
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
        var unsaved = await Host(r, saved: false);
        var loose = await PieceCard(r, unsaved, "p1");
        Check.That(!loose.Contains("Edit in the piece studio"), "a table with no save to come back to: " + loose);
    }

    [NodeTest]
    static async Task Leaving_for_the_studio_saves_the_game_keeps_the_view_and_names_the_save_pack_and_kind_to_come_back_to(TestRunner r)
    {
        var h = await Host(r);
        h.Table.Rig.Show(new CameraRig.View(new Vector3(12, 0, -7), 40, 1.1f, -0.9f, false));
        await r.Frames(1);
        Check.Equal("", StudioTrip.SaveForTrip(h.Hud.Ctx, "user.mine", "user.mine.hussars") ?? "", "nothing stops the trip");
        Check.That(File.Exists(h.S.SavePath), "the game is written to its save before the table closes");
        Check.That(h.Cfg.CameraAt.TryGetValue("trip-test", out var kept) && Mathf.IsEqualApprox(kept[0], 12) && Mathf.IsEqualApprox(kept[1], -7), "the view is kept for the table that comes back");
        var trip = StudioTrip.Take();
        Check.Equal(new StudioTrip(h.S.SavePath!, "Bruno", "user.mine", "user.mine.hussars"), trip, "the trip back");
        Check.That(StudioTrip.Take() == null, "the trip is taken once");
    }

    [NodeTest]
    static async Task The_studio_opened_from_the_table_starts_on_the_kind_and_its_Done_goes_back_to_the_table(TestRunner r)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var packId = PackEdit.CreatePack(new PackageStore(root), "Test pieces", "Tester");
        string uhlans;
        {
            var maker = new PackageStore(root);
            var pack = maker.Pack(packId)!;
            var sha = PackEdit.AddImage(maker, pack, Image.CreateEmpty(8, 8, false, Image.Format.Rgba8));
            PackEdit.NewDef(maker, pack, "Hussars", sha, 8, 8, 1.0, "box");
            uhlans = PackEdit.NewDef(maker, pack, "Uhlans", sha, 8, 8, 1.0, "box").Id;
            PackEdit.Save(maker, pack);
        }
        var store = new PackageStore(root);
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
        var cfg = new Settings();
        var table = new Table { Name = "Table" };
        r.AddChild(table);
        table.Bind(s, cfg);
        bool back = false;
        var ed = new PieceStudio { Name = "Studio", BackToTable = true };
        r.AddChild(ed);
        ed.Bind(s, table, store, packId, cfg, () => back = true, uhlans);
        await r.Frames(5);
        Check.Equal(uhlans, ed.Sel?.Id, "the kind right-clicked at the table is the one shown");
        var done = ed.Root.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.Text == "Done");
        Check.That(done.TooltipText.Contains("back to the table"), "Done says where it goes: " + done.TooltipText);
        ed.AskLeave();
        for (int i = 0; i < 60 && !back; i++) await r.Frames(1);
        Check.That(back, "Done hands back to the trip home");
    }
}
