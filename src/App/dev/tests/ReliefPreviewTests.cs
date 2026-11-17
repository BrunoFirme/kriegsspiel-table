using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ReliefPreviewTests
{
    const double Side = 40, UnitsPerMetre = 0.02, Within = 0.05;
    const int MostFrames = 600, CommitFrames = 20;
    const float FirstContour = 40, ContourStep = 20, Summit = 30, Lifted = 7, Rough = 6;
    const double LowLine = 16, HighLine = 19, LineReach = 18, Far = 19, Aside = 10, HillFoot = 6, LatticeStep = 6;
    const int LatticeReach = 3;

    static readonly Vec2[] Lattice = Enumerable.Range(-LatticeReach, 2 * LatticeReach + 1)
        .SelectMany(i => Enumerable.Range(-LatticeReach, 2 * LatticeReach + 1).Select(j => new Vec2(i * LatticeStep, j * LatticeStep))).ToArray();
    static readonly Vector2 FarAway = new(-100000, -100000);

    static async Task<OfflineTable> Flat(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        await r.Frames(8);
        t.Table.SetTool(Tool.Relief);
        return t;
    }

    static SheetDef Sheet(OfflineTable t) => t.S.View.Sheets.Single();

    static double Metres(OfflineTable t, Vec2 at) => t.S.Ground.At(at) / UnitsPerMetre;

    static async Task Until(TestRunner r, Func<bool> done)
    {
        for (int i = 0; i < MostFrames && !done(); i++) await r.Frame();
    }

    static async Task Committed(TestRunner r, OfflineTable t, string before)
    {
        await Until(r, () => Sheet(t).Relief != before && !t.S.Relief.Drafting);
        await r.Frames(CommitFrames);
    }

    static void Line(OfflineTable t, double z)
    {
        foreach (double x in new[] { -LineReach, LineReach, LineReach })
        {
            t.Table.Relief.Press(new Vec2(x, z), false, false);
            t.Table.Relief.Release();
        }
    }

    static bool Same(double[] a, double[] b) => a.Zip(b).All(p => Math.Abs(p.First - p.Second) < Within);

    [NodeTest]
    static async Task Contour_lines_show_the_ground_raised_through_them_and_Raise_the_ground_lays_what_was_shown(TestRunner r)
    {
        var t = await Flat(r);
        var sha = Sheet(t).Relief!;
        var cfg = t.Table.Settings;
        cfg.ReliefMode = ReliefTool.ContoursMode;
        cfg.ReliefContourHeight = FirstContour;
        cfg.ReliefContourStep = ContourStep;
        Line(t, LowLine);
        Line(t, HighLine);
        Vec2[] spots = [new(0, LowLine), new(0, (LowLine + HighLine) / 2), new(0, HighLine)];
        await Until(r, () => Metres(t, spots[2]) > FirstContour);
        double[] shown = spots.Select(p => Metres(t, p)).ToArray();
        Check.That(Math.Abs(shown[0] - FirstContour) < 1 && Math.Abs(shown[2] - FirstContour - ContourStep) < 1, $"the ground shows the lines before it is raised: {shown[0]:0.0} m and {shown[2]:0.0} m");
        Check.That(Sheet(t).Relief == sha && t.S.Relief.Under(Sheet(t))!.Highest == 0, "and nothing is laid yet");

        cfg.ReliefMode = ReliefTool.HillMode;
        await r.Frames(2);
        Check.That(Metres(t, spots[2]) < Within && t.Table.Relief.Contours == 2, $"another tool shows the ground as it lies and keeps the lines: {Metres(t, spots[2]):0.00} m");
        cfg.ReliefMode = ReliefTool.ContoursMode;
        await Until(r, () => Metres(t, spots[2]) > FirstContour);

        t.Table.Relief.RaiseContours();
        await Committed(r, t, sha);
        double[] laid = spots.Select(p => Metres(t, p)).ToArray();
        Check.That(Sheet(t).Relief != sha && Same(shown, laid), $"Raise the ground lays what was shown: {string.Join(", ", shown.Zip(laid).Select(p => $"{p.First:0.00}/{p.Second:0.00}"))}");
        Check.Equal(0, t.Table.Relief.Contours, "the lines are used up");

        var raised = Sheet(t).Relief!;
        var across = new Vec2(0, -LowLine);
        cfg.ReliefContourHeight = FirstContour;
        Line(t, -LowLine);
        await Until(r, () => Metres(t, across) > FirstContour - 1);
        Check.That(Metres(t, across) > FirstContour - 1 && Sheet(t).Relief == raised, "a new line shows without laying");
        t.Table.Relief.ClearContours();
        await r.Frames(2);
        Check.That(Metres(t, across) < Within && Same(laid, spots.Select(p => Metres(t, p)).ToArray()) && !t.S.Relief.Drafting, "and Clear takes it off");
    }

    [NodeTest]
    static async Task A_hill_shows_while_it_is_dragged_out_follows_its_Height_and_Esc_takes_it_back(TestRunner r)
    {
        var t = await Flat(r);
        var sha = Sheet(t).Relief!;
        var cfg = t.Table.Settings;
        cfg.ReliefMode = ReliefTool.HillMode;
        cfg.ReliefFormHeight = Summit;
        cfg.ReliefProfile = ReliefForms.Round;
        var summit = new Vec2(-Aside, -Aside);
        var foot = new Vec2(-Aside + HillFoot, -Aside);
        t.Table.Relief.Press(summit, false, false);
        t.Table.Relief.Motion(foot);
        await Until(r, () => t.Table.Relief.Previewing);
        Check.That(Math.Abs(Metres(t, summit) - Summit) < Within * 4 && Sheet(t).Relief == sha, $"the hill shows at its Height while dragged, nothing laid: {Metres(t, summit):0.00} m");
        cfg.ReliefFormHeight = Summit * 2;
        await Until(r, () => Metres(t, summit) > Summit * 1.5);
        Check.That(Math.Abs(Metres(t, summit) - Summit * 2) < Within * 8, $"and follows the Height: {Metres(t, summit):0.00} m");
        Check.That(t.Table.Relief.DropShape(), "Esc drops the hill");
        await r.Frame();
        Check.That(Metres(t, summit) < Within && !t.S.Relief.Drafting, $"and the ground is as it was: {Metres(t, summit):0.00} m");

        cfg.ReliefFormHeight = Summit;
        t.Table.Relief.Press(summit, false, false);
        t.Table.Relief.Motion(foot);
        await Until(r, () => t.Table.Relief.Previewing);
        double shown = Metres(t, summit);
        t.Table.Relief.Release();
        await Committed(r, t, sha);
        Check.That(Sheet(t).Relief != sha && Math.Abs(Metres(t, summit) - shown) < Within, $"release lays the hill shown: {shown:0.00} m shown, {Metres(t, summit):0.00} m laid");
    }

    [NodeTest]
    static async Task A_sheet_pass_shows_on_the_sheet_pointed_at_and_a_click_lays_just_what_was_shown(TestRunner r)
    {
        var t = await Flat(r);
        var sha = Sheet(t).Relief!;
        var cfg = t.Table.Settings;
        var corner = new Vec2(-Far, Far);
        Vec2[] spots = [new(0, 0), new(-Aside, Aside), corner, new(Aside, -Aside)];
        cfg.ReliefMode = ReliefTool.RoughenMode;
        cfg.ReliefRough = Rough;
        Pointer.Drive(t.Table.Rig.Cam.UnprojectPosition(Coords.ToWorld(new Vec2(0, 0))));
        try { await Passes(r, t, sha, spots, corner); }
        finally { Pointer.Drive(FarAway); }
    }

    static async Task Passes(TestRunner r, OfflineTable t, string sha, Vec2[] spots, Vec2 corner)
    {
        var cfg = t.Table.Settings;
        await Until(r, () => t.Table.Relief.Previewing);
        spots = [.. spots, .. Lattice];
        double[] shown = spots.Select(p => Metres(t, p)).ToArray();
        Check.That(shown.Any(m => m > Within) && Sheet(t).Relief == sha, $"roughened ground shows on the sheet pointed at, nothing laid: {string.Join(", ", shown.Select(m => $"{m:0.00}"))} m");
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        t.Table.Relief.Release();
        await Committed(r, t, sha);
        Check.That(Same(shown, spots.Select(p => Metres(t, p)).ToArray()), "a click lays just what was shown");
        Check.That(!t.Table.Relief.Previewing, "and the pass rests on that sheet");

        var laid = Sheet(t).Relief!;
        double before = Metres(t, corner);
        cfg.ReliefMode = ReliefTool.LiftMode;
        cfg.ReliefLift = Lifted;
        await Until(r, () => t.Table.Relief.Previewing);
        Check.That(Math.Abs(Metres(t, corner) - before - Lifted) < Within, $"another pass shows over it: {Metres(t, corner) - before:0.00} m");
        Pointer.Drive(FarAway);
        await r.Frames(2);
        Check.That(!t.Table.Relief.Previewing && Math.Abs(Metres(t, corner) - before) < Within && Sheet(t).Relief == laid, "pointing off the sheets takes it off");
    }
}
