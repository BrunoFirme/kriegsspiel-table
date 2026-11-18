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

    static async Task<OfflineTable> Ground(TestRunner r, double grade = 0)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < flat.Rows; y++)
            for (int x = 0; x < flat.Cols; x++) flat.H[y * flat.Cols + x] = (float)(grade * y / (flat.Rows - 1));
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

    const double TownReach = 5;
    const int TownSeed = 7, MostSurfacesOfAMesh = 256, PartsOfABigTown = 300;

    [NodeTest]
    static async Task A_picked_town_of_many_parts_is_outlined_in_one_mesh_surface(TestRunner r)
    {
        var t = await Ground(r);
        var sheet = t.S.View.Sheets[0];
        var town = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0);
        Check.That(town.Parts.Count > PartsOfABigTown, $"the town has {town.Parts.Count} parts");
        t.S.Do(new LayBuilt { Thing = town });
        await r.Frames(2);
        t.Table.Settings.Build.Do(BuildPrefs.Pick);
        var house = town.Parts.First(p => p.Class == "house");
        t.Table.Build.Press(BuiltGeometry.ToWorld(sheet, new SheetPoint(house.At[0], house.At[1])), false);
        t.Table.Build.Release();
        await r.Frames(2);
        int surfaces = t.Table.Overlay.Mesh.GetSurfaceCount();
        Check.That(t.Table.Build.Picked != null && surfaces > 0 && surfaces < MostSurfacesOfAMesh / 4, $"the picked town's outline takes {surfaces} surfaces");
    }

    [NodeTest]
    static async Task A_second_click_picks_the_whole_building_a_third_one_part_and_a_building_moves_and_goes_as_one(TestRunner r)
    {
        var t = await Ground(r);
        var sheet = t.S.View.Sheets[0];
        var site = BuildTool.Site(t.S, sheet);
        var town = Enumerable.Range(1, 12).Select(seed => Settlements.Lay(site, sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, seed, 0.5, 0, 0))
            .First(laid => laid.Parts.Any(p => p.Class == "church" && p.Group > 0) && laid.Parts.Any(p => p.Class == "steeple"));
        t.S.Do(new LayBuilt { Thing = town });
        await r.Frames(2);
        var build = t.Table.Build;
        t.Table.Settings.Build.Do(BuildPrefs.Pick);
        var church = town.Parts.First(p => p.Class == "church");
        var at = BuiltGeometry.ToWorld(sheet, BuiltSite.Round(church).At.Stored);
        void Click()
        {
            build.Press(at, false);
            build.Release();
        }
        BuiltThing Laid() => t.S.View.Built.Single();
        Click();
        Check.That(build.Picked != null && build.PickedParts.Count == 0, "the first click picks the whole place");
        Click();
        var whole = build.PickedParts.ToList();
        Check.That(whole.Count > 1 && whole.All(i => Laid().Parts[i].Group == church.Group) && whole.Any(i => Laid().Parts[i].Class == "steeple") && build.PickedPart == null,
            $"the second picks the church with its steeple and its yard ({whole.Count} parts)");
        Click();
        Check.That(build.PickedPart?.Class == "church" && build.PickedParts.Count == 1, "the third picks the church alone, not the yard it stands in");
        Click();
        Check.That(build.Picked != null && build.PickedParts.Count == 0, "the fourth picks the whole place again");
        Click();
        var before = Laid();
        await Drag(r, build, at, at + new Vec2(Reach, Reach / 2));
        var after = Laid();
        var shift = (X: after.Parts[whole[0]].At[0] - before.Parts[whole[0]].At[0], Y: after.Parts[whole[0]].At[1] - before.Parts[whole[0]].At[1]);
        Check.That(shift.X != 0 && whole.All(i => after.Parts[i].Kept && after.Parts[i].At[0] - before.Parts[i].At[0] == shift.X && after.Parts[i].At[1] - before.Parts[i].At[1] == shift.Y),
            "a drag moves the building as one and keeps it");
        Check.That(Enumerable.Range(0, before.Parts.Count).Except(whole).All(i => after.Parts[i].At.SequenceEqual(before.Parts[i].At)), "and nothing else moves");
        Check.That(build.Turn(30) && whole.All(i => !Laid().Parts[i].At.SequenceEqual(after.Parts[i].At) || Laid().Parts[i].At.Count == 2), "Q and E turn the building about its middle");
        await r.Frames(2);
        Check.That(build.Delete() && !Laid().Parts.Any(p => p.Kept) && Laid().Parts.Count == before.Parts.Count - whole.Count, "Delete removes the building, yard and steeple with it");
    }

    [NodeTest]
    static async Task A_road_drawn_from_a_dead_end_carries_it_on_and_takes_the_surface_chosen(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Roads);
        prefs.Line = "road";
        prefs.FindsWay = false;
        prefs.Surface = Looks.Cobbles;
        build.Press(new Vec2(-15, -5), true);
        build.Release();
        build.Press(new Vec2(0, -5), false);
        await r.Frames(2);
        var first = t.S.View.Built.Single();
        Check.Equal(Looks.Cobbles, first.Parts.First(p => p.Kind == PartKinds.Line).Surface, "a road is laid in the surface chosen");
        prefs.Surface = "";
        build.Press(new Vec2(0.4, -4.7), true);
        build.Release();
        build.Press(new Vec2(12, 2), false);
        await r.Frames(2);
        var one = t.S.View.Built.Where(b => b.Kind == BuiltKinds.Road).ToList();
        Check.That(one.Count == 1 && one[0].Id == first.Id && one[0].Outline.Count == 3 * 2, $"a road started on a dead end carries that road on: {one.Count} road, {one[0].Outline.Count / 2} waypoints");
        Check.Equal(Looks.Cobbles, one[0].Parts.First(p => p.Kind == PartKinds.Line).Surface, "in the surface it had");
        t.S.UndoLast();
        await r.Frames(2);
        Check.Equal(2 * 2, t.S.View.Built.Single().Outline.Count, "one undo takes the new length off");
        prefs.Do(BuildPrefs.Pick);
        build.Press(new Vec2(-8, -5), false);
        build.Release();
        build.Press(new Vec2(-8, -5), false);
        build.Release();
        build.Pave(Looks.Gravel);
        await r.Frames(2);
        Check.Equal(Looks.Gravel, t.S.View.Built.Single().Parts.First(p => p.Kind == PartKinds.Line).Surface, "a picked road takes another surface");
    }

    const float CloseView = 6;

    [NodeTest]
    static async Task A_dragged_wall_stands_its_towers_takes_a_gate_and_a_ring_round_a_town_becomes_its_wall(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        var sheet = t.S.View.Sheets[0];
        (prefs.Work, prefs.WorkTowers, prefs.WorkDitch) = (Works.Wall, Works.FewTowers, false);
        prefs.Do(BuildPrefs.Works);
        build.Press(new Vec2(-15, -15), false);
        foreach (var at in new[] { new Vec2(-15, -12), new Vec2(-15.1, -9), new Vec2(-15, -8), new Vec2(-12, -8), new Vec2(-6, -8) }) build.Motion(at);
        build.Release();
        await r.Frames(2);
        var wall = t.S.View.Built.Last();
        Check.Equal(BuiltKinds.Works, wall.Kind, "a dragged wall stands as a wall of its own");
        Check.Equal(3, Works.Corners(wall).Count, "its stroke is straightened to its one corner");
        Check.That(wall.Parts.Count(p => p.Class == "tower") >= 3, "towers stand at its ends and corner");

        prefs.Do(BuildPrefs.Pick);
        build.Press(new Vec2(-15, -11), false);
        build.Release();
        Check.Equal(wall.Id, build.Picked?.Id, "a click on it picks it");
        build.Arm(BuildTool.Gate);
        build.Press(new Vec2(-15, -11), false);
        build.Release();
        await r.Frames(2);
        Check.That(t.S.View.Built.First(b => b.Id == wall.Id).Parts.Any(p => p.Class == "gatehouse"), "Gate opens a gate where the wall is clicked");
        build.Arm(null);
        build.Rework(cls: Works.Palisade);
        await r.Frames(2);
        Check.Equal(Works.Palisade, Works.ClassOf(t.S.View.Built.First(b => b.Id == wall.Id)), "Kind makes it a palisade");

        build.Cancel();
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(8, 8))), 3, TownSeed, 0.6, 0, 0) });
        await r.Frames(2);
        var town = t.S.View.Built.Last();
        prefs.Work = Works.Wall;
        prefs.Do(BuildPrefs.Works);
        foreach (var corner in new[] { new Vec2(2, 2), new Vec2(14, 2), new Vec2(14, 14), new Vec2(2, 14) }) build.Press(corner, true);
        build.Press(new Vec2(2, 2), false);
        await r.Frames(2);
        Check.That(Walls.Of(t.S.View.Built.First(b => b.Id == town.Id)) is { Masonry: true, Corners.Count: 4 }, "four corners Shift+clicked round a town and a click on the first make its wall");
        Check.Equal(1, t.S.View.Built.Count(b => b.Kind == BuiltKinds.Works), "and lay no wall of their own");
    }

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
        prefs.Do(BuildPrefs.Places);
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.3f;
        prefs.Naming = BuildPrefs.NoName;
        await Drag(r, build, new Vec2(0, 0), new Vec2(5, 0));
        var laid = t.S.View.Built.Single();
        prefs.Do(BuildPrefs.Pick);
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

        prefs.Do(BuildPrefs.Things);
        prefs.Block = "barn";
        build.Press(new Vec2(0.4, 0.3), false);
        build.Release();
        await r.Frames(2);
        var more = t.S.View.Built.Single();
        Check.That(more.Parts.Count(p => p.Class == "barn" && p.Kept) == 1 && more.Parts[^1] is { Class: "barn", Kept: true }, "Draw stands a barn in the place");
        Check.That(build.PickedPart?.Class == "barn", "and the barn is in hand");
        var barn = more.Parts[^1].At.ToList();
        Check.That(build.Turn(15), "Q and E turn it");
        await r.Frames(2);
        Check.That(!t.S.View.Built.Single().Parts[^1].At.SequenceEqual(barn), "its corners have turned");
        long Span(BuiltThing thing) => thing.Parts[^1].At.Where((_, i) => i % 2 == 0).Max() - thing.Parts[^1].At.Where((_, i) => i % 2 == 0).Min();
        long wide = Span(t.S.View.Built.Single());
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
        prefs.Do(BuildPrefs.Places);
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
        prefs.Do(BuildPrefs.Pick);
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
        build.Arm(null);
        (prefs.Thins, prefs.Brush) = (true, BrushUnits);
        prefs.Do(BuildPrefs.Homes);
        await Drag(r, build, new Vec2(0, 0), new Vec2(1, 0));
        int thinned = Houses(Town());
        Check.That(thinned < before && Town().Parts.Any(p => p.Kept && p.Class == "garden"), $"Brush Thin takes houses under the brush, nothing picked, and leaves gardens: {before} then {thinned}");
        prefs.Thins = false;
        await Drag(r, build, new Vec2(0, 0), new Vec2(1, 0));
        Check.That(Houses(Town()) > thinned, $"Fill fronts the streets under it with houses: {thinned} then {Houses(Town())}");

        prefs.Do(BuildPrefs.Pick);
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
    static async Task A_place_is_laid_in_its_manner_and_Draw_stands_things_from_the_catalogue_in_it(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Places);
        prefs.Place = BuiltKinds.Settlement;
        prefs.Houses = 0.5f;
        prefs.Walls = 0;
        prefs.Naming = BuildPrefs.NoName;
        prefs.Manner = Manners.Id(Manners.Northern);
        await Drag(r, build, new Vec2(0, 0), new Vec2(5, 0));
        BuiltThing Town() => t.S.View.Built.Single();
        Check.That(Manners.Of(Town()) == Manners.Northern, "the place is laid in the Culture chosen");
        prefs.Do(BuildPrefs.Pick);
        var house = Town().Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        build.Reset(Manners.Setting, Manners.Continental);
        await r.Frames(2);
        Check.That(Manners.Of(Town()) == Manners.Continental, "its Culture is changed when picked");
        prefs.Do(BuildPrefs.Things);
        prefs.Block = "windmill";
        build.Press(new Vec2(0.3, 0.2), false);
        build.Release();
        await r.Frames(2);
        Check.That(Town().Parts[^2] is { Class: "mill", Kind: PartKinds.Block, Kept: true } && Town().Parts[^1] is { Class: "mill", Kind: PartKinds.Sign },
            "a windmill from the catalogue stands in the town with its sails");
        prefs.Block = "cattle";
        build.Press(new Vec2(3.5, 0.4), false);
        build.Release();
        await r.Frames(2);
        Check.That(Town().Parts[^1] is { Class: "cattle", Kept: true }, "and cattle beside it");
        prefs.Block = "well";
        build.Press(new Vec2(-15, -15), false);
        build.Release();
        await r.Frames(2);
        Check.That(t.S.View.Built.Count(b => b.Kind == BuiltKinds.Building) == 1, "away from the town, a thing stands alone");
        prefs.Manner = Manners.Id(Manners.Continental);
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
    }

    [NodeTest]
    static async Task A_click_lays_nothing_and_a_drag_lays_the_place_it_spans(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Places);
        prefs.Place = BuiltKinds.Settlement;
        prefs.Naming = BuildPrefs.NoName;
        build.Press(new Vec2(-8, 0), false);
        build.Release();
        await r.Frames(2);
        Check.Equal(0, t.S.View.Built.Count, "a click lays nothing");
        prefs.Place = BuiltKinds.Fort;
        build.Press(new Vec2(10, 10), false);
        build.Release();
        await r.Frames(2);
        Check.Equal(0, t.S.View.Built.Count, "a click lays no fort either");
        prefs.Place = BuiltKinds.Settlement;
        build.Press(new Vec2(-10, 0), false);
        build.Motion(new Vec2(0, 0));
        build.Release();
        await r.Frames(2);
        Check.Equal(1, t.S.View.Built.Count, "a drag lays a town");
    }

    [NodeTest]
    static async Task A_place_takes_a_generated_or_a_typed_name_and_is_renamed_when_picked(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Places);
        prefs.Place = BuiltKinds.Settlement;
        prefs.Naming = BuildPrefs.NoName;
        int lettered = t.Table.MapLettering.Root.GetChildCount();
        await Drag(r, build, new Vec2(0, 0), new Vec2(4, 0));
        var laid = t.S.View.Built.Single();
        Check.That(laid.Name == null && t.Table.MapLettering.Root.GetChildCount() == lettered, $"no one names a place but the player: {laid.Name}");

        prefs.Do(BuildPrefs.Pick);
        var house = laid.Parts.First(p => p.Class == "house");
        build.Press(BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint(house.At[0], house.At[1])), false);
        build.Release();
        build.Rename("  Aubach ");
        await r.Frames(2);
        Check.Equal("Aubach", t.S.View.Built.Single().Name, "a typed name is taken, tidied");
        Check.That(t.Table.MapLettering.Root.GetChildCount() > lettered, "and lettered on the map");
        build.Again();
        await r.Frames(2);
        Check.Equal("Aubach", t.S.View.Built.Single().Name, "Again keeps the name");
        build.Rename("");
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Name == null, "an emptied name takes the lettering away");
        build.Cancel();

        prefs.Do(BuildPrefs.Places);
        prefs.Naming = BuildPrefs.Typed;
        await Drag(r, build, new Vec2(-12, 10), new Vec2(-4, 10));
        Check.That(build.Naming && t.S.View.Built.Count == 1, "Typed waits for the name before it lays the place");
        var entry = t.Table.GetTree().Root.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Last(e => e.PlaceholderText == "Name");
        entry.Text = "Kirchdorf";
        entry.EmitSignal(LineEdit.SignalName.TextSubmitted, entry.Text);
        await r.Frames(2);
        Check.That(!build.Naming && t.S.View.Built.Count == 2 && t.S.View.Built[^1].Name == "Kirchdorf", "Enter lays it under the typed name");

        await Drag(r, build, new Vec2(12, 10), new Vec2(18, 10));
        Check.That(build.Naming, "a second place asks again");
        Check.That(build.Drop() && !build.Naming && t.S.View.Built.Count == 2, "and Esc drops the place with its box");
        await Drag(r, build, new Vec2(12, 10), new Vec2(18, 10));
        build.Cancel();
        await r.Frames(2);
        Check.That(!build.Naming && t.S.View.Built.Count == 3 && t.S.View.Built[^1].Name == null, "leaving the tool lays the waiting place unnamed");
        t.S.UndoLast();
        await r.Frames(2);

        prefs.Naming = BuildPrefs.NoName;
        await Drag(r, build, new Vec2(12, -12), new Vec2(18, -12));
        Check.That(t.S.View.Built.Count == 3 && t.S.View.Built[^1].Name == null, "None lays a place without a name");
        prefs.Naming = BuildPrefs.Typed;
        prefs.Place = BuildPrefs.Redoubt;
        await Drag(r, build, new Vec2(-14, -14), new Vec2(-9, -14));
        Check.That(t.S.View.Built.Count == 4 && t.S.View.Built[^1].Name == null, "a redoubt is not named");
    }

    [NodeTest]
    static async Task A_dragged_settlement_is_laid_picked_rerolled_moved_and_removed_each_in_one_undo_step(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Places);
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
        build.Motion(new Vec2(-9, -14));
        build.Release();
        await r.Frames(2);
        Check.That(Forts.Plain(t.S.View.Built[^1]), "Redoubt lays a plain work");
        t.S.UndoLast();
        await r.Frames(2);

        prefs.Do(BuildPrefs.Pick);
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
        var grab = BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint((first.At[0] + first.At[4]) / 2, (first.At[1] + first.At[5]) / 2));
        build.Press(grab, false);
        build.Release();
        Check.That(build.PickedLead?.Class == "house", "a second click takes the house under it with what stands in its yard");
        build.Press(grab, false);
        build.Motion(grab + new Vec2(Reach, 0));
        build.Release();
        await r.Frames(2);
        var moved = t.S.View.Built.Single().Parts.First(p => p.Class == "house");
        var now = BuiltGeometry.ToWorld(t.S.View.Sheets[0], new SheetPoint((moved.At[0] + moved.At[4]) / 2, (moved.At[1] + moved.At[5]) / 2));
        Check.That(System.Math.Abs(now.X - grab.X - Reach) < 0.01 && System.Math.Abs(now.Z - grab.Z) < 0.01, $"a drag moves the picked house: {now.X - grab.X:0.00}");
        Check.That(moved.Kept, "the house moved by hand is kept");
        build.Again();
        await r.Frames(2);
        Check.That(t.S.View.Built.Single().Parts.Any(p => p.Kept && p.At[0] == moved.At[0]), "and stays through another Again");

        var whole = t.S.View.Built.Single();
        var sheet = t.S.View.Sheets[0];
        var between = Enumerable.Range(-40, 81).SelectMany(x => Enumerable.Range(-40, 81).Select(z => new Vec2(x * 0.1, z * 0.1)))
            .First(p => BuiltThings.StandsOver(whole, Spot.Of(BuiltGeometry.ToSheet(sheet, p))) && BuiltThings.Nearest(whole, Spot.Of(BuiltGeometry.ToSheet(sheet, p))).Reach > Clear);
        build.Cancel();
        prefs.Do(BuildPrefs.Places);
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
    static async Task A_building_is_drawn_from_corner_to_corner_reshaped_by_its_handles_and_given_a_wing(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        var sheet = t.S.View.Sheets[0];
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), CloseView);
        await r.Frames(2);
        prefs.Do(BuildPrefs.Things);
        prefs.Block = "house";
        prefs.FromCorner = true;
        await Drag(r, build, new Vec2(-4, -1), new Vec2(4, 2));
        prefs.FromCorner = false;
        BuiltPart House() => t.S.View.Built.Single().Parts.First(p => p.Kind == PartKinds.Block);
        var (wide, deep) = BuiltThings.Sides(House());
        Check.That(System.Math.Abs(wide - 8) < 0.01 && System.Math.Abs(deep - 3) < 0.01, $"a drag from corner to corner gives the building both its sides ({wide:0.##} by {deep:0.##})");
        Check.That(build.Picked?.Id == t.S.View.Built.Single().Id && build.Shaped != null, "the building just drawn is picked, with its handles");

        var end = BuiltGeometry.ToWorld(sheet, PartHandles.Of(House())[1].Stored);
        var heart = BuiltGeometry.ToWorld(sheet, BuiltSite.Round(House()).At.Stored);
        await Drag(r, build, end, new Vec2(end.X + System.Math.Sign(end.X - heart.X) * 3, end.Z));
        var (longer, same) = BuiltThings.Sides(House());
        Check.That(System.Math.Abs(longer - 11) < 0.05 && System.Math.Abs(same - 3) < 0.05, $"a side handle pulls that side alone ({longer:0.##} by {same:0.##})");
        Check.Equal(1, t.S.View.Built.Count, "and stands no second building");

        build.Arm(BuildTool.Wing);
        var side = BuiltGeometry.ToWorld(sheet, PartHandles.Of(House())[2].Stored);
        var outward = side - BuiltGeometry.ToWorld(sheet, BuiltSite.Round(House()).At.Stored);
        await Drag(r, build, side, side + outward + outward + outward);
        var winged = t.S.View.Built.Single();
        Check.Equal(2, winged.Parts.Count(p => p.Kind == PartKinds.Block), "a drag out from a side adds a wing");
        Check.That(build.Adding == null, "and the wing is put down once");
        t.S.UndoLast();
        await r.Frames(2);
        Check.Equal(1, t.S.View.Built.Single().Parts.Count(p => p.Kind == PartKinds.Block), "one undo takes the wing off");
        build.Cancel();
    }

    [NodeTest]
    static async Task A_town_is_painted_with_the_brush_grown_and_shrunk_and_reshaped_by_a_corner_of_its_outline(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        var sheet = t.S.View.Sheets[0];
        prefs.Do(BuildPrefs.Places);
        (prefs.Place, prefs.Outline, prefs.Brush, prefs.Houses, prefs.Walls, prefs.Naming) = (BuiltKinds.Settlement, BuildPrefs.Painted, 3, 0.8f, 0, BuildPrefs.NoName);
        build.Press(new Vec2(-14, -2), false);
        for (int i = 1; i <= 14; i++) build.Motion(new Vec2(-14 + i * 2, -2 + (i % 4 < 2 ? 0 : 1.5f)));
        build.Release();
        await r.Frames(2);
        var town = t.S.View.Built.Single();
        var ring = Outlines.Of(town);
        bool Has(BuiltThing place, Vec2 near, double within) => place.Parts.Any(p => p.Kind == PartKinds.Block && (BuiltGeometry.ToWorld(sheet, BuiltSite.Round(p).At.Stored) - near).Length < within);
        Check.That(ring != null && town.Kind == BuiltKinds.Settlement, "a brush stroke lays a settlement with an outline");
        Check.That(Has(town, new Vec2(-13, -2), 4) && Has(town, new Vec2(13, -1), 4), "built to both ends of the stroke");
        Check.That(town.Parts.Where(p => p.Kind == PartKinds.Block).All(p => Outlines.Depth(ring!, BuiltSite.Round(p).At) > -0.5), "and nowhere outside it");
        Check.That(build.Picked?.Id == town.Id, "the settlement just laid is picked");

        prefs.Do(BuildPrefs.Grow);
        await Drag(r, build, new Vec2(0, 0), new Vec2(0, 12));
        var grown = t.S.View.Built.Single();
        Check.That(Has(grown, new Vec2(0, 10), 4) && Has(grown, new Vec2(-13, -2), 4), "Grow adds the ground brushed to the settlement and keeps the rest");
        prefs.Do(BuildPrefs.Shrink);
        await Drag(r, build, new Vec2(-3, 12), new Vec2(3, 12));
        Check.That(!Has(t.S.View.Built.Single(), new Vec2(0, 12), 2.5), "Shrink gives it up again");
        t.S.UndoLast();
        t.S.UndoLast();
        await r.Frames(2);
        Check.Equal(ring!.Count, Outlines.Of(t.S.View.Built.Single())!.Count, "two undos bring the first outline back");

        prefs.Outline = BuildPrefs.Cornered;
        prefs.Do(BuildPrefs.Places);
        build.Cancel();
        foreach (var corner in new[] { new Vec2(-12, 10), new Vec2(-4, 10), new Vec2(-4, 18), new Vec2(-12, 18), new Vec2(-20, 30) }) build.Press(corner, true);
        Check.That(build.Unpoint(), "the last point clicked is taken back");
        build.Press(new Vec2(-12, 10), false);
        await r.Frames(2);
        var square = t.S.View.Built.Last();
        Check.That(t.S.View.Built.Count == 2 && Outlines.Of(square) is { Count: 4 }, "four clicked corners lay a second settlement in that outline");
        prefs.Do(BuildPrefs.Pick);
        build.Press(new Vec2(-8, 14), false);
        build.Release();
        var at = BuiltGeometry.ToWorld(sheet, Outlines.Of(square)!.MaxBy(c => BuiltGeometry.ToWorld(sheet, c.Stored).X + BuiltGeometry.ToWorld(sheet, c.Stored).Z).Stored);
        await Drag(r, build, at, new Vec2(at.X + 5, at.Z + 5));
        var pulled = t.S.View.Built.First(b => b.Id == square.Id);
        Check.That(Outlines.Of(pulled)!.Any(c => (BuiltGeometry.ToWorld(sheet, c.Stored) - new Vec2(at.X + 5, at.Z + 5)).Length < 0.01), "a corner of the outline is dragged to reshape it");
        prefs.Outline = BuildPrefs.Round;
        build.Cancel();
        prefs.Do(BuildPrefs.Roads);
        (prefs.Line, prefs.FindsWay) = ("road", false);
        foreach (var way in new[] { new Vec2(10, 20), new Vec2(20, 20), new Vec2(25, 40) }) build.Press(way, true);
        build.Unpoint();
        build.Press(new Vec2(20, 20), false);
        await r.Frames(2);
        Check.Equal(2 * 2, t.S.View.Built.Last(b => b.Kind == BuiltKinds.Road).Outline.Count, "a plain click on the last waypoint lays the road through the waypoints left");
        Check.That(!build.Unpoint(), "with nothing pointed out Backspace is not taken");
    }

    [NodeTest]
    static async Task A_road_is_clicked_out_and_a_building_and_details_are_laid(TestRunner r)
    {
        var t = await Ground(r);
        var build = t.Table.Build;
        var prefs = t.Table.Settings.Build;
        prefs.Do(BuildPrefs.Roads);
        prefs.Line = "road";
        prefs.FindsWay = false;
        build.Press(new Vec2(-15, -5), true);
        build.Press(new Vec2(0, -5), true);
        build.Press(new Vec2(15, -4), false);
        await r.Frames(2);
        var road = t.S.View.Built.Single();
        Check.Equal(BuiltKinds.Road, road.Kind, "a road is laid on the plain click");
        Check.Equal(3 * 2, road.Outline.Count, "through its three waypoints");
        build.Press(new Vec2(-15, 14), false);
        foreach (float x in new[] { -10f, -5f, 0f, 5f, 10f, 15f }) build.Motion(new Vec2(x, 14 + x * 0.02f));
        build.Release();
        await r.Frames(2);
        var dragged = t.S.View.Built.Last();
        Check.That(dragged.Id != road.Id && dragged.Kind == BuiltKinds.Road && dragged.Outline.Count == 2 * 2, $"a road dragged nearly straight is laid in one leg: {dragged.Outline.Count / 2} waypoints");

        prefs.Do(BuildPrefs.Things);
        prefs.Block = "church";
        build.Press(new Vec2(5, 5), false);
        build.Release();
        await r.Frames(2);
        Check.That(t.S.View.Built.Any(b => b.Kind == BuiltKinds.Building && b.Parts.Any(p => p.Class == "church")), "a click stands a church");

        prefs.Do(BuildPrefs.Details);
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

    const double Hillside = 0.4, Cliff = 0.65;

    [NodeTest]
    static async Task A_village_is_laid_on_a_hillside_and_a_drag_on_a_cliff_says_there_is_no_room(TestRunner r)
    {
        foreach (double grade in new[] { Hillside, Cliff })
        {
            var t = await Ground(r, grade);
            string said = "";
            t.Table.Hint += h => said = h;
            var prefs = t.Table.Settings.Build;
            prefs.Do(BuildPrefs.Places);
            prefs.Place = BuiltKinds.Settlement;
            prefs.Naming = BuildPrefs.NoName;
            await Drag(r, t.Table.Build, new Vec2(0, 0), new Vec2(5, 0));
            bool built = t.S.View.Built.Count == 1 && Houses(t.S.View.Built[0]) > 0;
            Check.That(grade < Cliff ? built : t.S.View.Built.Count == 0 && said == BuildTool.NoRoom, $"a {grade:P0} slope: {t.S.View.Built.Count} laid, said \"{said}\"");
        }
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

    const int OnceEnglish = 1;

    [NodeTest]
    static async Task The_Culture_list_has_a_sign_and_a_name_for_each_culture_of_the_map_s_age(TestRunner r)
    {
        await r.Frame();
        foreach (int epoch in new[] { Manners.Ancient, Manners.Medieval, Manners.Later, Manners.Modern })
        {
            string picked = "";
            var cultures = Manners.In(epoch).Select(m => (Manners.Id(m), Manners.Named(m).Name, Manners.Named(m).Tip, (Texture2D?)CultureSigns.Of(m))).ToArray();
            var list = Ui.Picker(cultures, cultures[^1].Item1, key => picked = key);
            r.AddChild(list);
            await r.Frame();
            Check.That(list.ItemCount == cultures.Length && list.Selected == cultures.Length - 1, $"{list.ItemCount} cultures listed, the chosen one shown");
            Check.That(Enumerable.Range(0, list.ItemCount).All(i => list.GetItemIcon(i) is { } sign && sign.GetWidth() > 1 && list.GetItemText(i).Length > 0), "each has its sign and its name");
            Check.That(cultures.Select(c => c.Item4).Distinct().Count() == cultures.Length, "no two share a sign");
            list.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
            Check.That(picked == cultures[0].Item1, $"picking the first gives {picked}");
            list.QueueFree();
        }
        Check.That(CultureSigns.Of(OnceEnglish) == CultureSigns.Of(Manners.Continental), "a place saved as English wears Continental's sign");
    }

    [NodeTest]
    static async Task The_Build_panel_s_choices_are_pictured_tiles_each_with_a_picture_of_its_own(TestRunner r)
    {
        await r.Frame();
        var missing = BuildPanel.Pictures.Where(kind => !ToolIcon.Choices.ContainsKey(kind)).ToList();
        Check.That(missing.Count == 0 && BuildPanel.Pictures.Distinct().Count() == BuildPanel.Pictures.Count(), $"every choice has its picture: {string.Join(", ", missing)}");
        string picked = "";
        (string Key, string Text, string Tip)[] walls = [("none", "None", "open"), ("wall", "Walled", "a wall"), ("bastions", "Bastioned", "a trace")];
        var rows = ToolTile.Rows(walls, "wall", walls.Length, key => picked = key, key => "walls-" + key, true);
        r.AddChild(rows);
        await r.Frame();
        var tiles = rows.FindChildren("*", "Button", true, false).OfType<Button>().ToList();
        Check.That(tiles.Count == walls.Length && tiles.All(b => b.Text == "" && b.TooltipText.Length > 0), "a tile per choice, no word for a face, a tooltip each");
        var icons = tiles.Select(b => b.FindChildren("*", "", true, false).OfType<ToolIcon>().Single().Kind).ToList();
        Check.That(icons.SequenceEqual(walls.Select(w => ToolIcon.TerrainKinds + "walls-" + w.Key)), $"each holds its picture: {string.Join(", ", icons)}");
        Check.That(tiles.All(b => b.FindChildren("*", "Label", true, false).OfType<Label>().Single().Text.Length > 0), "and its name under it");
        Check.That(tiles[1].ButtonPressed && !tiles[0].ButtonPressed, "the chosen one is lit");
        tiles[2].EmitSignal(BaseButton.SignalName.Pressed);
        Check.Equal("bastions", picked, "a press picks it");
        rows.QueueFree();
    }

    const float HouseM = 8, LongM = 12, Half = 0.5f, BrushUnits = 0.7f;

    [NodeTest]
    static async Task Builders_of_another_culture_roof_a_house_their_own_way_and_no_roof_breaks_the_building_rules(TestRunner r)
    {
        await r.Frame();
        Check.That(RoofBook.Of("domus", Manners.Roman, Manners.Roman, HouseM, LongM, Half, Half) == (Form.Atrium, Cover.Tile), "Romans roof an atrium house in tile");
        Check.That(RoofBook.Of("domus", Manners.Egyptian, Manners.Egyptian, HouseM, LongM, Half, Half) is (Form.Atrium, Cover.Silt or Cover.Lime), "Egyptian builders give it flat mud roofs");
        Check.That(RoofBook.Of("roundhouse", Manners.Tribal, Manners.Tribal, HouseM, HouseM, Half, Half) == (Form.Cone, Cover.Thatch), "a round house is a thatched cone");
        Check.That(RoofBook.Of("roundhouse", Manners.Egyptian, Manners.Egyptian, HouseM, HouseM, Half, Half) is (Form.Dome, Cover.Silt or Cover.Lime), "built in mud brick it is a dome");
        Check.That(RoofBook.Of("roundhouse", Manners.Roman, Manners.Roman, HouseM, HouseM, Half, Half) == (Form.Cone, Cover.Tile), "built by Romans, a tiled cone");
        foreach (float town in new[] { 0.1f, 0.5f, 0.9f })
        {
            var covers = Enumerable.Range(0, 40).Select(i => RoofBook.Of("house", Manners.Continental, Manners.Continental, HouseM, LongM, i / 40f, Half, town).Cover).ToList();
            Check.That(covers.Count(c => c == RoofBook.Main(Manners.Continental, town)) >= 32, $"a town is roofed mostly in one covering: {covers.Count(c => c == RoofBook.Main(Manners.Continental, town))} of 40");
        }
        string[] classes = ["house", "domus", "insula", "roundhouse", "greathouse", "baths", "basilica", "warehouse", "barn", "shed", "public", "dovecote", "taberna", "villa"];
        foreach (int builds in Manners.All.Select(m => m.Number))
            foreach (string cls in classes)
                for (float pick = 0.05f; pick < 1; pick += 0.1f)
                {
                    var (form, cover) = RoofBook.Of(cls, builds, builds, HouseM, LongM, pick, 1 - pick);
                    bool flat = RoofBook.Level(cover), thatch = cover is Cover.Thatch or Cover.Turf or Cover.Palm;
                    Check.That(!flat || form is Form.Flat or Form.Court or Form.Atrium or Form.Dome or Form.Vault, $"{cls} by {Manners.Id(builds)}: {cover} on a {form} roof");
                    Check.That(!thatch || form is not (Form.Mansard or Form.Pyramid or Form.Flat or Form.Dome or Form.Vault), $"{cls} by {Manners.Id(builds)}: {cover} on a {form} roof");
                }
    }
}
