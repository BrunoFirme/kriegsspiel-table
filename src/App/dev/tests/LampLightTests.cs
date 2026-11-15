using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class LampLightTests
{
    const double Side = 40, TownReach = 5;
    const int TownSeed = 7, Settle = 12;

    [NodeTest]
    static async Task Lamps_light_what_is_built_when_the_light_is_low_and_not_by_day_or_when_turned_off(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        await r.Frames(4);
        var sheet = t.S.View.Sheets[0];
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 0.8, 0.5, 0) });
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side / 4);
        await r.Frames(Settle);
        Check.That(!t.Table.Lamps.Root.Visible && t.Table.Lamps.Glow == 0, "by day nothing is lit");
        var night = t.S.View.Lighting.Clone();
        (night.SunEnergy, night.Ambient, night.FromClock) = (0, 0.3, false);
        t.S.Do(new SetLighting { Lighting = night });
        await r.Frames(Settle);
        Check.That(t.Table.Lamps.Root.Visible && t.Table.Lamps.Glow > 0.99f, $"at night the lamps are lit: {t.Table.Lamps.Glow:0.00}");
        Check.That(TableLamps.Laid > 20, $"{TableLamps.Laid} lamps laid");
        Check.That(t.Table.Lamps.Lights.Count(l => l.Visible && !l.ShadowEnabled && l.LightEnergy > 0 && !l.DistanceFadeEnabled) == TableLamps.Laid, "every lamp is an engine light without shadows, lit at any distance by default");
        t.Table.Settings.LampReachM = Settings.LampReachLeast;
        await r.Frames(2);
        Check.That(t.Table.Lamps.Lights.All(l => l.DistanceFadeEnabled), "and fading out past a reach set lower");
        t.Table.Settings.LampReachM = Settings.LampReachOff;
        var dark = night.Clone();
        dark.LampsOut = true;
        t.S.Do(new SetLighting { Lighting = dark });
        await r.Frames(Settle);
        Check.That(!t.Table.Lamps.Root.Visible, "with the scenario's lamps turned off nothing is lit");
        t.S.Do(new SetLighting { Lighting = night });
        await r.Frames(Settle);
        int all = TableLamps.Laid;
        t.Table.SetTool(Tool.Build);
        t.Table.Settings.Build.Mode = BuildPrefs.Lamps;
        t.Table.Settings.Build.LampsOn = false;
        t.Table.Settings.Build.Brush = (float)TownReach;
        Brush(t);
        await r.Frames(Settle);
        Check.That(TableLamps.Laid < all, $"the brush puts out the lamps under it: {TableLamps.Laid} of {all} lit");
        Check.That(t.S.View.Built.Any(b => b.Parts.Any(p => p.Dark != null)), "and the town keeps which are out");
        t.Table.Settings.Build.LampsOn = true;
        Brush(t);
        await r.Frames(Settle);
        Check.Equal(all, TableLamps.Laid, "lit again by the brush");
        Check.That(t.S.View.Built.All(b => b.Parts.All(p => p.Dark == null)), "and none kept out");
    }

    static void Brush(OfflineTable t)
    {
        t.Table.Build.Press(new Vec2(-TownReach, 0), false);
        t.Table.Build.Motion(new Vec2(0, 0));
        t.Table.Build.Motion(new Vec2(TownReach, 0));
        t.Table.Build.Release();
    }
}
