using System;
using System.Linq;
using System.Threading.Tasks;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BuildingScaleAppTests
{
    const double Side = 100, HexMetresPerUnit = 5660, Scale = 200, TownReachM = 150, RoadM = 20000, EarthM = 40_075_000, PlanetWide = 409.6;
    const double HouseLeastM = 5, HouseMostM = 40;
    const int TownSeed = 7, SettleFrames = 8, UsualMostLevel = 14;

    static async Task<OfflineTable> Table(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 1 / HexMetresPerUnit });
        return t;
    }

    static SheetDef Sheet(OfflineTable t) => t.S.View.Sheets.Single();

    static double Span(BuiltThing thing) => BuiltGeometry.Box(thing) is var (low, high) ? (high.X - low.X) / (double)BuiltGeometry.PerUnit : 0;

    static Lighting Scaled(OfflineTable t, double scale)
    {
        var next = t.S.View.Lighting.Clone();
        next.BuildingScale = Lighting.HeldBuildingScale(scale);
        return next;
    }

    [NodeTest]
    static async Task A_new_building_scale_lays_the_table_again_at_its_size_and_one_undo_takes_it_back(TestRunner r)
    {
        var t = await Table(r);
        var sheet = Sheet(t);
        double perMetre = 1 / HexMetresPerUnit;
        var site = BuildTool.Site(t.S, sheet);
        var middle = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0)));
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(site, sheet.Id, middle, TownReachM * perMetre, TownSeed, 1, 0, 0) });
        var course = new[] { new Vec2(-RoadM * perMetre / 2, 10), new Vec2(RoadM * perMetre / 2, 10) }.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p))).ToList();
        t.S.Do(new LayBuilt { Thing = BuiltRoads.Lay(BuildTool.Site(t.S, sheet), sheet.Id, course, "road", false) });
        await r.Frames(SettleFrames);
        var before = t.S.View.Built.Select(b => b.Clone()).ToList();
        Check.That(before.Count == 2, $"the town and road are laid, {before.Count} laid");

        var (change, refused) = await BuildTool.Rescaled(t.S, Scaled(t, Scale));
        Check.That(change != null && refused == null, $"the scale change is made: {refused}");
        t.S.Do(change!);
        await r.Frames(SettleFrames);
        var town = t.S.View.Built.Single(b => b.Kind == BuiltKinds.Settlement);
        var road = t.S.View.Built.Single(b => b.Kind == BuiltKinds.Road);
        Check.That(t.S.View.Lighting.Building == Scale, $"the scenario holds ×{Scale}, holds ×{t.S.View.Lighting.Building}");
        Check.That(t.S.View.Built.All(b => Math.Abs(b.Setting(BuiltThings.Scale, 0) - perMetre * Scale) < perMetre * Scale * 1e-9), "each thing is stamped at the new scale");
        double grown = Span(town) / Span(before[0]);
        Check.That(grown > Scale * 0.8 && grown < Scale * 1.2, $"the town grew {grown:0} times, asked ×{Scale}");
        Check.That(road.Outline.SequenceEqual(before[1].Outline), "the road keeps its course");
        Check.That(BuiltGeometry.Points(town.Outline)[0] == BuiltGeometry.Points(before[0].Outline)[0], "the town keeps its middle");

        int laidAfter = t.S.View.Built.Count;
        t.S.Do(new LayBuilt { Thing = BuiltThings.Building(BuildTool.Site(t.S, sheet), sheet.Id, "house", middle, new Spot(1, 0)) });
        double longM = BuiltThings.Sides(t.S.View.Built[^1].Parts[0]).Long / (perMetre * Scale);
        Check.That(t.S.View.Built.Count == laidAfter + 1 && longM > HouseLeastM && longM < HouseMostM, $"a house laid after is laid at the new scale, {longM:0.#} m long at ×{Scale}");
        t.S.UndoLast();
        await r.Frames(SettleFrames);
        t.S.UndoLast();
        await r.Frames(SettleFrames);
        Check.That(t.S.View.Lighting.Building == 1, $"one undo takes the scale back, ×{t.S.View.Lighting.Building} with {t.S.View.Built.Count} built");
        Check.That(t.S.View.Built.Select(b => KTable.Core.Json.Serialize(b)).SequenceEqual(before.Select(b => KTable.Core.Json.Serialize(b))), "and every built thing as it was");
    }

    [NodeTest]
    static async Task A_scale_too_big_for_the_sheet_is_refused_and_changes_nothing(TestRunner r)
    {
        var t = await Table(r);
        var sheet = Sheet(t);
        var middle = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0)));
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, middle, TownReachM / HexMetresPerUnit, TownSeed, 1, 0, 0) });
        var before = KTable.Core.Json.Serialize(t.S.View.Built);
        var (change, refused) = await BuildTool.Rescaled(t.S, Scaled(t, Lighting.MostBuildingScale));
        Check.That(change == null && refused == BuildTool.TooBigAtScale, "growth past what the sheet holds is refused");
        Check.That(t.S.View.Lighting.Building == 1 && KTable.Core.Json.Serialize(t.S.View.Built) == before, "nothing changed");
    }

    [NodeTest]
    static async Task A_scale_change_lays_off_the_frame_refuses_a_second_and_sends_nothing_if_the_table_changed(TestRunner r)
    {
        var t = await Table(r);
        var sheet = Sheet(t);
        var middle = Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0)));
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, middle, TownReachM / HexMetresPerUnit, TownSeed, 1, 0, 0) });
        var rescaling = BuildTool.Rescaled(t.S, Scaled(t, Scale));
        Check.That(!rescaling.IsCompleted && BuildTool.Rescaling, "the town is laid again off the frame");
        var (second, why) = await BuildTool.Rescaled(t.S, Scaled(t, Scale * 2));
        Check.That(second == null && why == BuildTool.StillRescaling, "a second change while it lays is refused");
        t.S.Do(new LayBuilt { Thing = BuiltThings.Building(BuildTool.Site(t.S, sheet), sheet.Id, "house", Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(20, 20))), new Spot(1, 0)) });
        var (change, refused) = await rescaling;
        Check.That(change == null && refused == BuildTool.ChangedWhileRescaling, $"a table that changed meanwhile gets nothing sent: {refused}");
        Check.That(t.S.View.Lighting.Building == 1 && !BuildTool.Rescaling, "the scale stays as it was");
    }

    [NodeTest]
    static async Task The_agent_sets_the_building_scale_and_the_town_is_laid_again_at_it(TestRunner r)
    {
        var (h, agent) = await AgentGroundTests.Ground(r);
        var (laidError, laid) = await AgentRecordTests.Ask(agent, "build_place", """{"kind":"settlement","x":0,"z":0,"radius":2,"seed":4}""");
        Check.That(!laidError, "a town is laid: " + laid);
        double span = Span(h.S.View.Built.Single());
        var (error, said) = await AgentRecordTests.Ask(agent, "set_light", """{"building_scale":3}""");
        await r.Frames(SettleFrames);
        Check.That(!error && h.S.View.Lighting.Building == 3, $"the agent sets ×3: {said}");
        double grown = Span(h.S.View.Built.Single()) / span;
        Check.That(grown > 2.4 && grown < 3.6, $"and the town is laid again about three times as wide, {grown:0.##}");
        Check.That((await AgentRecordTests.Ask(agent, "set_light", """{"building_scale":0.01}""")).error, "a scale below the least is refused");
    }

    [NodeTest]
    static Task Ground_tiles_at_a_large_building_scale_drop_back_to_usual_depth(TestRunner r)
    {
        double planet = PlanetWide / EarthM;
        Check.That(GroundTiles.LevelsFor((float)PlanetWide, planet) > UsualMostLevel, "life size on an Earth-wide sheet needs the deepest tiles");
        int scaled = GroundTiles.LevelsFor((float)PlanetWide, planet * 1000);
        Check.That(scaled <= UsualMostLevel, $"at ×1000 it needs {scaled} levels, no more than the usual {UsualMostLevel}");
        return Task.CompletedTask;
    }
}
