using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

static class SightBlurTests
{
    const double Reach = 10, RingAt = 8.5;
    const int Pixels = 400, Around = 12;

    static double Detail(Image img, double from, double to)
    {
        double sum = 0;
        int w = img.GetWidth(), h = img.GetHeight();
        float cx = w / 2f, cy = h / 2f, r = w / 2f;
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                float d = new Vector2(x - cx, y - cy).Length() / r;
                if (d < from || d > to) continue;
                float here = img.GetPixel(x, y).Luminance, across = img.GetPixel(x + 1, y).Luminance - here, down = img.GetPixel(x, y + 1).Luminance - here;
                sum += across * across + down * down;
            }
        return sum;
    }

    [ScreenTest]
    static async Task Pulling_the_sharp_ring_in_blurs_the_edge_of_a_pov_picture_and_leaves_its_middle_sharp(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: Around + 1, arrange: g =>
        {
            g.VisionRadius = Reach;
            var board = g.Layer(Layers.Umpire);
            board[0].Pos = new Vec2(0, 0);
            for (int i = 1; i <= Around; i++)
            {
                double a = i * Math.Tau / Around;
                board[i].Pos = new Vec2(Math.Cos(a) * RingAt, Math.Sin(a) * RingAt);
            }
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture began");
        t.Table.SetSightingSharp(1);
        var sharp = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no sharp picture");
        t.Table.SetSightingSharp(0.3);
        var blurred = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no blurred picture");
        double edgeSharp = Detail(sharp, 0.75, 0.95), edgeBlurred = Detail(blurred, 0.75, 0.95);
        double middleSharp = Detail(sharp, 0, 0.25), middleBlurred = Detail(blurred, 0, 0.25);
        Check.That(edgeSharp > 0 && middleSharp > 0, $"the picture has detail to blur: edge {edgeSharp:0.00}, middle {middleSharp:0.00}");
        Check.That(edgeBlurred < edgeSharp * 0.5, $"the edge loses most of its detail: {edgeBlurred:0.00} of {edgeSharp:0.00}");
        Check.That(Math.Abs(middleBlurred - middleSharp) < middleSharp * 0.1, $"the middle keeps its detail: {middleBlurred:0.00} of {middleSharp:0.00}");
        t.Table.CancelSighting();
        Check.Equal(0.3, t.S.View.VisionSharp ?? 1, "the sharp ring kept with the scenario");
        Check.Equal(0, t.Errors.Count, string.Join("; ", t.Errors));
    }

    [NodeTest]
    static async Task A_distance_typed_by_the_sharp_ring_s_slider_sets_where_the_blur_starts(TestRunner r)
    {
        var h = await HudTests.Host(r);
        h.Table.SetSelection(new[] { "p1" });
        h.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var d = h.Table.Draft ?? throw new TestFailure("no POV capture began");
        var sharp = h.Hud.Pov.Panel.FindChildren("*", nameof(HSlider), true, false).OfType<HSlider>().Single(x => x.TooltipText.StartsWith("Where the picture starts to blur"));
        var box = sharp.GetParent().GetChildren().OfType<LineEdit>().Single();
        Check.That(box.Editable, "the sharp ring's distance can be typed");
        box.GrabFocus();
        box.Text = (d.Radius / 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        box.ReleaseFocus();
        await r.Frames(1);
        Check.That(Math.Abs(d.Sharp - 0.5) < SightingDraft.SharpStep, $"half the reach typed sets the ring half way, not at {d.Sharp}");
        h.Table.CancelSighting();
    }
}
