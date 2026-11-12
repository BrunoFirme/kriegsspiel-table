using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Client;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BuildToolTests
{
    const double Side = 40;
    const int Reach = 12;
    const double Clear = 0.2;

    static async Task<OfflineTable> Ground(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        await r.Frames(8);
        t.Table.SetTool(Tool.Build);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
        await r.Frames(2);
        return t;
    }

    static int Houses(BuiltThing thing) => thing.Parts.Count(p => p.Class == "house");

    [NodeTest]
    static async Task A_dragged_settlement_is_laid_picked_rerolled_moved_and_removed_each_in_one_undo_step(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.6f;
        build.Press(new Vec2(0, 0), false);
        build.Motion(new Vec2(4, 0));
        build.Release();
        await r.Frames(2);
        var laid = t.S.View.Built.Single();
        Check.Equal(BuiltKinds.Settlement, laid.Kind, "a settlement is laid");
        Check.That(Houses(laid) > 10, $"with houses: {Houses(laid)}");
        Check.Equal("g", laid.Sheet, "on the sheet under the drag");

        prefs.Place = BuildPrefs.Redoubt;
        build.Press(new Vec2(-14, -14), false);
        build.Motion(new Vec2(-12, -14));
        build.Release();
        await r.Frames(2);
        Check.That(Forts.Plain(t.S.View.Built[^1]), "Redoubt lays a plain work");
        t.S.UndoLast();
        await r.Frames(2);

        prefs.Mode = BuildPrefs.Pick;
        var house = laid.Parts.First(p => p.Class == "house");
        var at = BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1]));
        build.Press(at, false);
        build.Release();
        Check.That(build.Picked?.Id == laid.Id, "a click on a house picks the place");
        build.Again();
        await r.Frames(2);
        var again = t.S.View.Built.Single();
        Check.That(again.Id == laid.Id && again.Seed != laid.Seed, "Again lays it afresh under the same id");

        var first = again.Parts.First(p => p.Class == "house");
        var grab = BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(first.At[0], first.At[1]));
        build.Press(grab, false);
        build.Release();
        Check.That(build.PickedPart?.Class == "house", "a second click takes the house under it");
        build.Press(grab, false);
        build.Motion(grab + new Vec2(Reach, 0));
        build.Release();
        await r.Frames(2);
        var moved = t.S.View.Built.Single().Parts.First(p => p.Class == "house");
        var now = BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(moved.At[0], moved.At[1]));
        Check.That(System.Math.Abs(now.X - grab.X - Reach) < 0.01 && System.Math.Abs(now.Z - grab.Z) < 0.01, $"a drag moves the picked house alone: {now.X - grab.X:0.00}");
        Check.That(moved.Kept, "the house moved by hand is kept");
        build.Again();
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Parts.Any(p => p.Kept && p.At[0] == moved.At[0]), "and stays through another Again");

        var whole = t.S.View.Built.Single();
        var sheet = t.S.View.Sheets[0];
        var between = Enumerable.Range(-40, 81).SelectMany(x => Enumerable.Range(-40, 81).Select(z => new Vec2(x * 0.1, z * 0.1)))
            .First(p => BuiltThings.StandsOver(whole, Spot.Of(BuiltGeometry.ToSheet(sheet, p))) && BuiltThings.Nearest(whole, Spot.Of(BuiltGeometry.ToSheet(sheet, p))).Reach > Clear);
        build.Cancel();
        prefs.Mode = BuildPrefs.Places;
        build.Press(between, false, picks: true);
        build.Release();
        Check.That(build.Picked?.Id == whole.Id && prefs.Mode == BuildPrefs.Pick, "Alt+click between its houses picks the place from another mode");
        build.Press(between, false);
        build.Motion(between + new Vec2(Reach, 0));
        build.Release();
        await r.Frames(2);
        int Square(BuiltThing b) => b.Parts.First(p => p.Class == "square").At[0];
        Check.Equal(Reach * BuiltGeometry.PerUnit, System.Math.Abs(Square(t.S.View.Built.Single()) - Square(whole)), "a drag on the picked place moves it whole");

        Check.That(build.Delete(), "Delete removes what is picked");
        await r.Frames(2);
        Check.Equal(0, t.S.View.Built.Count, "nothing is left");
        t.S.UndoLast();
        await r.Frames(2);
        Check.Equal(1, t.S.View.Built.Count, "one undo brings the place back");
    }

    [NodeTest]
    static async Task A_road_is_clicked_out_and_a_building_and_details_are_laid(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Roads;
        prefs.Line = "road";
        prefs.FindsWay = false;
        build.Press(new Vec2(-15, -5), false);
        build.Press(new Vec2(0, -5), false);
        build.Press(new Vec2(15, -4), false);
        build.Press(new Vec2(15, -4), true);
        await r.Frames(2);
        var road = t.S.View.Built.Single();
        Check.Equal(BuiltKinds.Road, road.Kind, "a road is laid on the double-click");
        Check.Equal(3 * 2, road.Outline.Count, "through its three waypoints");

        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Building;
        prefs.Block = "church";
        build.Press(new Vec2(5, 5), false);
        build.Release();
        await r.Frames(2);
        Check.That(t.S.View.Built.Any(b => b.Kind == BuiltKinds.Building && b.Parts.Any(p => p.Class == "church")), "a click stands a church");

        prefs.Mode = BuildPrefs.Details;
        prefs.Plots = 0.7f;
        prefs.Buildings = 0.8f;
        prefs.Brush = 8;
        build.Press(new Vec2(-12, 6), false);
        build.Motion(new Vec2(0, 8));
        build.Motion(new Vec2(12, 6));
        build.Release();
        await r.Frames(2);
        var details = t.S.View.Built.FirstOrDefault(b => b.Kind == BuiltKinds.Details);
        Check.That(details != null && details.Parts.Any(p => p.Class == "farm") && details.Parts.Any(p => p.Class is "hedge" or "fence"), "a details stroke lays farms and boundaries");
        Check.That(details!.Parts.Any(p => p.Class == "track"), "and joins a farm to the road");
    }

    [NodeTest]
    static async Task A_host_s_table_turns_painted_roads_and_towns_into_built_things_in_one_undo_step(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        var old = ReliefGrid.Flat(Side, Side);
        System.Array.Fill(old.Kind, (byte)1);
        for (int x = old.Cols / 8; x < old.Cols * 7 / 8; x++)
            for (int y = old.Rows / 2 - 1; y <= old.Rows / 2 + 1; y++) old.Kind[y * old.Cols + x] = GroundKinds.Road;
        for (int y = 0; y < old.Rows; y++)
            for (int x = 0; x < old.Cols; x++)
                if ((x - old.Cols / 3) * (x - old.Cols / 3) + (y - old.Rows / 4) * (y - old.Rows / 4) < 40 * 40) old.Kind[y * old.Cols + x] = GroundKinds.Town;
        var (sha, _) = ReliefImport.Save(old, t.S.Store.Root);
        t.S.Relief.Remember(sha, old);
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        for (int i = 0; i < 240 && t.S.View.Built.Count == 0; i++) await r.Frame();
        Check.That(t.S.View.Built.Any(b => b.Kind == BuiltKinds.Road), "the painted road is a built road");
        Check.That(t.S.View.Built.Any(b => b.Kind == BuiltKinds.Settlement && Houses(b) > 5), "the painted town is a built settlement");
        var sheet = t.S.View.Sheets.Single();
        Check.That(sheet.Relief != sha, "the sheet has taken the cleaned ground");
        await r.Frames(20);
        var now = t.S.Relief.Grid(sheet);
        Check.That(now != null && !now.Kind.Any(k => k is GroundKinds.Road or GroundKinds.Town), "with no painted road or town left in it");
        int built = t.S.View.Built.Count;
        await r.Frames(20);
        Check.Equal(built, t.S.View.Built.Count, "the cleaned ground is not converted again");
        t.S.UndoLast();
        await r.Frames(4);
        Check.That(t.S.View.Built.Count == 0 && t.S.View.Sheets.Single().Relief == sha, "one undo gives the painted ground back");
    }

    [NodeTest]
    static async Task Nothing_is_built_before_the_map_has_a_scale(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Size = new Size2(Side, Side) } });
        await r.Frames(4);
        t.Table.SetTool(Tool.Build);
        t.Table.Build.Press(new Vec2(0, 0), false);
        t.Table.Build.Motion(new Vec2(4, 0));
        t.Table.Build.Release();
        await r.Frames(2);
        Check.Equal(0, t.S.View.Built.Count, "an unscaled map takes no built thing");
    }
}
