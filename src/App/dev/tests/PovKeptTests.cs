using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PovKeptTests
{
    const string U = Layers.Umpire;
    const double Reach = 10, Behind = 6, LineAt = 3;

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    static List<Vec2> Across() => new() { new(LineAt, -Reach), new(LineAt, Reach) };

    static async Task<OfflineTable> Table(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 3, arrange: g =>
        {
            g.VisionRadius = Reach;
            var board = g.Layer(U);
            board[0].Pos = new Vec2(0, 0);
            board[1].Pos = new Vec2(Behind, 0);
            board[2].Pos = new Vec2(0, -Behind);
        });
        await r.Frames(2);
        return t;
    }

    static async Task<SightingDraft> Look(TestRunner r, OfflineTable t, string id)
    {
        t.Table.SetSelection(new[] { id });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        return t.Table.Draft ?? throw new TestFailure("no POV capture began");
    }

    static List<Stroke> Kept(OfflineTable t) => t.S.View.DrawLayer(U).Where(st => st.Sight != null).ToList();

    [NodeTest]
    static async Task A_line_kept_on_the_table_goes_on_the_sight_layer_and_every_later_pov_capture_uses_it_while_that_layer_is_shown(TestRunner r)
    {
        var t = await Table(r);
        var d = await Look(r, t, "p1");
        var s = t.Table.Sighting;
        s.SetKeep(true);
        Check.That(t.Table.Settings.SightingKeep, "keeping is remembered for the next capture");
        Check.That(s.Trace(Across()), "a line is kept");
        await r.Frames(2);
        var sight = t.S.View.Boards.SingleOrDefault(b => b.Sight) ?? throw new TestFailure("no Sight layer was made");
        Check.That(sight.Name == "Sight" && Kept(t) is [{ } kept] && kept.Board == sight.Id && d.Lines.Count == 0, "the line is a drawing on the Sight layer, not the picture's own");
        Check.Equal("p1,p3", InSight(d), "and it blocks the view now");
        t.Table.CancelSighting();
        d = await Look(r, t, "p3");
        Check.That(d.Keep && !InSight(d).Contains("p2"), "a later capture, from another piece, is blocked by it too: " + InSight(d));
        t.Table.ShowBoard(sight.Id, false);
        s.TableChanged();
        await r.Frames(3);
        Check.That(InSight(d).Contains("p2"), "with the Sight layer hidden its lines stop counting: " + InSight(d));
        t.Table.ShowBoard(sight.Id, true);
        t.Table.CancelSighting();
    }

    [NodeTest]
    static async Task A_kept_line_is_picked_with_a_click_moved_by_its_corners_changed_in_kind_and_taken_away_with_delete(TestRunner r)
    {
        var t = await Table(r);
        var d = await Look(r, t, "p1");
        var s = t.Table.Sighting;
        s.SetKeep(true);
        s.Trace(Across());
        await r.Frames(2);
        string id = Kept(t).Single().Id;
        s.Press(new Vec2(LineAt, 2));
        s.Release();
        Check.Equal(id, s.PickedLine, "a click on the kept line picks it, drawing nothing");
        Check.That(Kept(t).Count == 1 && s.Hint().Contains("Delete"), "and the hint line says what can be done with it: " + s.Hint());
        var (first, top) = (Kept(t).Single().Points[0], Kept(t).Single().Points[^1]);
        var to = new Vec2(top.X + 1, top.Z - 2);
        s.Press(top);
        s.Move(to);
        s.Release();
        await r.Frames(2);
        var moved = Kept(t).Single();
        Check.That(moved.Id == id && (moved.Points[^1] - to).Length < 1e-6 && (moved.Points[0] - first).Length < 1e-9, "dragging a corner moves that corner of the same line");
        s.SetLineKind(SightLine.Blurs);
        await r.Frames(3);
        Check.Equal(SightLine.Blurs, Kept(t).Single().Sight, "Lines turns the picked line into a blur line");
        Check.That(InSight(d).Contains("p2"), "which no longer hides what lies behind it: " + InSight(d));
        t.Table.Keys.OnKey(new InputEventKey { Keycode = Key.Delete, Pressed = true });
        await r.Frames(2);
        Check.That(Kept(t).Count == 0 && s.PickedLine == null, "Delete takes it away");
        t.Table.CancelSighting();
    }

    [NodeTest]
    static async Task The_sight_layer_has_its_eye_in_the_draw_panel_like_every_layer_and_it_follows_the_pov_window_s(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new AddBoard { Name = SightBoard.Named, Sight = true });
        for (int i = 0; i < 60 && !h.S.View.Boards.Any(b => b.Sight); i++) await r.Frame();
        var sight = h.S.View.Boards.Single(b => b.Sight).Id;
        IHudShell shell = h.Hud;
        shell.TogglePanel("paint");
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(3);
        Button[] DrawEyes() => WindowTests.Descendants(shell.Panels["paint"].Body).OfType<Button>()
            .Where(b => b.TooltipText.StartsWith("Hide this layer", StringComparison.Ordinal) || b.TooltipText.StartsWith("Show it again", StringComparison.Ordinal)).ToArray();
        Button SightEye() => WindowTests.Descendants(shell.Panels["paint"].Body).OfType<Button>().Single(b => b.Text == SightBoard.Named).GetParent().GetChild<Button>(0);
        Button Shown() => h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.TooltipText.StartsWith("Kept lines", StringComparison.Ordinal));
        Check.Equal(h.S.View.Boards.Count, DrawEyes().Length, "every layer, Sight too, has its eye in the Draw panel");
        Check.That(Shown().TooltipText.StartsWith("Kept lines shown", StringComparison.Ordinal), "the POV window's Lines row shows the kept lines' state");
        SightEye().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.Table.HiddenBoards.Contains(sight) && Shown().TooltipText.StartsWith("Kept lines hidden", StringComparison.Ordinal), "the Draw panel's eye hides the Sight layer and the POV window says so");
        Shown().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(!h.Table.HiddenBoards.Contains(sight) && SightEye().TooltipText.StartsWith("Hide this layer", StringComparison.Ordinal), "the POV window's eye shows it again and the Draw panel's eye follows");
        h.Table.CancelSighting();
    }

    [NodeTest]
    static async Task The_pov_window_keeps_its_everyday_rows_up_front_and_the_rest_on_its_options_page_with_the_profiles_over_both(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: "pages");
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var panel = h.Hud.Pov.Panel;
        List<string> Shown() => panel.Body.FindChildren("*", nameof(Label), true, false).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text).ToList();
        Button Turn(string text) => panel.Body.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.Text == text && b.IsVisibleInTree());
        bool Profiles() => panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.Text == "+" && b.IsVisibleInTree());
        var everyday = Shown();
        Check.That(new[] { "View", "Format", "Radius", "Viewshed", "Lines" }.All(everyday.Contains) && !everyday.Contains("Shot") && !everyday.Contains("Sharp to"), "the everyday page: " + string.Join(", ", everyday));
        Check.That(Profiles(), "the profiles stand over it");
        Turn("Options  ›").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var options = Shown();
        Check.That(new[] { "Shot", "Size", "Text", "Sharp to", "Line blur" }.All(options.Contains) && !options.Contains("Radius"), "the options page: " + string.Join(", ", options));
        Check.That(Profiles() && Turn("Capture") != null, "with the profiles still over it and Capture still at hand");
        Turn("‹  Back").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Shown().Contains("Radius"), "and back to the everyday page");
        h.Table.CancelSighting();
    }
}
