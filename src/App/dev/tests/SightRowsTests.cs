using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class SightRowsTests
{
    const int Points = 512, Probes = 20000, Pixels = 300;
    const double Reach = 10;

    static bool InWhole(Vector2[] outline, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
        {
            Vector2 a = outline[i], b = outline[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    static Vector2[] Sight(Random rng, float rough) =>
        Enumerable.Range(0, Points).Select(i =>
        {
            double a = i * Math.Tau / Points, far = 40 * (1 - rough * rng.NextDouble());
            return new Vector2((float)(300 + Math.Cos(a) * far), (float)(-120 + Math.Sin(a) * far));
        }).ToArray();

    [NodeTest]
    static async Task A_point_is_in_sight_by_its_row_of_the_outline_exactly_as_by_the_whole_outline(TestRunner r)
    {
        var rng = new Random(7);
        var rows = new SightRows();
        foreach (float rough in new[] { 0f, 0.15f, 0.6f })
        {
            var outline = Sight(rng, rough);
            rows.Lay(outline);
            int kept = Enumerable.Range(0, SightRows.Rows).Max(rows.Edges), differ = 0, inside = 0;
            Check.That(kept <= SightRows.MostEdges, $"no row of this outline holds more edges than it keeps: {kept}");
            for (int i = 0; i < Probes; i++)
            {
                var p = new Vector2((float)(250 + rng.NextDouble() * 100), (float)(-170 + rng.NextDouble() * 100));
                bool whole = InWhole(outline, p);
                if (whole) inside++;
                if (rows.Holds(p) != whole) differ++;
            }
            Check.That(differ == 0 && inside > Probes / 10, $"roughness {rough}: {differ} of {Probes} points judged otherwise, {inside} in sight, at most {kept} edges to a row");
            foreach (var corner in outline.Take(64)) Check.That(rows.Holds(corner) == InWhole(outline, corner), "a point on a corner of the outline is judged the same");
        }
        rows.Lay(Array.Empty<Vector2>());
        Check.That(!rows.Holds(Vector2.Zero), "with no outline nothing is in sight");
        await r.Frame();
    }

    [ScreenTest]
    static async Task A_line_traced_across_the_view_veils_what_lies_behind_it_in_the_pov_picture(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 1, arrange: g =>
        {
            g.VisionRadius = Reach;
            g.Layer(Layers.Umpire)[0].Pos = new Vec2(0, 0);
        });
        await r.Frames(2);
        t.Table.SetSelection(new[] { "p1" });
        t.Table.SetTool(Tool.Sighting);
        await r.Frames(2);
        var d = t.Table.Draft ?? throw new TestFailure("no POV capture began");
        var open = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture");
        t.Table.Sighting.Press(new Vec2(4, -6));
        for (double z = -5.5; z <= 6; z += 0.5) t.Table.Sighting.Move(new Vec2(4, z));
        t.Table.Sighting.Release();
        await r.Frames(3);
        Check.Equal(1, d.Walls.Count, "the traced line is kept");
        var walled = await t.Table.RenderSighting(d, Pixels) ?? throw new TestFailure("no picture with the line");
        int middle = Pixels / 2, off = (int)(Pixels * 0.35);
        float behindBefore = open.GetPixel(middle + off, middle).Luminance, behind = walled.GetPixel(middle + off, middle).Luminance;
        float frontBefore = open.GetPixel(middle - off, middle).Luminance, front = walled.GetPixel(middle - off, middle).Luminance;
        Check.That(behindBefore > 0.1f && behind < 0.06f, $"the table behind the line is veiled: {behindBefore:0.00} then {behind:0.00}");
        Check.That(Math.Abs(front - frontBefore) < 0.02f, $"the table on the near side is as it was: {frontBefore:0.00} then {front:0.00}");
        t.Table.CancelSighting();
    }
}
