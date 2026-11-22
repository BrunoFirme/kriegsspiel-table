using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PovManyEyesTests
{
    const string U = Layers.Umpire;
    const double Reach = 10, Apart = 30, Beside = 4, BesideFar = 3;
    const int Pixels = 128;

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    static async Task<(OfflineTable t, SightingDraft d)> TwoWatchers(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 5, arrange: g =>
        {
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "", Pos = new Vec2(Apart / 2, 0), Size = new Size2(Apart + 4 * Reach, 4 * Reach) });
            g.VisionRadius = Reach;
            var board = g.Layer(U);
            board[0].Pos = new Vec2(0, 0);
            board[1].Pos = new Vec2(Apart, 0);
            board[2].Pos = new Vec2(Beside, 0);
            board[3].Pos = new Vec2(Apart / 2, 0);
            board[4].Pos = new Vec2(Apart + BesideFar, 0);
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1", "p2" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        return (t, t.Table.Draft ?? throw new TestFailure("no POV capture began"));
    }

    [NodeTest]
    static async Task Pieces_picked_together_give_one_picture_of_all_they_see_with_walls_traced_against_each_of_them(TestRunner r)
    {
        var (t, d) = await TwoWatchers(r);
        Check.That(d.Many && d.Eyes.Count == 2, $"each picked piece looks out: {d.Eyes.Count} eyes");
        Check.Equal("p1,p2,p3,p5", InSight(d), "what either sees is in the picture, and the piece between their reaches is not");
        Check.That((d.Center - new Vec2(Apart / 2, 0)).Length < 1e-9 && Math.Abs(d.Frame - (Apart / 2 + Reach)) < 1e-9, $"the picture spans both reaches: {d.Center}, {d.Frame}");
        Check.Equal(Pixels * 5 / 2, t.Table.Sighting.PictureSide(d, Pixels), "and is drawn larger, so each reach keeps its detail");
        Check.That(t.Table.Sighting.Hint().StartsWith("What 2 pieces see together"), "the hint line says whose sight is joined: " + t.Table.Sighting.Hint());
        t.Table.SetSightingFirstPerson(true);
        Check.That(!d.FirstPerson, "first person stays off: it looks out of one piece");
        Check.That(t.Table.Sighting.Trace(new List<Vec2> { new(Beside / 2, -3), new(Beside / 2, 3) }), "a wall is traced");
        Check.Equal("p1,p2,p5", InSight(d), "the wall hides what stands behind it from the piece it faces");
        t.S.Do(new MovePiece { Layer = U, Id = "p2", Pos = new Vec2(Apart / 2 + Reach / 2, 0) });
        await r.Frames(3);
        Check.Equal("p1,p2,p4", InSight(d), "a picked piece moved brings its sight along");
        t.Table.CancelSighting();
    }

    static List<Button> FirstPerson(Control panel) => panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Where(b => b.Text == "FPV").ToList();

    [NodeTest]
    static async Task The_pov_panel_says_how_many_pieces_look_out_and_keeps_first_person_for_one(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 3);
        h.Table.SetSelection(new[] { "p1", "p3" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var panel = h.Hud.Pov.Panel;
        string Count() => panel.FindChildren("*", nameof(Label), true, false).OfType<Label>().Single(l => l.Text.EndsWith("in the picture", StringComparison.Ordinal)).Text;
        var counted = panel.FindChildren("*", nameof(Label), true, false).OfType<Label>().Single(l => l.Text.EndsWith("in the picture", StringComparison.Ordinal));
        Check.That(Count().StartsWith("Seen by 2 pieces · ", StringComparison.Ordinal) && counted.TooltipText.Length > 0, "the count says how many pieces look out, and its tooltip how: " + Count());
        var locked = FirstPerson(panel).Single();
        Check.That(locked.Disabled && locked.TooltipText.Contains("one piece"), "first person waits for one piece: " + locked.TooltipText);
        h.Table.SetSelection(new[] { "p1" });
        await r.Frames(2);
        Check.That(h.Table.Draft is { Many: false }, "picking one piece looks out of it alone");
        Check.That(FirstPerson(panel).Single() is { Disabled: false }, "and first person is there again");
        Check.That(!Count().StartsWith("Seen by", StringComparison.Ordinal), "and the count names no lookouts: " + Count());
        h.Table.CancelSighting();
    }

    static float Solid(Image img, double x, double z)
    {
        double perUnit = img.GetWidth() / (Apart + 2 * Reach);
        return img.GetPixel((int)((x + Reach) * perUnit), (int)(img.GetHeight() / 2.0 - z * perUnit)).A;
    }

    [ScreenTest]
    static async Task A_picture_of_pieces_picked_together_spans_both_reaches_and_leaves_out_the_ground_between(TestRunner r)
    {
        var (t, d) = await TwoWatchers(r);
        var img = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture");
        double wide = img.GetWidth(), tall = img.GetHeight();
        Check.That(Math.Abs(wide / tall - (Apart + 2 * Reach) / (2 * Reach)) < 0.1, $"the picture is as wide as both reaches together: {wide}×{tall}");
        Check.That(Solid(img, 0, 0) > 0.99f && Solid(img, Apart, 0) > 0.99f && Solid(img, Beside, Beside) > 0.99f, "the ground round each piece is in it");
        Check.That(Solid(img, Apart / 2, 0) < 0.01f, $"the ground out of both reaches is see-through: {Solid(img, Apart / 2, 0):0.00}");
        t.Table.CancelSighting();
    }
}
