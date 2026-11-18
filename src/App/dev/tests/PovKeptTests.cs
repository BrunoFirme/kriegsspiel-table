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
    static async Task The_sight_layer_wears_the_pov_mark_in_the_draw_panel(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.S.Do(new AddBoard { Name = SightBoard.Named, Sight = true });
        for (int i = 0; i < 60 && !h.S.View.Boards.Any(b => b.Sight); i++) await r.Frame();
        IHudShell shell = h.Hud;
        shell.TogglePanel("paint");
        await r.Frames(3);
        var body = shell.Panels["paint"].Body;
        Check.That(WindowTests.Descendants(body).OfType<ToolIcon>().Any(i => i.Kind == "sighting" && i.TooltipText.StartsWith("POV capture lines kept", StringComparison.Ordinal)),
            "the Sight layer's row carries the POV mark and its tooltip says what it holds");
        shell.TogglePanel("paint");
    }
}
