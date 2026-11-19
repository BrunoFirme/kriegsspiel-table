using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class PovGridTests
{
    const string U = Layers.Umpire;
    const double Pitch = 10, Root3 = 1.7320508075688772;
    const int Pixels = 128;

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    static readonly string[] FormatNames = { "Round", "Square", "Drawn", "Grid" };

    static List<string> Formats(Control panel) =>
        panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Where(b => b.IsVisibleInTree() && FormatNames.Contains(b.Text)).Select(b => b.Text).ToList();

    static void Press(Control panel, string text) =>
        panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Single(b => b.IsVisibleInTree() && b.Text == text).EmitSignal(BaseButton.SignalName.Pressed);

    static bool Shows(Control panel, string text) =>
        panel.FindChildren("*", "", true, false).Any(n => n is Label { Text: var l } && l == text || n is LineEdit { Text: var e } && e == text);

    [NodeTest]
    static async Task Grid_is_offered_only_while_the_scenario_has_a_grid_and_the_grid_going_off_turns_the_picture_round(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 2);
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var panel = h.Hud.Pov.Panel;
        Check.Equal("Round,Square,Drawn", string.Join(",", Formats(panel)), "with no grid the format row has no Grid");

        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "hex", Pitch = Pitch } });
        await r.Frames(3);
        Check.Equal("Round,Square,Drawn,Grid", string.Join(",", Formats(panel)), "a grid laid while the window is open adds Grid at once");
        Press(panel, "Grid");
        await r.Frames(2);
        var d = h.Table.Draft ?? throw new TestFailure("no POV capture");
        Check.That(d.Gridded && h.Hud.Cfg.SightingFormat == "grid", $"the picture is cut to the grid and the choice is kept: {d.Format}, {h.Hud.Cfg.SightingFormat}");
        Check.That(Shows(panel, PovPanel.RingsText(d.Rings, d.Grid!)), "the radius reads in hexes: " + PovPanel.RingsText(d.Rings, d.Grid!));
        Check.That(!Shows(panel, "Lines"), "no lines are traced in a grid picture");
        Check.That(h.Table.Sighting.Hint().StartsWith("Whole hexes around the piece", StringComparison.Ordinal), "the hint line says so: " + h.Table.Sighting.Hint());
        int was = d.Rings;
        h.Table.Sighting.Reach(true);
        Check.Equal(was + 1, d.Rings, "the larger key adds a ring");
        h.Table.Sighting.Reach(false);
        h.Table.Sighting.Reach(false);
        Check.Equal(Math.Max(was - 1, 0), d.Rings, "and the smaller key takes one");

        h.S.Do(new SetGrid { Grid = new GridDef { Kind = GridDef.NoGrid, Pitch = Pitch } });
        await r.Frames(3);
        Check.That(d.Format == SightingFormat.Round && !d.Gridded, "the grid taken off turns the picture round: " + d.Format);
        Check.Equal("Round,Square,Drawn", string.Join(",", Formats(panel)), "and Grid leaves the format row");
        Check.Equal("grid", h.Hud.Cfg.SightingFormat, "while the setting stays for the next gridded table");
        h.Table.CancelSighting();

        h.S.Do(new SetGrid { Grid = new GridDef { Kind = "square", Pitch = Pitch } });
        await r.Frames(2);
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        Check.That(h.Table.Draft is { Gridded: true }, "the next POV capture on a gridded table opens on Grid");
        Check.That(Shows(panel, PovPanel.RingsText(h.Table.Draft!.Rings, h.Table.Draft.Grid!)) && PovPanel.RingsText(2, h.Table.Draft.Grid!) == "2 squares", "counted in squares on a square grid");
        h.Table.CancelSighting();
    }

    static OptionButton Pick(HudTests.Rig h, string item) =>
        h.Hud.FindChildren("*", nameof(OptionButton), true, false).OfType<OptionButton>().Single(o => Enumerable.Range(0, o.ItemCount).Any(i => o.GetItemText(i) == item));

    static void Choose(OptionButton o, string item)
    {
        int i = Enumerable.Range(0, o.ItemCount).First(k => o.GetItemText(k) == item);
        o.Select(i);
        o.EmitSignal(OptionButton.SignalName.ItemSelected, i);
    }

    [NodeTest]
    static async Task Scenario_properties_offer_flat_tops_for_hexes_alone(TestRunner r)
    {
        var h = await HudTests.Host(r);
        ScenarioDialog.Open(h.Hud.Ctx);
        await r.Frames(2);
        var top = Pick(h, "Flat");
        Check.That(!top.Visible, "with no grid there is no Top");
        Choose(Pick(h, "Squares"), "Squares");
        await r.Frames(2);
        Check.That(!top.Visible && h.S.View.Grid is { Kind: "square" }, "squares have no top");
        Choose(Pick(h, "Hexes"), "Hexes");
        await r.Frames(2);
        Check.That(top.Visible && h.S.View.Grid is { Kind: "hex", FlatTop: false }, "hexes offer a top and start pointed");
        Choose(top, "Flat");
        for (int i = 0; i < 30 && h.S.View.Grid is not { FlatTop: true }; i++) await r.Frame();
        Check.That(h.S.View.Grid is { Kind: "hex", FlatTop: true }, "Flat stands the hexes in columns");
    }

    static Vec2 FlatCell(int across, int down) => new(across * Pitch * Root3 / 2, (down + (across & 1) * 0.5) * Pitch);

    [NodeTest]
    static async Task Flat_topped_hexes_stand_in_columns_and_the_picture_takes_whole_hexes_ring_by_ring(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 5, arrange: g =>
        {
            g.Grid = new GridDef { Kind = "hex", Pitch = Pitch, FlatTop = true };
            var board = g.Layer(U);
            board[0].Pos = FlatCell(0, 0);
            board[1].Pos = FlatCell(0, 1);
            board[2].Pos = FlatCell(1, 0);
            board[3].Pos = FlatCell(0, 2);
            board[4].Pos = FlatCell(0, 6);
        });
        await r.Frames(2);
        Check.That((GridSnap.Nearest(t.S.View.Grid!, FlatCell(1, 1) + new Vec2(0, 4.6)) - FlatCell(1, 1)).Length < 1e-9, "a piece snaps within a flat-topped hex's height");
        t.Table.SetSelection(new[] { "p1" });
        Check.That(t.Table.Sighting.Begin(Pitch), "the tool begins on the picked piece");
        t.Table.SetSightingFormat(SightingFormat.Grid);
        var d = t.Table.Draft!;
        t.Table.SetSightingRings(1);
        Check.Equal("p1,p2,p3", InSight(d), "one hex out takes in the six round the piece's own, a hex down and a column across");
        Check.That(d.Loops.Count == 1 && d.Polygon.Count == 18, $"and its outline is the ring's: {d.Loops.Count} loops, {d.Polygon.Count} corners");
        t.Table.Sighting.Reach(true);
        Check.Equal("p1,p2,p3,p4", InSight(d), "a ring more reaches two hexes down the column");
        Check.That(d.Polygon.All(v => Math.Max(Math.Abs(v.X - d.Center.X), Math.Abs(v.Z - d.Center.Z)) <= d.Frame + 1e-9), "the frame holds every hex");
        t.Table.SetSightingRings(0);
        Check.Equal("p1", InSight(d), "and none out is the piece's own hex");
        t.S.Do(new MovePiece { Layer = U, Id = "p1", Pos = FlatCell(0, 5) });
        await r.Frames(3);
        Check.Equal("p1", InSight(d), "the hex follows the piece when it moves");
        t.Table.SetSightingRings(1);
        Check.Equal("p1,p5", InSight(d), "and the rings round it");
        t.Table.Sighting.Cancel();
        Check.That(Math.Abs(t.S.View.VisionRadius - 1.5 * Pitch) < 1e-9, "the scenario keeps the reach as one hex out: " + t.S.View.VisionRadius);
    }

    static float Alpha(Image img, double x, double z, double halfWide, double halfTall) =>
        img.GetPixel(Math.Clamp((int)((x + halfWide) / (2 * halfWide) * img.GetWidth()), 0, img.GetWidth() - 1),
                     Math.Clamp((int)((halfTall - z) / (2 * halfTall) * img.GetHeight()), 0, img.GetHeight() - 1)).A;

    [ScreenTest]
    static async Task A_grid_picture_is_cut_on_the_hex_edges_and_cropped_to_them(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.Grid = new GridDef { Kind = "hex", Pitch = Pitch };
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "", Pos = new Vec2(0, 0), Size = new Size2(8 * Pitch, 8 * Pitch) });
            g.Layer(U)[0].Pos = new Vec2(0, 0);
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        t.Table.SetSightingFormat(SightingFormat.Grid);
        t.Table.SetSightingRings(1);
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture");
        var img = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture");
        double halfWide = 1.5 * Pitch, halfTall = (Root3 / 2 + 1 / Root3) * Pitch;
        Check.That(Math.Abs((double)img.GetWidth() / img.GetHeight() - halfWide / halfTall) < 0.06, $"the picture is cropped to the seven hexes: {img.GetWidth()}×{img.GetHeight()}");
        foreach (var (x, z) in new[] { (0.0, 0.0), (Pitch, 0.0), (-Pitch / 2, -Pitch * Root3 / 2), (Pitch * 1.3, 0.0) })
            Check.That(Alpha(img, x, z, halfWide, halfTall) > 0.99f, $"a hex in the ring is in it at {x:0.#}, {z:0.#}");
        foreach (var (x, z) in new[] { (13.5, 12.5), (-13.5, 12.5), (13.5, -12.5), (-13.5, -12.5) })
            Check.That(Alpha(img, x, z, halfWide, halfTall) < 0.01f, $"the ground past the ring's hex edges is see-through at {x:0.#}, {z:0.#}");
        t.Table.CancelSighting();
    }
}
