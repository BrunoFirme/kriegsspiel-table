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

    const float CloseView = 6;

    static async Task Drag(TestRunner r, BuildTool build, Vec2 from, Vec2 to)
    {
        build.Press(from, false);
        build.Motion(to);
        build.Release();
        await r.Frames(2);
    }

    [NodeTest]
    static async Task A_picked_place_is_sculpted_by_its_settings_by_adding_and_by_turning_and_sizing_a_part(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.3f;
        prefs.Naming = BuildPrefs.NoName;
        await Drag(r, build, new Vec2(0, 0), new Vec2(5, 0));
        var laid = t.S.View.Built.Single();
        prefs.Mode = BuildPrefs.Pick;
        var house = laid.Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        Check.That(build.Picked?.Id == laid.Id, "the place is picked");

        build.Reset(Settlements.Houses, 0.9);
        await r.Frames(2);
        var denser = t.S.View.Built.Single();
        Check.That(denser.Id == laid.Id && denser.Seed == laid.Seed && Houses(denser) > Houses(laid) * 2, $"Houses lays it denser under the same seed: {Houses(laid)} then {Houses(denser)}");
        build.Reset(Settlements.Walls, 0.5);
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Parts.Any(p => p.Class is "wall" or "palisade"), "Walls walls it");

        int before = t.S.View.Built.Single().Parts.Count;
        build.Arm("barn");
        build.Press(new Vec2(0.4, 0.3), false);
        build.Release();
        await r.Frames(2);
        var more = t.S.View.Built.Single();
        Check.That(more.Parts.Count == before + 1 && more.Parts[^1] is { Class: "barn", Kept: true }, "a click stands a barn in the place");
        Check.That(build.PickedPart?.Class == "barn", "and the barn is in hand");
        var barn = more.Parts[^1].At.ToList();
        Check.That(build.Turn(15), "Q and E turn it");
        await r.Frames(2);
        Check.That(!t.S.View.Built.Single().Parts[^1].At.SequenceEqual(barn), "its corners have turned");
        long Span(BuiltThing thing) => thing.Parts[^1].At.Where((_, i) => i % 2 == 0).Max() - thing.Parts[^1].At.Where((_, i) => i % 2 == 0).Min();
        long wide = Span(t.S.View.Built.Single());
        build.Arm(null);
        build.Resize(true);
        await r.Frames(2);
        Check.That(Span(t.S.View.Built.Single()) > wide, "= makes it larger");
        build.Reset(Settlements.Houses, 0.5);
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Parts.Any(p => p.Class == "barn" && p.Kept), "and it stays when the place is laid again");
    }

    [NodeTest]
    static async Task A_picked_town_has_its_wall_moved_and_gated_by_hand_is_thinned_and_filled_by_a_brush_and_is_kept_whole(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.8f;
        prefs.Walls = 0.5f;
        prefs.Plan = Plans.Grown;
        prefs.Naming = BuildPrefs.NoName;
        await Drag(r, build, new Vec2(0, 0), new Vec2(5, 0));
        var sheet = t.S.View.Sheets[0];
        BuiltThing Town() => t.S.View.Built.Single();
        Vec2 World(Spot p) => BuiltGeometry.ToWorld(sheet, p.Stored);
        var laid = Town();
        prefs.Mode = BuildPrefs.Pick;
        var house = laid.Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(sheet, new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        Check.That(build.Picked?.Id == laid.Id && build.PickedPart == null, "the town is picked whole");

        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), CloseView);
        await r.Frames(2);
        var trace = Walls.Of(laid)!;
        int corner = Enumerable.Range(0, trace.Corners.Count).MaxBy(i => trace.Gates.Min(g => (g - trace.Corners[i]).Length));
        var beyond = trace.Corners[corner] + (trace.Corners[corner] - Enclosures.Middle(trace.Corners)).Unit;
        build.Press(World(trace.Corners[corner]), false);
        build.Motion(World(beyond));
        build.Release();
        await r.Frames(2);
        Check.That(Walls.ByHand(Town()) is { } hand && hand.Corners.Any(c => (c - beyond).Length < 0.02), "dragging a corner of the wall moves the wall");
        Check.That(Houses(Town()) == Houses(laid), "and leaves the houses");

        int gates = Walls.Of(Town())!.Gates.Count;
        build.Arm(BuildTool.Gate);
        build.Press(World(beyond), false);
        build.Release();
        await r.Frames(2);
        Check.That(Walls.Of(Town())!.Gates.Count == gates + 1, "Gate opens a gate where the wall is clicked");
        build.Press(World(beyond), false);
        build.Release();
        await r.Frames(2);
        Check.That(Walls.Of(Town())!.Gates.Count == gates, "and a click on that gate closes it");

        int before = Houses(Town());
        build.Arm(Brushed.Thin);
        await Drag(r, build, new Vec2(0, 0), new Vec2(1, 0));
        int thinned = Houses(Town());
        Check.That(thinned < before && Town().Parts.Any(p => p.Kept && p.Class == "garden"), $"Thin takes houses under the brush and leaves gardens: {before} then {thinned}");
        build.Arm(Brushed.Fill);
        await Drag(r, build, new Vec2(0, 0), new Vec2(1, 0));
        Check.That(Houses(Town()) > thinned, $"Fill fronts the streets under it with houses: {thinned} then {Houses(Town())}");

        build.Arm(null);
        build.Keep(true);
        await r.Frames(2);
        int held = Houses(Town());
        build.Again();
        build.Reset(Settlements.Houses, 0.2);
        await r.Frames(2);
        Check.That(BuiltThings.Fixed(Town()) && Houses(Town()) == held, "kept whole, Again and its settings leave it alone");
        build.Keep(false);
        await r.Frames(2);
        build.Reset(Settlements.Houses, 0.2);
        await r.Frames(2);
        Check.That(Houses(Town()) < held && Walls.ByHand(Town()) != null, "let go, it is laid again inside the wall moved by hand");
    }

    [NodeTest]
    static async Task A_place_is_laid_in_its_manner_and_diving_in_brings_the_eye_close_to_stand_things_from_the_catalogue(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.5f;
        prefs.Walls = 0;
        prefs.Naming = BuildPrefs.NoName;
        prefs.Manner = Manners.Id(Manners.English);
        await Drag(r, build, new Vec2(0, 0), new Vec2(5, 0));
        BuiltThing Town() => t.S.View.Built.Single();
        Check.That(Manners.Of(Town()) == Manners.English, "the place is laid in the Manner chosen");
        prefs.Mode = BuildPrefs.Pick;
        var house = Town().Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        build.Reset(Manners.Setting, Manners.Northern);
        await r.Frames(2);
        Check.That(Manners.Of(Town()) == Manners.Northern, "its Manner is changed when picked");
        Check.That(!build.Near, "from afar the eye is not near");
        float afar = t.Table.Rig.PixelsPerUnit();
        build.DiveIn();
        await r.Frames(3);
        Check.That(t.Table.Rig.PixelsPerUnit() > afar * 4, $"Dive in brings the eye close: {afar:0.#} then {t.Table.Rig.PixelsPerUnit():0.#} pixels to the unit");
        int before = Town().Parts.Count;
        build.Arm("windmill");
        build.Press(new Vec2(0.3, 0.2), false);
        build.Release();
        await r.Frames(2);
        Check.That(Town().Parts.Count == before + 2 && Town().Parts[^2] is { Class: "mill", Kind: PartKinds.Block, Kept: true } && Town().Parts[^1] is { Class: "mill", Kind: PartKinds.Sign },
            "a windmill from the catalogue stands with its sails");
        build.Arm("cattle");
        build.Press(new Vec2(0.5, 0.4), false);
        build.Release();
        await r.Frames(2);
        Check.That(Town().Parts[^1] is { Class: "cattle", Kept: true }, "and cattle beside it");
        build.Arm(null);
        prefs.Manner = Manners.Id(Manners.Continental);
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
    }

    [NodeTest]
    static async Task A_place_takes_a_generated_or_a_typed_name_and_is_renamed_when_picked(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Mode = BuildPrefs.Places;
        prefs.Place = BuiltKinds.Settlement;
        prefs.Naming = BuildPrefs.Generated;
        prefs.Tongue = "english";
        int lettered = t.Table.MapLettering.Root.GetChildCount();
        await Drag(r, build, new Vec2(0, 0), new Vec2(4, 0));
        var laid = t.S.View.Built.Single();
        Check.That(laid.Name is { Length: >= 4 }, $"Generated names the place: {laid.Name}");
        Check.That(t.Table.MapLettering.Root.GetChildCount() > lettered, "and the name is lettered on the map");

        prefs.Mode = BuildPrefs.Pick;
        var house = laid.Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        build.Rename("  Aubach ");
        await r.Frames(2);
        Check.Equal("Aubach", t.S.View.Built.Single().Name, "a typed name replaces it");
        build.Again();
        await r.Frames(2);
        Check.Equal("Aubach", t.S.View.Built.Single().Name, "Again keeps the name");
        build.NameAfresh();
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Name is { Length: >= 4 } fresh && fresh != "Aubach", "Generate makes another");
        build.Rename("");
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Name == null, "an emptied name takes the lettering away");
        build.Cancel();

        prefs.Mode = BuildPrefs.Places;
        prefs.Naming = BuildPrefs.Typed;
        await Drag(r, build, new Vec2(-12, 10), new Vec2(-9, 10));
        Check.That(build.Naming && t.S.View.Built.Count == 1, "Typed waits for the name before it lays the place");
        var entry = t.Table.GetTree().Root.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Last(e => e.PlaceholderText == "Name");
        entry.Text = "Kirchdorf";
        entry.EmitSignal(LineEdit.SignalName.TextSubmitted, entry.Text);
        await r.Frames(2);
        Check.That(!build.Naming && t.S.View.Built.Count == 2 && t.S.View.Built[^1].Name == "Kirchdorf", "Enter lays it under the typed name");

        await Drag(r, build, new Vec2(12, 10), new Vec2(15, 10));
        Check.That(build.Naming, "a second place asks again");
        Check.That(build.Drop() && !build.Naming && t.S.View.Built.Count == 2, "and Esc drops the place with its box");
        await Drag(r, build, new Vec2(12, 10), new Vec2(15, 10));
        build.Cancel();
        await r.Frames(2);
        Check.That(!build.Naming && t.S.View.Built.Count == 3 && t.S.View.Built[^1].Name == null, "leaving the tool lays the waiting place unnamed");
        t.S.UndoLast();
        await r.Frames(2);

        prefs.Naming = BuildPrefs.NoName;
        await Drag(r, build, new Vec2(12, -12), new Vec2(15, -12));
        Check.That(t.S.View.Built.Count == 3 && t.S.View.Built[^1].Name == null, "None lays a place without a name");
        prefs.Naming = BuildPrefs.Generated;
        prefs.Place = BuildPrefs.Redoubt;
        await Drag(r, build, new Vec2(-14, -14), new Vec2(-12, -14));
        Check.That(t.S.View.Built.Count == 4 && t.S.View.Built[^1].Name == null, "a redoubt is not named");
    }

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
