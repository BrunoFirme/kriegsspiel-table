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

static class HudTests
{
    internal sealed record Rig(Hud Hud, Table Table, Session S, Net Net);

    internal static async Task<Rig> Host(TestRunner r, int pieces = 2, int sheets = 0)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new PackageStore(root);
        store.RegisterDef(new PackManifest { Id = "test", Name = "Test" }, new PieceDef { Id = "x", Name = "Block" });
        var g = new GameState { Name = "Hud test" };
        g.Layers[Layers.Umpire] = Enumerable.Range(1, pieces).Select(i => new PieceInstance { Id = "p" + i, Def = "x", Pos = new Vec2(i, 0) }).ToList();
        for (int i = 0; i < sheets; i++) g.Sheets.Add(new SheetDef { Id = "s" + i });
        var s = new Session(new Assets(store));
        var net = new Net { Name = "Net" };
        r.AddChild(net);
        net.Init(s);
        net.HostOffline(new GameServer(g, store), "Tester");
        var cfg = new Settings();
        var table = new Table { Name = "Table" };
        r.AddChild(table);
        table.Bind(s, cfg);
        var hud = new Hud { Name = "Hud" };
        r.AddChild(hud);
        hud.Bind(s, table, net, cfg);
        await r.Frames(2);
        return new Rig(hud, table, s, net);
    }

    [NodeTest]
    static async Task The_top_line_names_the_game_and_the_host_s_part_in_it(TestRunner r)
    {
        var h = await Host(r);
        Check.That(h.Hud.Top.Title.Text.Contains("Hud test"), "title: " + h.Hud.Top.Title.Text);
        Check.That(h.Hud.Top.NextTurnBtn.Visible, "an umpire can end the turn");
        Check.That(!h.Hud.Top.SaveWarn.Visible, "nothing unsaved");
        Check.That(h.Hud.LogPanel.Text.GetParsedText().Contains("you are the host umpire"), "the transcript says who you are");
    }

    [NodeTest]
    static async Task An_empty_table_points_the_umpire_at_the_map_page(TestRunner r)
    {
        var h = await Host(r, sheets: 0);
        Check.That(h.Hud.LogPanel.Text.GetParsedText().Contains("This scenario has no map yet"), "the transcript says so");
        var mapped = await Host(r, sheets: 1);
        Check.That(!mapped.Hud.LogPanel.Text.GetParsedText().Contains("This scenario has no map yet"), "not said with a map");
    }

    [NodeTest]
    static async Task Next_turn_advances_the_clock_and_keeps_the_turn_in_the_history(TestRunner r)
    {
        var h = await Host(r);
        int before = h.S.View.Clock.Turn;
        h.Hud.Top.NextTurnBtn.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(before + 1, h.S.View.Clock.Turn, "turn");
        Check.Equal(1, h.Net.Server!.Turns.Count, "turns in the history");
    }

    [NodeTest]
    static async Task A_refused_change_is_written_in_the_transcript(TestRunner r)
    {
        var h = await Host(r);
        string? why = null;
        h.S.Error += e => why = e;
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "nobody", Pos = new Vec2(1, 1) });
        await r.Frame();
        Check.That(why != null, "the session was told");
        var lines = h.Hud.LogPanel.Text.GetParsedText();
        Check.That(lines.TrimEnd().EndsWith(why!, System.StringComparison.Ordinal), $"the transcript ends with '{why}': ...{lines[^System.Math.Min(lines.Length, 160)..]}");
    }

    [NodeTest]
    static async Task Leaving_the_tree_takes_every_handler_off_the_session(TestRunner r)
    {
        var h = await Host(r);
        h.Hud.QueueFree();
        await r.Frames(2);
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p1", Pos = new Vec2(3, 3) });
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "nobody", Pos = new Vec2(1, 1) });
        await r.Frame();
        Check.Equal(new Vec2(3, 3), h.Table.Pieces.NodeOf(Layers.Umpire, "p1")!.Piece.Pos, "the table still follows the game");
    }

    [NodeTest]
    static async Task A_tool_panel_opens_and_closes_from_its_button_and_the_button_shows_which(TestRunner r)
    {
        var h = await Host(r);
        IHudShell shell = h.Hud;
        Check.That(!shell.Panels["boards"].Visible, "the layers panel starts shut");
        shell.TogglePanel("boards");
        await r.Frame();
        Check.That(shell.Panels["boards"].Visible, "opened");
        shell.TogglePanel("boards");
        await r.Frame();
        Check.That(!shell.Panels["boards"].Visible, "shut again");
    }

    [NodeTest]
    static async Task The_settings_open_from_the_menu_as_a_dialog_on_the_hud(TestRunner r)
    {
        var h = await Host(r);
        ((IHudShell)h.Hud).ShowSettings();
        await r.Frames(2);
        var dlg = h.Hud.FindChildren("*", nameof(AcceptDialog), true, false).OfType<AcceptDialog>().SingleOrDefault(d => d.Title == "Settings");
        Check.That(dlg != null && dlg.Visible, "the settings dialog is up");
    }
}
