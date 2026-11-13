using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PlacementTests
{
    const string U = Layers.Umpire;

    static List<string> StripNames(HudTests.Rig h) =>
        WindowTests.Descendants(h.Hud.Strip.Panel).OfType<Button>().Where(b => !b.IsQueuedForDeletion()).Select(b => b.TooltipText.Split('\n')[0].Split("   ")[0]).ToList();

    static Button StripButton(HudTests.Rig h, string name) =>
        WindowTests.Descendants(h.Hud.Strip.Panel).OfType<Button>().First(b => !b.IsQueuedForDeletion() && b.TooltipText.StartsWith(name, StringComparison.Ordinal));

    [NodeTest]
    static async Task The_strip_groups_the_table_the_hand_measuring_marks_records_and_views_and_ends_in_undo_redo_and_the_log(TestRunner r)
    {
        var h = await HudTests.Host(r, sheets: 1);
        var hidden = h.Hud.Cfg.StripHidden.ToList();
        h.Hud.Cfg.StripHidden.Clear();
        h.Hud.Cfg.StripHidden.AddRange(new[] { "calibrate", "boards", "zones" });
        h.Hud.Strip.Rebuild();
        await r.Frames(3);
        var names = StripNames(h);
        var wanted = new[]
        {
            "Map", "Terrain", "Pieces", "Dice", "Select", "Turn / resize", "Formations", "Ruler", "Ranging", "Draw", "Decals", "POV capture", "Capture",
            "3D table", "Plates behind labels", "Frame the map", "Compass", "Undo", "Redo", "Log",
        };
        Check.Equal(string.Join(", ", wanted), string.Join(", ", names), "the buttons, top to bottom, with a choice kept from before for buttons now gone doing no harm");
        var was = h.S.View.Find(U, "p1")!.Pos;
        h.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = was + new Vec2(3, 0) });
        StripButton(h, "Undo").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That((h.S.View.Find(U, "p1")!.Pos - was).Length < 1e-6, "Undo on the strip takes the move back");
        StripButton(h, "Redo").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That((h.S.View.Find(U, "p1")!.Pos - (was + new Vec2(3, 0))).Length < 1e-6, "and Redo does it again");
        h.Hud.Cfg.StripHidden.Clear();
        h.Hud.Cfg.StripHidden.AddRange(hidden);
        h.Hud.Strip.Rebuild();
    }

    [ScreenTest]
    static async Task The_whole_strip_fits_the_smallest_window_by_giving_up_a_little_of_each_button(TestRunner r)
    {
        var h = await HudTests.Host(r);
        var hidden = h.Hud.Cfg.StripHidden.ToList();
        h.Hud.Cfg.StripHidden.Clear();
        h.Hud.Strip.Rebuild();
        await r.Frames(4);
        h.Hud.Relayout();
        await r.Frames(2);
        float floor = h.Hud.GetViewport().GetVisibleRect().Size.Y;
        Check.That(h.Hud.Strip.Panel.GetGlobalRect().End.Y <= floor, $"the strip ends at {h.Hud.Strip.Panel.GetGlobalRect().End.Y} in a window {floor} high");
        Check.That(StripNames(h).Contains("Log") && StripButton(h, "Log").GetGlobalRect().End.Y <= floor, "its last button, the Log, is whole");
        h.Hud.Cfg.StripHidden.AddRange(hidden);
        h.Hud.Strip.Rebuild();
    }

    [NodeTest]
    static async Task The_Draw_panel_holds_the_layers_the_drawings_are_on(TestRunner r)
    {
        var h = await HudTests.Host(r);
        IHudShell shell = h.Hud;
        Check.That(!shell.Panels.ContainsKey("boards"), "there is no Layers panel of its own");
        shell.TogglePanel("paint");
        await r.Frames(3);
        var body = shell.Panels["paint"].Body;
        List<Button> Rows() => WindowTests.Descendants(body).OfType<Button>().Where(b => !b.IsQueuedForDeletion() && b.TooltipText.StartsWith("Draw on this layer", StringComparison.Ordinal)).ToList();
        int layers = h.S.View.Boards.Count;
        Check.Equal(layers, Rows().Count, "a row for every layer");
        WindowTests.Descendants(body).OfType<Button>().Single(b => b.Text == "+" && !b.IsQueuedForDeletion()).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.S.View.Boards.Count == layers + 1 && Rows().Count == layers + 1, "+ adds a layer and its row");
        var second = h.S.View.Boards[^1];
        Rows().Single(b => b.Text == second.Name).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.Equal(second.Id, h.Table.DrawBoard(), "a click on a row makes it the layer drawn on");
        Check.That(Rows().Single(b => b.Text == second.Name).ButtonPressed, "and its row shows it");
        shell.TogglePanel("paint");
    }

    [NodeTest]
    static async Task The_maps_scale_is_set_from_the_Map_page(TestRunner r)
    {
        var bare = await HudTests.Host(r, sheets: 0);
        bare.Hud.Drawer.ShowPage("map");
        bare.Hud.Map.Rebuild();
        await r.Frames(2);
        Button Scale(HudTests.Rig h) => WindowTests.Descendants(h.Hud.Drawer.Pages["map"].page!).OfType<Button>().Single(b => b.Text == "Map scale…" && !b.IsQueuedForDeletion());
        Check.That(Scale(bare).Disabled && Scale(bare).TooltipText == "Add a map first", "with no map the button waits and says why");
        var h = await HudTests.Host(r, sheets: 1);
        h.Hud.Drawer.ShowPage("map");
        h.Hud.Map.Rebuild();
        await r.Frames(2);
        Check.That(!Scale(h).Disabled && Scale(h).TooltipText.Contains("No scale yet", StringComparison.Ordinal), "with a map it says there is no scale yet");
        Scale(h).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(Tool.Calibrate, h.Table.Tool, "and takes the scale tool in hand");
        h.Table.SetTool(Tool.Select);
    }

    [NodeTest]
    static async Task The_zones_page_has_the_eye_that_shows_or_hides_them(TestRunner r)
    {
        var h = await HudTests.Host(r);
        bool was = h.Hud.Cfg.ShowZones;
        h.Hud.Drawer.ShowPage("layers");
        h.Hud.Umpires.Rebuild();
        await r.Frames(2);
        Button Eye() => WindowTests.Descendants(h.Hud.Drawer.Pages["layers"].page!).OfType<Button>()
            .Single(b => !b.IsQueuedForDeletion() && b.TooltipText.Contains("zones", StringComparison.Ordinal) | b.TooltipText == "Show them again");
        Eye().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Hud.Cfg.ShowZones != was && h.Table.ShowZones == !was, "the eye in the page's head shows or hides the zones on this screen");
        Eye().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.Equal(was, h.Hud.Cfg.ShowZones, "and puts them back");
    }

    [NodeTest]
    static async Task Engine_dialogs_put_Cancel_first_and_the_brass_button_last_as_the_apps_own_questions_do(TestRunner r)
    {
        var dlg = new ConfirmationDialog { Title = "Order", OkButtonText = "Do it" };
        Ui.Open(r, dlg);
        await r.Frames(2);
        var buttons = dlg.GetOkButton().GetParent().GetChildren().OfType<Button>().Where(b => b.Visible).ToList();
        Check.That(buttons.IndexOf(dlg.GetCancelButton()) < buttons.IndexOf(dlg.GetOkButton()), "Cancel stands left of the brass button: " + string.Join(", ", buttons.Select(b => b.Text)));
        dlg.Hide();
        dlg.QueueFree();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task Esc_closes_a_reading_window(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new SetBriefing { Text = "Hold the bridge." });
        BriefingWindow.Open(h.Hud, h.S);
        await r.Frames(3);
        var window = h.Hud.GetChildren().OfType<Window>().Last(w => w.Visible);
        window.PushInput(new InputEventKey { Keycode = Key.Escape, Pressed = true }, true);
        await r.Frames(3);
        Check.That(!GodotObject.IsInstanceValid(window) || window.IsQueuedForDeletion() || !window.Visible, "the briefing window is gone");
    }

    [NodeTest]
    static async Task The_studios_menu_ends_in_the_way_out_as_the_tables_does(TestRunner r)
    {
        var rig = await StudioTests.Open(r);
        var anchor = new Control();
        rig.Studio.Root.AddChild(anchor);
        rig.Studio.Menu.Show(anchor);
        await r.Frames(2);
        var menu = WindowTests.Descendants(rig.Studio.Root).OfType<PopupMenu>().Last();
        Check.Equal("Leave the studio", menu.GetItemText(menu.ItemCount - 1), "the last row leaves the studio");
        menu.Hide();
        anchor.QueueFree();
        await r.Frames(2);
    }

    static PopupPanel Card(HudTests.Rig h) =>
        WindowTests.Descendants(h.Hud.Ctx.Root).OfType<PopupPanel>().FirstOrDefault(p => p.Visible) ?? throw new TestFailure("no right-click card is up");

    static Button? Row(PopupPanel card, string text) =>
        WindowTests.Descendants(card).OfType<Button>().FirstOrDefault(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text == text));

    [NodeTest]
    static async Task Right_click_offers_Detach_beside_Attach_and_names_the_3D_switch_as_the_strip_does(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 2);
        h.S.Do(new Attach { Layer = U, Parent = "p1", Child = "p2" });
        h.Table.Selection.Set(new[] { "p2" });
        var node = h.Table.Pieces.NodeOf(U, "p2") ?? throw new TestFailure("the piece is on the table");
        h.Hud.Menus.Show(new Vector2(300, 300), node);
        await r.Frames(2);
        var detach = WindowTests.Descendants(Card(h)).OfType<Button>().FirstOrDefault(b => WindowTests.Descendants(b).OfType<Label>().Any(l => l.Text.StartsWith("Detach from ", StringComparison.Ordinal)))
            ?? throw new TestFailure("no Detach row on an attached piece's card");
        detach.EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(!(h.S.View.Find(U, "p1")!.Attached ?? new()).Contains("p2"), "Detach on the card lets the piece off its carrier");
        h.Table.Selection.Set(Array.Empty<string>());
        h.Hud.Menus.Show(new Vector2(300, 300), null);
        await r.Frames(2);
        Check.That(Row(Card(h), "3D table") != null && Row(Card(h), "Map view") == null, "the table's card calls the switch 3D table, whichever way it stands");
        Card(h).Hide();
    }
}
