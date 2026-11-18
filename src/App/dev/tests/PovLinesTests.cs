using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;

namespace KTable.App;

static class PovLinesTests
{
    const string U = Layers.Umpire;
    const double Reach = 10, Behind = 6, LineAt = 3, RingAt = 8.5;
    const int Pixels = 400, Around = 12;

    static string InSight(SightingDraft d) => string.Join(",", d.Included.OrderBy(x => x));

    static List<Vec2> Across(double x) => new() { new(x, -Reach), new(x, Reach) };

    static List<Vec2> Box(Vec2 c, double half) => new() { new(c.X - half, c.Z - half), new(c.X + half, c.Z - half), new(c.X + half, c.Z + half), new(c.X - half, c.Z + half) };

    [NodeTest]
    static async Task A_line_blocks_blurs_or_shows_what_lies_past_it_as_its_kind_says(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 4, arrange: g =>
        {
            g.VisionRadius = Reach;
            var board = g.Layer(U);
            board[0].Pos = new Vec2(0, 0);
            board[1].Pos = new Vec2(Behind, 0);
            board[2].Pos = new Vec2(-Behind, 0);
            board[3].Pos = new Vec2(0, Behind);
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var s = t.Table.Sighting;
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture began");
        Check.Equal("p1,p2,p3,p4", InSight(d), "with nothing traced all four are in sight");
        Check.That(s.Trace(Across(LineAt)), "a block line is traced");
        s.SetLineKind(SightLine.Blurs);
        Check.That(s.Trace(Across(-LineAt)) && d.Lines[^1] is { Kind: SightLine.Blurs, Blur: > 0 }, "a blur line takes the kind picked and its strength");
        Check.That(t.Table.Settings.SightingLines == SightLine.Blurs && d.BlursPastLines, "the kind picked is remembered and the picture blurs past the line");
        Check.That(s.Trace(new() { new(-Reach, LineAt), new(Reach, LineAt) }, SightLine.Block), "a second block line, north");
        Check.Equal("p1,p3", InSight(d), "what is behind a block line is left out; what is behind a blur line stays in");
        Check.That(s.Hint().Contains("blurs"), "the hint line says what the next line does: " + s.Hint());
        Check.That(s.Trace(Box(new Vec2(0, Behind), 1), SightLine.Shows), "a view outline is drawn round the piece to the north");
        Check.Equal("p1,p3,p4", InSight(d), "and shows it over the line that hid it");
        s.UndoStroke();
        Check.Equal("p1,p3", InSight(d), "undo takes the outline back");
        s.RedoStroke();
        Check.Equal("p1,p3,p4", InSight(d), "and redo puts it back");
        s.ClearOutline();
        Check.Equal("p1,p2,p3,p4", InSight(d), "clear takes every line away");
        t.Table.CancelSighting();
    }

    const float InsideRim = 0.85f;

    static double Detail(Image img, Func<float, bool> across)
    {
        double sum = 0;
        int w = img.GetWidth(), h = img.GetHeight();
        for (int y = 0; y < h - 1; y++)
            for (int x = 0; x < w - 1; x++)
            {
                float u = (x - w / 2f) / (w / 2f), v = (y - h / 2f) / (h / 2f);
                if (!across(u) || u * u + v * v > InsideRim * InsideRim) continue;
                float here = img.GetPixel(x, y).Luminance, right = img.GetPixel(x + 1, y).Luminance - here, down = img.GetPixel(x, y + 1).Luminance - here;
                sum += right * right + down * down;
            }
        return sum;
    }

    [ScreenTest]
    static async Task Past_a_blur_line_the_pov_picture_loses_its_detail_and_short_of_it_keeps_it(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: Around + 1, arrange: g =>
        {
            g.VisionRadius = Reach;
            var board = g.Layer(U);
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
        var sharp = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no sharp picture");
        t.Table.Sighting.SetLineBlur(SightingDraft.MostBlur / 3);
        Check.That(t.Table.Sighting.Trace(Across(LineAt), SightLine.Blurs), "a blur line is traced to the east");
        await r.Frames(2);
        var blurred = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no blurred picture");
        bool East(float x) => x > 0.45f;
        bool West(float x) => x < -0.1f;
        double eastSharp = Detail(sharp, East), eastBlurred = Detail(blurred, East), westSharp = Detail(sharp, West), westBlurred = Detail(blurred, West);
        Check.That(eastSharp > 0 && westSharp > 0, $"the picture has detail to blur: east {eastSharp:0.00}, west {westSharp:0.00}");
        Check.That(eastBlurred < eastSharp * 0.5, $"past the line it loses most of its detail: {eastBlurred:0.00} of {eastSharp:0.00}");
        Check.That(Math.Abs(westBlurred - westSharp) < westSharp * 0.1, $"short of it the detail stays: {westBlurred:0.00} of {westSharp:0.00}");
        t.Table.CancelSighting();
    }
}
