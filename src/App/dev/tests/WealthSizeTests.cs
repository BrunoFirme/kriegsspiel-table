using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class WealthSizeTests
{
    const double Side = 40, PerMetre = 0.02, LongM = 20, DeepM = 16, BehindM = 2.1, Grown = 1.1;
    const int Waits = 400, Clicks = 3;

    static async Task Raised(TestRunner r)
    {
        await r.Frames(2);
        for (int i = 0; i < Waits && TableRaised.Busy; i++) await r.Frames(1);
        await r.Frames(2);
    }

    static async Task<bool> Roofed(TestRunner r, HudTests.Rig h, Vector3 at, bool roofed)
    {
        for (int i = 0; i < Waits && h.Table.Raised.Ceiling(at) != null != roofed; i++) await r.Frames(1);
        return h.Table.Raised.Ceiling(at) != null == roofed;
    }

    static Vector2 Drawn(HudTests.Rig h, SheetDef sheet) =>
        BuiltPainter.ShapesOf(h.S.View.Built.Single(), Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet), (float)PerMetre).Single().Box.Size;

    [NodeTest]
    static async Task A_picked_house_made_rich_stands_larger_on_the_map_and_raised_and_undo_gives_back_its_laid_size(TestRunner r)
    {
        var h = await HudTests.Host(r, pieces: 0);
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, h.S.Store.Root);
        h.S.Relief.Remember(sha, flat);
        h.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        h.S.Do(new SetCalibration { UnitsPerMeter = PerMetre });
        var medieval = Json.Clone(h.S.View.Lighting);
        (medieval.MapStyle, medieval.Raised) = (MapStyles.Medieval, true);
        h.S.Do(new SetLighting { Lighting = medieval });
        await r.Frames(4);
        var sheet = h.S.View.Sheets.Single();
        var centre = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0)));
        var house = BuiltThings.Building(BuildTool.Site(h.S, sheet, manner: Manners.Gothic), sheet.Id, "house", centre, new Spot(1, 0), LongM * PerMetre, DeepM * PerMetre);
        h.S.Do(new LayBuilt { Thing = house });
        await Raised(r);
        var behind = Coords.ToWorld(BuiltGeometry.ToWorld(sheet, (centre - new Spot(0, (DeepM / 2 + BehindM) * PerMetre)).Stored), 0);
        var laid = Drawn(h, sheet);
        Check.That(h.Table.Raised.Ceiling(behind) == null, "behind the laid house there is no roof");
        h.Table.SetTool(Tool.Build);
        h.Table.Settings.Build.Do(BuildPrefs.Pick);
        var built = h.S.View.Built.Single();
        for (int click = 0; click < Clicks && h.Table.Build.PickedParts.Count == 0; click++)
        {
            h.Table.Build.Press(BuiltGeometry.ToWorld(sheet, new SheetPoint(built.Parts[0].At[0], built.Parts[0].At[1])), false);
            h.Table.Build.Release();
            await r.Frames(2);
        }
        Check.That(h.Table.Build.Picked?.Id == built.Id && h.Table.Build.PickedParts.Contains(0), "the house is picked");
        h.Table.Build.Zone.Wealth(WealthLevel.Rich);
        await Raised(r);
        Check.That(h.S.View.Built.Single().Parts[0].Wealth == "rich", "the house is rich");
        var rich = Drawn(h, sheet);
        Check.That(rich.X > laid.X * Grown || rich.Y > laid.Y * Grown, $"a rich house is drawn larger on the map: {laid} to {rich}");
        Check.That(await Roofed(r, h, behind, true), "and raised over the ground behind where it was laid");
        h.S.UndoLast();
        await Raised(r);
        Check.That(Drawn(h, sheet) == laid && h.S.View.Built.Single().Parts[0].At.SequenceEqual(built.Parts[0].At), "undone, it is drawn at its laid size");
        Check.That(await Roofed(r, h, behind, false), "and raised on its laid outline");
    }
}
