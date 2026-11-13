using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ReliefHoldTests
{
    const double Side = 40, UnitsPerMetre = 0.02;
    const int HeldFrames = 90, CommitFrames = 20, MostCommitFrames = 600;
    const float Ceiling = 5, Rate = 50, Within = 0.05f, Terrace = 12;
    const double Aside = 10, HillFoot = 6;
    const float Summit = 30, FirstContour = 40, ContourStep = 20, Lifted = 7;
    const double Rise = 0.01;
    const double LowLine = 16, HighLine = 19, LineReach = 18, Far = 19;

    [NodeTest]
    static async Task A_held_raise_climbs_in_metres_and_stops_at_its_ceiling(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        string last = sha;
        async Task Committed()
        {
            for (int i = 0; i < MostCommitFrames && (t.S.View.Sheets.Single().Relief == last || t.S.Relief.Grid(t.S.View.Sheets.Single()) == null); i++) await r.Frame();
            last = t.S.View.Sheets.Single().Relief ?? last;
            await r.Frames(CommitFrames);
        }
        await r.Frames(8);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "raise";
        cfg.ReliefBrush = 4;
        cfg.ReliefRate = Rate;
        cfg.ReliefUpTo = Ceiling;
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        await r.Frames(2);
        double early = t.S.Ground.At(new Vec2(0, 0)) / UnitsPerMetre;
        Check.That(early > 0 && early < Ceiling, $"it climbs while held, {early:0.00} m after two frames");
        await r.Frames(HeldFrames);
        t.Table.Relief.Release();
        await Committed();
        double top = t.S.Ground.At(new Vec2(0, 0)) / UnitsPerMetre;
        Check.That(System.Math.Abs(top - Ceiling) < Within, $"and stops at {Ceiling} m: {top:0.00} m");
        Check.That(t.S.View.Sheets.Single().Relief != sha, "the raised ground is the sheet's relief");

        cfg.ReliefMode = ReliefTool.LevelMode;
        cfg.ReliefLevel = Terrace;
        t.Table.Relief.Press(new Vec2(Aside, Aside), false, false);
        t.Table.Relief.Release();
        await Committed();
        double laid = t.S.Ground.At(new Vec2(Aside, Aside)) / UnitsPerMetre;
        Check.That(System.Math.Abs(laid - Terrace) < Within, $"Set height lays {Terrace} m: {laid:0.00} m");
        cfg.ReliefMode = "smooth";
        t.Table.Relief.Press(new Vec2(0, 0), false, pick: true);
        Check.That(cfg.ReliefMode == ReliefTool.LevelMode && System.Math.Abs(cfg.ReliefLevel - Ceiling) < Within * 2, $"Alt+click takes the height there: {cfg.ReliefLevel:0.0} m");

        cfg.ReliefMode = ReliefTool.HillMode;
        cfg.ReliefFormHeight = Summit;
        cfg.ReliefProfile = ReliefForms.Round;
        t.Table.Relief.Press(new Vec2(-Aside, -Aside), false, false);
        t.Table.Relief.Motion(new Vec2(-Aside + HillFoot, -Aside));
        t.Table.Relief.Release();
        await Committed();
        double summit = t.S.Ground.At(new Vec2(-Aside, -Aside)) / UnitsPerMetre;
        Check.That(System.Math.Abs(summit - Summit) < Within * 4, $"a hill dragged out stands {Summit} m: {summit:0.00} m");
        Check.That(t.S.Ground.At(new Vec2(-Aside + HillFoot + 1, -Aside)) < 1e-6, "and ends at its foot");

        cfg.ReliefMode = ReliefTool.ContoursMode;
        cfg.ReliefContourHeight = FirstContour;
        cfg.ReliefContourStep = ContourStep;
        foreach (double z in new[] { LowLine, HighLine })
            foreach (double x in new[] { -LineReach, LineReach, LineReach })
            {
                t.Table.Relief.Press(new Vec2(x, z), false, false);
                t.Table.Relief.Release();
            }
        Check.Equal(2, t.Table.Relief.Contours, "two contour lines are kept");
        Check.That(cfg.ReliefContourHeight == FirstContour + 2 * ContourStep, "the height steps after each line");
        t.Table.Relief.RaiseContours();
        await Committed();
        double Metres(double z) => t.S.Ground.At(new Vec2(0, z)) / UnitsPerMetre;
        Check.That(System.Math.Abs(Metres(LowLine) - FirstContour) < 1 && System.Math.Abs(Metres(HighLine) - FirstContour - ContourStep) < 1,
            $"the ground passes through the lines: {Metres(LowLine):0.0} m and {Metres(HighLine):0.0} m");
        double between = Metres((LowLine + HighLine) / 2);
        Check.That(between > FirstContour + 2 && between < FirstContour + ContourStep - 2, $"and grades between them: {between:0.0} m");
        Check.Equal(0, t.Table.Relief.Contours, "the lines are used up");

        double corner = t.S.Ground.At(new Vec2(-Far, Far)) / UnitsPerMetre;
        cfg.ReliefMode = ReliefTool.LiftMode;
        cfg.ReliefLift = Lifted;
        for (int i = 0; i < MostCommitFrames && t.S.Relief.Grid(t.S.View.Sheets.Single()) == null; i++) await r.Frame();
        for (int i = 0; i < 2; i++)
        {
            t.Table.Relief.Press(new Vec2(0, 0), false, false);
            t.Table.Relief.Release();
        }
        for (int i = 0; i < MostCommitFrames && t.S.Relief.Drafting; i++) await r.Frame();
        double lifted = t.S.Ground.At(new Vec2(-Far, Far)) / UnitsPerMetre;
        Check.That(System.Math.Abs(lifted - corner - 2 * Lifted) < Within, $"Lift all twice at once raises the whole sheet by {2 * Lifted} m, the later save landing last: {lifted - corner:0.00} m");
    }

    [NodeTest]
    static async Task An_older_save_landing_late_leaves_the_newer_edit_on_show_until_its_own_save_lands(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var root = t.S.Store.Root;
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        await r.Frames(8);
        var older = flat.Copy();
        ReliefFinish.Lift(older, Rise);
        var newer = older.Copy();
        ReliefFinish.Lift(newer, Rise);
        var (olderSha, _) = ReliefImport.Save(older, root);
        var (newerSha, _) = ReliefImport.Save(newer, root);
        t.S.Relief.Remember(olderSha, older);
        t.S.Relief.Remember(newerSha, newer);
        var at = new Vec2(0, 0);
        t.S.Relief.Draft("g", newer);
        double shown = t.S.Ground.At(at);
        t.S.Do(new SetSheetRelief { Id = "g", Relief = olderSha, Height = 1 });
        await r.Frames(2);
        Check.That(t.S.Relief.Drafting && System.Math.Abs(t.S.Ground.At(at) - shown) < Within, "the older save leaves the newer edit on show");
        t.S.Do(new SetSheetRelief { Id = "g", Relief = newerSha, Height = 1 });
        await r.Frames(2);
        Check.That(!t.S.Relief.Drafting && System.Math.Abs(t.S.Ground.At(at) - shown) < Within, "and the edit's own save takes its place");
    }

    [NodeTest]
    static Task A_square_or_diamond_brush_s_outline_turns_with_the_sheet_it_paints(TestRunner r)
    {
        const double Radius = 2, Turn = 30;
        var sheet = new SheetDef { Id = "g", Size = new Size2(Side, Side), Pos = new Vec2(3, -4), RotDeg = Turn };
        var at = new Vec2(5, -1);
        var (cx, cz) = Ground.Local(sheet, at);
        var square = ReliefTool.TipOutline(at, Radius, BrushTip.Square, Turn).Select(p => Ground.Local(sheet, p)).ToList();
        static bool Near(double a, double b) => System.Math.Abs(a - b) < 1e-9;
        Check.That(square.All(p => Near(System.Math.Abs(p.x - cx), Radius) && Near(System.Math.Abs(p.z - cz), Radius)), "a square brush's corners lie on the sheet's own axes");
        var diamond = ReliefTool.TipOutline(at, Radius, BrushTip.Diamond, Turn).Select(p => Ground.Local(sheet, p)).Select(p => (x: System.Math.Abs(p.x - cx), z: System.Math.Abs(p.z - cz))).ToList();
        Check.That(diamond.All(p => Near(p.x + p.z, Radius) && Near(System.Math.Min(p.x, p.z), 0)), "a diamond brush's points lie on the sheet's own axes");
        return Task.CompletedTask;
    }

    [NodeTest]
    static async Task Heights_tints_the_ground_only_while_the_Shape_tab_is_in_hand(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frames(2);
        bool Shown() => t.Table.Relief.HeightsShown;
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "raise";
        t.Table.SetTool(Tool.Relief);
        await r.Frames(2);
        Check.That(!Shown(), "the ground is untinted until Heights is ticked");
        t.Table.Relief.Heights = true;
        await r.Frames(2);
        Check.That(Shown(), "ticked, the ground is tinted by height");
        cfg.ReliefMode = "paint:grass";
        await r.Frames(2);
        Check.That(!Shown(), "on the Paint tab it is not");
        cfg.ReliefMode = "raise";
        await r.Frames(2);
        Check.That(Shown(), "back on Shape it is again");
        t.Table.SetTool(Tool.Select);
        await r.Frames(2);
        Check.That(!Shown(), "and with another tool in hand the map is itself");
        t.Table.Relief.Heights = false;
    }
}
