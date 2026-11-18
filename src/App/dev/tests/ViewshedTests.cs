using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ViewshedTests
{
    const string U = Layers.Umpire;
    const int Cells = 161, Pixels = 128;
    const double SheetSide = 24, Reach = 10, EyeUp = 0.5, EyeOverRidge = 5;
    const double RidgeFrom = 3, RidgeTo = 3.6, RidgeTop = 0.3, RimFrom = 7, RimRise = 3;
    const double Near = 2, Dead = 5, Far = 9;

    static double RingGround(double away) => away >= RidgeFrom && away <= RidgeTo ? RidgeTop : away > RimFrom ? (away - RimFrom) * RimRise : 0;

    static async Task<OfflineTable> RidgeRoundTheEye(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4, arrange: g =>
        {
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "", Size = new Size2(SheetSide, SheetSide) });
            g.VisionRadius = Reach;
            var board = g.Layer(U);
            board[0].Pos = new Vec2(0, 0);
            board[1].Pos = new Vec2(Near, 0);
            board[2].Pos = new Vec2(0, Dead);
            board[3].Pos = new Vec2(-Far, 0);
        });
        await r.Frames(2);
        var grid = new ReliefGrid(Cells, Cells);
        for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                double x = (i / (double)(Cells - 1) - 0.5) * SheetSide, z = (j / (double)(Cells - 1) - 0.5) * SheetSide;
                grid.H[j * Cells + i] = (float)(RingGround(Math.Sqrt(x * x + z * z)) / SheetSide);
            }
        var (sha, _) = ReliefImport.Save(grid, t.S.Store.Root);
        t.S.Relief.Remember(sha, grid);
        t.S.Do(new SetSheetRelief { Id = "s0", Relief = sha, Height = 1 });
        await r.Frames(4);
        Check.That(Math.Abs(t.S.Ground.At(new Vec2(RidgeFrom + 0.3, 0)) - RidgeTop) < 0.05 && t.S.Ground.At(new Vec2(0, Dead)) < 0.05, "the ridge stands round the middle of the sheet");
        return t;
    }

    static async Task<SightingDraft> ViewshedFromP1(TestRunner r, OfflineTable t)
    {
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        t.Table.SetSightingFormat(SightingFormat.Round);
        t.Table.SetSightingViewshed(true);
        Check.That(Math.Abs(t.Table.Draft!.WatchMetres - SightingDraft.ManHigh) < 1e-9, $"a viewshed opens at a man's height: {t.Table.Draft.WatchMetres:0.##}");
        t.Table.SetSightingWatch(EyeUp);
        await r.Frames(2);
        return t.Table.Draft ?? throw new TestFailure("no POV capture began");
    }

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    [NodeTest]
    static async Task A_flying_piece_s_viewshed_looks_from_its_altitude(TestRunner r)
    {
        var t = await RidgeRoundTheEye(r);
        var d = await ViewshedFromP1(r, t);
        Check.Equal("p1,p2,p4", InSight(d), "on the ground the piece behind the ridge is left out");
        double low = d.Seen!.Eye;
        t.S.Do(new SetPieceOption { Layer = U, Id = "p1", Name = PieceOptions.Flies, Value = true });
        await r.Frames(4);
        Check.That(Math.Abs(d.Seen!.Eye - low - PieceInstance.DefaultAltitude) < 0.05, $"in flight the eye stands its altitude higher: {low:0.##} to {d.Seen.Eye:0.##}");
        Check.Equal("p1,p2,p3,p4", InSight(d), "and sees over the ridge");
        Check.That(SightingDraft.HighestWatch >= 150, "the eye can be set half as high again as a hundred metres");
        t.Table.CancelSighting();
    }

    [NodeTest]
    static async Task A_viewshed_leaves_out_what_stands_on_dead_ground_and_a_higher_eye_brings_it_back(TestRunner r)
    {
        var t = await RidgeRoundTheEye(r);
        var d = await ViewshedFromP1(r, t);
        Check.That(d is { Surveyed: true, Seen: not null }, "the viewshed surveys the ground");
        Check.That(d.Seen!.Sees(new Vec2(Near, 0)) && !d.Seen.Sees(new Vec2(0, Dead)) && d.Seen.Sees(new Vec2(-Far, 0)), "near ground and the far rim are seen, the ground behind the ridge is not");
        Check.Equal("p1,p2,p4", InSight(d), "the piece behind the ridge is left out, the one on the far rim is in");
        t.Table.SetSightingWatch(EyeOverRidge);
        Check.Equal("p1,p2,p3,p4", InSight(d), "from over the ridge the piece behind it is in the picture");
        t.Table.SetSightingEye(t.Table.SightingEyeHighest);
        Check.Equal("p1,p2,p3,p4", InSight(d), "the first-person eye is another matter and changes nothing here");
        t.Table.SetSightingWatch(EyeUp);
        Check.Equal("p1,p2,p4", InSight(d), "and out again when the eye comes down");
        t.Table.SetSightingFormat(SightingFormat.Square);
        Check.That(d is { Surveyed: true, Seen: not null } && Math.Abs(d.Seen.Reach - Reach * Math.Sqrt(2)) < 1e-9, $"a square keeps the viewshed, out to its corners: {d.Seen?.Reach:0.##}");
        Check.That(d.Seen!.Sees(new Vec2(Far, Far)) && !d.Seen.Sees(new Vec2(0, Dead)), "the rim in the square's corner is seen, the ground behind the ridge still is not");
        Check.Equal("p1,p2,p4", InSight(d), "and the piece behind the ridge stays out");
        t.Table.SetSightingFormat(SightingFormat.Round);
        t.S.Do(new SetSheetRelief { Id = "s0", Relief = null, Height = 1 });
        await r.Frames(4);
        Check.Equal("p1,p2,p3,p4", InSight(d), "with the ground laid flat everything in reach is seen");
        t.Table.SetSightingViewshed(false);
        Check.That(d.Seen == null, "Viewshed off drops the survey");
        t.Table.CancelSighting();
    }

    [NodeTest]
    static async Task The_pov_panel_offers_the_viewshed_with_its_eye_height(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.Selection.Set(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        Button Named(string text) => h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().First(b => b.Text == text);
        bool EyeRow() => h.Hud.Pov.Panel.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().Any(x => x.TooltipText.StartsWith("How high the eye"));
        if (h.Table.Draft!.FirstPerson) Named("2D").EmitSignal(Button.SignalName.Pressed);
        Named("Square").EmitSignal(Button.SignalName.Pressed);
        Named("Off").EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(!EyeRow() && Named("Off").ButtonPressed && !Named("On").ButtonPressed, "with Viewshed off there is no eye to set");
        Check.That(h.Hud.Pov.Panel.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(l => l.Text == "Viewshed"), "Viewshed is a row of the Reach form");
        Check.That(!h.Hud.Pov.Panel.FindChildren("*", nameof(Button), true, false).OfType<Button>().Any(b => b.Text == "Viewshed"), "and no format of its own");
        Named("On").EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Table.Draft is { Surveyed: true, Seen: not null, Format: SightingFormat.Square } && h.Hud.Cfg is { SightingViewshed: true, SightingFormat: "square" }, "Viewshed is on over the square and kept");
        Check.That(Named("On").ButtonPressed && !Named("Off").ButtonPressed, "On stays lit when the panel is built again");
        Check.That(EyeRow(), "the viewshed has a slider for how high the eye stands");
        Check.That(h.Hud.Pov.Panel.FindChildren("*", nameof(Label), true, false).OfType<Label>().Any(l => l.Text == "Eye height"), "named Eye height");
        var eye = h.Hud.Pov.Panel.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().First(x => x.TooltipText.StartsWith("How high the eye"));
        Check.That(Math.Abs(eye.Value - SightingDraft.ManHigh) < 1e-6, $"it opens at a man's height: {eye.Value:0.##}");
        double low = h.Table.Draft!.Seen!.Eye;
        eye.Value = 30;
        await r.Frame();
        Check.That(Math.Abs(h.Table.Draft.WatchMetres - 30) < 1e-6 && h.Table.Draft.Seen.Eye > low && Math.Abs(h.Hud.Cfg.SightingWatch - 30) < 1e-6, "the slider lifts the eye the viewshed looks from and is kept");
        Check.That(h.Table.Sighting.Hint().StartsWith("The ground hides"), "the top bar says what the viewshed does: " + h.Table.Sighting.Hint());
        Named("Off").EmitSignal(Button.SignalName.Pressed);
        await r.Frames(2);
        Check.That(h.Table.Draft is { Surveyed: false, Seen: null } && !h.Hud.Cfg.SightingViewshed && !EyeRow(), "off again, the survey and its slider go");
        h.Table.CancelSighting();
    }

    static float Solid(Image img, double x, double z)
    {
        double perUnit = img.GetWidth() / (2 * Reach);
        return img.GetPixel((int)(img.GetWidth() / 2.0 + x * perUnit), (int)(img.GetHeight() / 2.0 - z * perUnit)).A;
    }

    [ScreenTest]
    static async Task A_viewshed_picture_is_cut_to_the_seen_ground_with_nothing_behind_it(TestRunner r)
    {
        var t = await RidgeRoundTheEye(r);
        var d = await ViewshedFromP1(r, t);
        var img = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture");
        Check.That(Math.Abs(img.GetWidth() - Pixels) <= 2 && Math.Abs(img.GetHeight() - Pixels) <= 2, $"the far rim is seen all round, so the picture keeps its size: {img.GetWidth()}×{img.GetHeight()}");
        foreach (var (x, z) in new[] { (1.0, 1.0), (-Near, 0.0), (0.0, -Near) })
            Check.That(Solid(img, x, z) > 0.99f, $"near ground at {x},{z} is in the picture: {Solid(img, x, z):0.00}");
        foreach (var (x, z) in new[] { (Dead, 0.0), (-Dead, 0.0), (0.0, Dead), (0.0, -Dead), (3.6, 3.6) })
            Check.That(Solid(img, x, z) < 0.01f, $"dead ground at {x},{z} is see-through: {Solid(img, x, z):0.00}");
        foreach (var (x, z) in new[] { (Far, 0.0), (-Far, 0.0), (0.0, Far), (0.0, -Far) })
            Check.That(Solid(img, x, z) > 0.99f, $"the far rim at {x},{z} shows again: {Solid(img, x, z):0.00}");
        Check.That(img.GetPixel(1, 1).A < 0.01f && img.GetPixel(img.GetWidth() - 2, img.GetHeight() - 2).A < 0.01f, "past the reach there is no background");
        t.Table.SetSightingFormat(SightingFormat.Square);
        var square = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no square picture");
        Check.That(Solid(square, Far, Far) > 0.99f && Solid(square, -Far, -Far) > 0.99f, $"a square viewshed shows the rim in its corners: {Solid(square, Far, Far):0.00}");
        Check.That(Solid(square, 3.6, 3.6) < 0.01f && Solid(square, 0, Dead) < 0.01f, "and leaves the dead ground out");
        t.Table.CancelSighting();
    }

    [ScreenTest]
    static async Task A_round_pov_picture_has_no_background_and_a_first_person_one_is_whole(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.Sheets.Add(new SheetDef { Id = "s0", Image = "", Package = "", Size = new Size2(SheetSide, SheetSide) });
            g.VisionRadius = Reach;
            g.Layer(U)[0].Pos = new Vec2(0, 0);
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture began");
        t.Table.SetSightingFormat(SightingFormat.Round);
        var img = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture");
        Check.That(Solid(img, 0, 0) > 0.99f && Solid(img, 6, 6) > 0.99f, "inside the reach the picture is solid");
        Check.That(img.GetPixel(1, 1).A < 0.01f && img.GetPixel(img.GetWidth() - 2, 1).A < 0.01f, "the corners outside the circle are see-through");
        var corner = img.GetPixel(1, 1);
        Check.That(corner.R + corner.G + corner.B < 0.01f, "and carry no colour of what lies there");
        float rim = img.GetPixel(img.GetWidth() / 2, 0).A + img.GetPixel(img.GetWidth() / 2, 1).A;
        Check.That(rim > 0.01f, $"the circle reaches the picture's edge: {rim:0.00}");
        t.Table.Draft!.Walls.Add(new() { new Vec2(4, -9), new Vec2(4, 9) });
        d.Recompute(t.Table);
        t.Table.ShowDraft();
        var walled = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no walled picture");
        Check.That(walled.GetWidth() < img.GetWidth() * 0.8 && walled.GetHeight() >= img.GetHeight() - 2, $"a wall to the east trims the picture to what is seen: {walled.GetWidth()}×{walled.GetHeight()}");
        t.Table.Draft.Walls.Clear();
        t.Table.Sighting.SetFirstPerson(true);
        await r.Frames(2);
        var eye = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no first-person picture");
        Check.That(eye.GetPixel(1, 1).A > 0.99f && eye.GetPixel(eye.GetWidth() - 2, eye.GetHeight() - 2).A > 0.99f, "a first-person picture keeps its sky and ground");
        t.Table.CancelSighting();
    }
}
