using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class ReliefHoldTests
{
    const double Side = 40, UnitsPerMetre = 0.02;
    const int HeldFrames = 90, CommitFrames = 20;
    const float Ceiling = 5, Rate = 50, Within = 0.05f, Terrace = 12;
    const double Aside = 10;

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
        await r.Frames(CommitFrames);
        double top = t.S.Ground.At(new Vec2(0, 0)) / UnitsPerMetre;
        Check.That(System.Math.Abs(top - Ceiling) < Within, $"and stops at {Ceiling} m: {top:0.00} m");
        Check.That(t.S.View.Sheets.Single().Relief != sha, "the raised ground is the sheet's relief");

        cfg.ReliefMode = ReliefTool.LevelMode;
        cfg.ReliefLevel = Terrace;
        t.Table.Relief.Press(new Vec2(Aside, Aside), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        double laid = t.S.Ground.At(new Vec2(Aside, Aside)) / UnitsPerMetre;
        Check.That(System.Math.Abs(laid - Terrace) < Within, $"Set height lays {Terrace} m: {laid:0.00} m");
        cfg.ReliefMode = "smooth";
        t.Table.Relief.Press(new Vec2(0, 0), false, pick: true);
        Check.That(cfg.ReliefMode == ReliefTool.LevelMode && System.Math.Abs(cfg.ReliefLevel - Ceiling) < Within * 2, $"Alt+click takes the height there: {cfg.ReliefLevel:0.0} m");
    }
}
