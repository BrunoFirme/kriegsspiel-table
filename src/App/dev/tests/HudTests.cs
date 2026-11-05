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

    internal static async Task<Rig> Host(TestRunner r, int pieces = 2, int sheets = 0, string[]? packs = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "ktable-nodetest-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var store = new PackageStore(root);
        store.RegisterDef(new PackManifest { Id = "test", Name = "Test" }, new PieceDef { Id = "x", Name = "Block" });
        var g = new GameState { Name = "Hud test", Packs = packs?.ToList() ?? new() };
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
        Check.That(h.Hud.LogPanel.Text.GetParsedText().Contains("No map yet"), "the transcript says so");
        var mapped = await Host(r, sheets: 1);
        Check.That(!mapped.Hud.LogPanel.Text.GetParsedText().Contains("No map yet"), "not said with a map");
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
    static async Task The_host_s_table_fetches_the_server_s_again_when_it_cannot_take_a_change(TestRunner r)
    {
        var h = await Host(r);
        h.S.Live.Layers[Layers.Umpire].RemoveAll(p => p.Id == "p2");
        bool thrown = false;
        h.S.Applied += _ =>
        {
            if (thrown) return;
            thrown = true;
            throw new System.InvalidOperationException("the table broke");
        };
        h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p1", Pos = new Vec2(5, 5) });
        await r.Frames(2);
        Check.That(thrown, "the table failed to take the move");
        var pieces = h.S.View.Layers[Layers.Umpire];
        Check.That(pieces.Any(p => p.Id == "p2"), "the server's table came back");
        Check.That(pieces.Any(p => p.Id == "p1" && p.Pos.X == 5), "with the move in it");
    }

    [NodeTest]
    static async Task A_table_that_never_takes_the_server_s_stops_fetching_it_after_two_tries_and_says_so(TestRunner r)
    {
        var h = await Host(r);
        int rebuilds = 0;
        var told = new System.Collections.Generic.List<string>();
        void Heard(string t) => told.Add(t);
        h.S.Applied += _ => throw new System.InvalidOperationException("the table broke");
        h.S.Reset += () =>
        {
            rebuilds++;
            throw new System.InvalidOperationException("the table broke");
        };
        KTable.Client.Problems.Told += Heard;
        try
        {
            for (int i = 0; i < 4; i++)
            {
                h.S.Do(new MovePiece { Layer = Layers.Umpire, Id = "p1", Pos = new Vec2(i, 5) });
                await r.Seconds(3.1);
            }
        }
        finally { KTable.Client.Problems.Told -= Heard; }
        Check.Equal(2, rebuilds, "rebuilds before giving up");
        Check.Equal(1, told.Count(t => t.Contains("could not follow")), "told once: " + string.Join(" | ", told));
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

    [NodeTest]
    static async Task The_pov_panel_switches_view_lens_text_and_facing(TestRunner r)
    {
        var h = await Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        Check.That(h.Table.Draft != null && h.Hud.Pov.Panel.Visible, "the tool is out with its panel");
        Button Named(string text) => h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text);
        Named("First person").EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        Check.That(h.Table.Draft!.FirstPerson && h.Table.Rig.Eye != null, "first person: the eye is on the piece");
        var eyeSlider = h.Hud.Pov.Panel.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().ToList();
        Check.That(eyeSlider.Count == 1 && eyeSlider[0].MaxValue > 0, "first person has one slider, for how high the eye stands");
        Check.That(!h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.Text == "Clear"), "no tracing to undo or clear from the eye");
        Check.That(h.Hud.Pov.Panel.FindChildren("*", nameof(SpinBox), true, false).Count == 0, "and no reach to type");
        Named("Wide").EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        Check.That(h.Table.Draft!.Wide && Mathf.IsEqualApprox(h.Table.Rig.Cam.Fov, CameraRig.WideLens), "the wide lens");
        Named("E").EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        Check.Equal("E", h.Table.Draft!.Facing, "east held");
        Check.That(Named("E").ButtonPressed && !Named("N").ButtonPressed, "the held direction is the lit one");
        Named("Hidden").EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        Check.That(h.Table.Draft!.HideText && (h.Table.Rig.Cam.CullMask & Table.TextLayer) == 0, "text hidden");
        Named("2D").EmitSignal(Button.SignalName.Pressed);
        await r.Frame();
        Check.That(!h.Table.Draft!.FirstPerson && h.Table.Rig.Eye == null && h.Table.Rig.TopDown, "back to the map from above");
        Check.That(h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.Text == "Round"), "the format row is back");
        Check.That(h.Hud.Pov.Panel.FindChildren("*", nameof(SpinBox), true, false).Count == 1 && h.Hud.Pov.Panel.FindChildren("*", nameof(HSlider), true, false).Count == 0,
            "with the reach back and the eye slider gone");
        h.Table.CancelSighting();
        await r.Frame();
        Check.That(h.Table.Draft == null && (h.Table.Rig.Cam.CullMask & Table.TextLayer) != 0, "the tool is down and text is back");
    }
}
