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

    static async Task<string> Orders(TestRunner r, OfflineTable t)
    {
        t.S.Do(new AddBoard { Name = "Orders" });
        for (int i = 0; i < 60 && !t.S.View.Boards.Any(b => b.Name == "Orders"); i++) await r.Frame();
        return t.S.View.Boards.Single(b => b.Name == "Orders").Id;
    }

    [NodeTest]
    static async Task The_pov_tools_undo_takes_back_only_its_own_kept_lines_and_leaves_every_other_change_as_it_was(TestRunner r)
    {
        var t = await Table(r);
        await Orders(r, t);
        await Look(r, t, "p1");
        var s = t.Table.Sighting;
        s.SetKeep(true);
        Check.That(s.Trace(Across()), "a line is kept");
        await r.Frames(2);
        Check.That(t.S.View.Boards[0].Sight && t.S.View.Boards.Count >= 2, "the Sight layer is made first in the list");
        var moved = new Vec2(Behind, 1);
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = moved });
        await r.Frames(2);
        Check.That(s.CanUndo && !s.CanRedo, "the POV undo has the kept line to take back");
        s.UndoStroke();
        await r.Frames(3);
        Check.That(Kept(t).Count == 0, "its undo takes the kept line away");
        Check.Equal(moved, t.S.View.Find(U, "p2")!.Pos, "the piece moved after it stays where it was put");
        Check.That(!s.CanUndo && s.CanRedo, "nothing more of the POV tool's to take back, the line to put back");
        s.RedoStroke();
        await r.Frames(3);
        Check.That(Kept(t).Count == 1 && t.S.View.Find(U, "p2")!.Pos == moved, "its redo puts the line back and leaves the piece");
        t.Table.CancelSighting();
    }

    [NodeTest]
    static async Task With_the_pov_window_open_drawing_stays_on_the_sight_layer_and_its_draw_button_makes_one_when_there_is_none(TestRunner r)
    {
        var t = await Table(r);
        var plain = await Orders(r, t);
        t.Table.Board = plain;
        await Look(r, t, "p1");
        Check.That(t.Table.DrawBoard() == plain && !t.Table.DrawLockedToSight, "with no Sight layer the pen stays on the picked layer");
        t.Table.DrawOnSight();
        for (int i = 0; i < 60 && !t.S.View.Boards.Any(b => b.Sight); i++) await r.Frame();
        var sight = t.S.View.Boards.FirstOrDefault(b => b.Sight) ?? throw new TestFailure("the button made no Sight layer");
        Check.That(t.S.View.Boards[0] == sight && t.Table.Tool == Tool.Draw && t.Table.Draft != null, "the button makes the Sight layer first in the list and pulls out the pen with the capture still open");
        t.Table.Board = plain;
        Check.That(t.Table.DrawBoard() == sight.Id && t.Table.DrawLockedToSight && t.Table.DrawsSight, "picking another layer leaves the pen on the Sight layer");
        t.Table.CancelSighting();
        Check.Equal(plain, t.Table.DrawBoard(), "with the window closed the pen goes back to the picked layer");
        t.Table.Board = "";
        Check.Equal(t.S.View.Boards.First(b => !b.Sight).Id, t.Table.DrawBoard(), "and with no layer picked it takes the first that is not the Sight layer");
    }

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
    static async Task The_sight_layer_has_its_eye_in_the_draw_panel_like_every_layer_and_the_pov_window_opens_it(TestRunner r)
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
        Button SightEye() =>
            WindowTests.Descendants(shell.Panels["paint"].Body).OfType<Button>().Single(b => b.Text == SightBoard.Named).GetParent().GetChildren().OfType<Button>().Single(b => DrawEyes().Contains(b));
        Check.Equal(h.S.View.Boards.Count, DrawEyes().Length, "every layer, Sight too, has its eye in the Draw panel");
        Check.That(!h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.TooltipText.StartsWith("Kept lines", StringComparison.Ordinal)), "the POV window has no eye of its own");
        SightEye().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.Table.HiddenBoards.Contains(sight), "the Draw panel's eye hides the Sight layer");
        SightEye().EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(!h.Table.HiddenBoards.Contains(sight), "and shows it again");
        h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.TooltipText.StartsWith("Sight layer\n", StringComparison.Ordinal)).EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(3);
        Check.That(h.Table.Tool == Tool.Draw && h.Table.DrawBoard() == sight && h.Table.Draft != null, "the Sight layer icon on the Lines row pulls out the pen on the Sight layer with the capture still open");
        h.Table.CancelSighting();
    }

    [NodeTest]
    static async Task The_pov_window_shows_every_row_of_its_mode_on_one_page_and_a_row_once_it_matters(TestRunner r)
    {
        var h = await HudTests.Host(r, scenario: "pages");
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var panel = h.Hud.Pov.Panel;
        List<string> Shown() => panel.Body.FindChildren("*", nameof(Label), true, false).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text).ToList();
        IEnumerable<Button> Buttons() => panel.Body.FindChildren("*", nameof(Button), true, false).OfType<Button>().Where(b => b.IsVisibleInTree());
        bool Has(string text) => Buttons().Any(b => b.Text == text);
        bool SightIcon() => Buttons().Any(b => b.TooltipText.StartsWith("Sight layer\n", StringComparison.Ordinal) && b.GetParent().GetChildren().OfType<Button>().Any(x => x.ToggleMode));
        Button Named(string text) => Buttons().Single(b => b.Text == text);
        bool Profiles() => panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => ProfileTabsTests.IsPlus(b) && b.IsVisibleInTree());
        Named("Round").EmitSignal(BaseButton.SignalName.Pressed);
        Named("Off").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var round = Shown();
        Check.That(new[] { "Mode", "Radius", "Viewshed", "Lines", "Blur", "Quality" }.All(round.Contains)
            && !round.Any(l => l is "Eye height" or "Edge blur" or "Line blur" or "Shot" or "Size" or "Sharp to" or "View" or "Format"), "Round: " + string.Join(", ", round));
        Check.That(!Has("Options  ›") && Has("Record") && Has("Capture") && SightIcon() && !Has("Clear") && !round.Any(l => l.Contains("traced", StringComparison.Ordinal)) && Profiles(),
            "one page, Record beside Capture, the Sight layer icon on the Lines row, no traced line yet and the profiles over it");
        Named("On").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        Check.That(Shown().Contains("Eye height"), "Viewshed on brings its eye height");
        Named("Off").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        panel.Body.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().Single(x => x.TooltipText.StartsWith("Where the picture starts to blur", StringComparison.Ordinal)).Value = 0.5;
        await r.Frames(2);
        Check.That(Shown().Contains("Edge blur"), "pulling Blur in brings Edge blur");
        var d = h.Table.Draft!;
        h.Table.Sighting.SetLineKind(SightLine.Blurs);
        Check.That(h.Table.Sighting.Trace(new List<Vec2> { new(d.Center.X + d.Radius / 2, d.Center.Z - d.Radius / 2), new(d.Center.X + d.Radius / 2, d.Center.Z + d.Radius / 2) }), "a blur line is traced");
        await r.Frames(2);
        Check.That(Shown().Contains("Line blur") && Shown().Contains("1 line traced") && Has("Clear"), "a Blur line brings Line blur, and its count with Clear: " + string.Join(", ", Shown()));
        Named("FPV").EmitSignal(BaseButton.SignalName.Pressed);
        await r.Frames(2);
        var fpv = Shown();
        Check.That(new[] { "Mode", "Facing", "Height", "Lens", "Quality" }.All(fpv.Contains) && !fpv.Contains("Radius") && !fpv.Contains("Lines") && !fpv.Contains("Blur"), "FPV: " + string.Join(", ", fpv));
        h.Table.CancelSighting();
    }
}
