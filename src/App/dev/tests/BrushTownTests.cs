using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class BrushTownTests
{
    const int GridSeed = 11, Shapes = 6000, LongEvery = 150, Asks = 300, MostWeighedOnOpenGround = Shapes / 20;
    const float TownShare = 0.5f, SmallShare = 0.01f, OpenAt = 0.9f, OpenAcross = 0.04f;

    [NodeTest]
    static Task The_shapes_near_a_place_are_found_without_weighing_them_all(TestRunner r)
    {
        var dice = new Random(GridSeed);
        float Roll() => (float)dice.NextDouble();
        var boxes = Enumerable.Range(0, Shapes).Select(i =>
            i % LongEvery == 0 ? new Rect2(Roll() * TownShare, Roll() * TownShare, Roll() * TownShare, Roll() * TownShare)
            : new Rect2(Roll() * TownShare, Roll() * TownShare, Roll() * SmallShare, i % 3 == 0 ? 0 : Roll() * SmallShare)).ToArray();
        var grid = new ShapeGrid(boxes);
        for (int ask = 0; ask < Asks; ask++)
        {
            var reach = new Rect2(Roll() * 1.2f - 0.1f, Roll() * 1.2f - 0.1f, Roll() * Roll(), Roll() * Roll());
            var there = Enumerable.Range(0, boxes.Length).Where(i => boxes[i].Intersects(reach, true)).ToList();
            var found = grid.Among(reach);
            Check.That(found.SequenceEqual(there), $"ask {ask} found {found.Count} shapes in their order where {there.Count} reach it");
            Check.That(grid.Among(reach, firstOnly: true).Count == Math.Min(1, there.Count), $"ask {ask} says whether any shape reaches it");
        }
        long before = grid.Weighed;
        var open = grid.Among(new Rect2(OpenAt, OpenAt, OpenAcross, OpenAcross));
        Check.That(open.Count == 0 && grid.Weighed - before <= MostWeighedOnOpenGround, $"open ground weighed {grid.Weighed - before} of {Shapes} shapes and found {open.Count}");
        return Task.CompletedTask;
    }

    const double Side = 40, UnitsPerMetre = 0.02, TownReach = 5, Beside = 15, DitchAt = -15, DitchHalf = 6, Across = 3;
    const int TownSeed = 7, SettleFrames = 8, StrokeSteps = 6, CommitFrames = 30, MostCommitFrames = 600;

    static async Task<OfflineTable> Table(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        return t;
    }

    static SheetDef Sheet(OfflineTable t) => t.S.View.Sheets.Single();

    static BuiltThing Town(OfflineTable t, Vec2 at, double reach, int seed) =>
        Settlements.Lay(BuildTool.Site(t.S, Sheet(t)), Sheet(t).Id, Spot.Of(BuiltGeometry.ToSheet(Sheet(t), at)), reach, seed, 1, 0, 0);

    static async Task Stroke(TestRunner r, OfflineTable t, string mode, Vec2 from, Vec2 to)
    {
        string? was = Sheet(t).Relief;
        t.Table.SetTool(Tool.Relief);
        t.Table.Settings.ReliefMode = mode;
        t.Table.Settings.ReliefPaint = "brush";
        t.Table.Settings.ReliefBrush = 2;
        t.Table.Relief.Press(from, false, false);
        for (int i = 1; i <= StrokeSteps; i++)
        {
            double along = i / (double)StrokeSteps;
            t.Table.Relief.Motion(new Vec2(from.X + (to.X - from.X) * along, from.Z + (to.Z - from.Z) * along));
            await r.Frames(2);
        }
        t.Table.Relief.Release();
        for (int i = 0; i < MostCommitFrames && (Sheet(t).Relief == was || t.S.Relief.Grid(Sheet(t)) == null); i++) await r.Frame();
        await r.Frames(CommitFrames);
    }

    static bool ShapedAsLaidAfresh(OfflineTable t)
    {
        var sheet = Sheet(t);
        var things = t.S.View.Built.Where(b => b.Sheet == sheet.Id).ToList();
        var afresh = Earthworks.Shaped(t.S.Relief.Grid(sheet)!, things, Ground.FullWidth(sheet), BuiltGeometry.FullHeight(sheet), UnitsPerMetre, Ground.FullWidth(sheet) * sheet.Height);
        return t.S.Relief.Shaped(sheet)!.H.AsSpan().SequenceEqual(afresh.H);
    }

    [NodeTest]
    static async Task A_stroke_on_open_ground_reads_no_built_part_and_lays_no_earthwork_again(TestRunner r)
    {
        var t = await Table(r);
        var sheet = Sheet(t);
        t.S.Do(new LayBuilt { Thing = Town(t, new Vec2(0, 0), TownReach, TownSeed) });
        var ditch = new[] { new Vec2(-DitchHalf, DitchAt), new Vec2(DitchHalf, DitchAt) }.Select(p => Spot.Of(BuiltGeometry.ToSheet(sheet, p)));
        t.S.Do(new LayBuilt { Thing = new BuiltThing { Id = "ditch", Sheet = sheet.Id, Kind = BuiltKinds.Water, Parts = { BuiltSite.Line("ditch", ditch) } } });
        await r.Frames(SettleFrames);
        int parts = t.S.View.Built.Sum(b => b.Parts.Count);
        long keyed = t.S.Relief.PartsKeyed, works = t.S.Relief.Works(sheet);
        int laid = t.S.Relief.TimesLaid;
        Check.That(keyed == parts && works != 0 && laid > 0, $"the {parts} built parts were read once for their earthworks: {keyed} read, laid {laid} times");
        await Stroke(r, t, "raise", new Vec2(-Beside, Beside), new Vec2(Beside, Beside));
        Check.That(t.S.Relief.PartsKeyed == keyed && t.S.Relief.Works(sheet) == works, $"a stroke on open ground read {t.S.Relief.PartsKeyed - keyed} built parts again");
        Check.That(t.S.Relief.TimesLaid == laid, $"and laid the earthworks {t.S.Relief.TimesLaid - laid} times again");
        Check.That(t.S.Ground.At(new Vec2(0, Beside)) > 0 && ShapedAsLaidAfresh(t), "the ground is as the earthworks laid afresh would shape it");
        await Stroke(r, t, "raise", new Vec2(0, DitchAt - Across), new Vec2(0, DitchAt + Across));
        Check.That(t.S.Relief.TimesLaid == laid + 1 && t.S.Relief.PartsKeyed == keyed, $"a stroke across the ditch lays the earthworks once more: {t.S.Relief.TimesLaid - laid} times, {t.S.Relief.PartsKeyed - keyed} parts read");
        Check.That(ShapedAsLaidAfresh(t), "and the ground is as the earthworks laid afresh would shape it");
    }

    const double HouseLongM = 12, HouseDeepM = 8, InTown = 1.5;

    [NodeTest]
    static async Task A_building_laid_shapes_only_itself_and_the_ground_near_it(TestRunner r)
    {
        var t = await Table(r);
        var sheet = Sheet(t);
        var site = BuildTool.Site(t.S, sheet);
        double wide = Ground.FullWidth(sheet), deep = BuiltGeometry.FullHeight(sheet);
        var town = Town(t, new Vec2(0, 0), TownReach, TownSeed);
        BuiltThing House(Vec2 at) => BuiltThings.Building(site, sheet.Id, "house", Spot.Of(BuiltGeometry.ToSheet(sheet, at)), new Spot(1, 0), site.M(HouseLongM), site.M(HouseDeepM));
        BuiltThing inside = House(new Vec2(InTown, InTown)), outside = House(new Vec2(Beside, Beside));
        var painter = new BuiltPainter();
        painter.Set([town], wide, deep, UnitsPerMetre);
        var bare = painter.Near().Texels.ToArray();
        long made = painter.ThingsMade;
        painter.Set([town, inside, outside], wide, deep, UnitsPerMetre);
        Check.That(painter.ThingsMade == made + 2, $"two houses laid beside a town shape {painter.ThingsMade - made} things");
        var afresh = new BuiltPainter();
        afresh.Set([town, inside, outside], wide, deep, UnitsPerMetre);
        var near = painter.Near().Texels.ToArray();
        Check.That(!near.AsSpan().SequenceEqual(bare) && near.AsSpan().SequenceEqual(afresh.Near().Texels), "the ground near them is what a painter starting afresh makes of it");
        painter.Set([town], wide, deep, UnitsPerMetre);
        Check.That(painter.ThingsMade == made + 2 && painter.Near().Texels.AsSpan().SequenceEqual(bare), "and with them gone it is as it was, the town not shaped again");
    }

    const double BigReach = 16, OpenZ = 17, OpenHalf = 14;
    const int BigTownSeed = 3, FewestParts = 10000, MostCardFrames = 900, CardCrop = 200;
    const float SameCardWithin = 0.02f, TownShowsFrom = 0.01f, NearAcross = 6;

    static async Task<Image> Card(TestRunner r, OfflineTable t, int after)
    {
        for (int i = 0; i < MostCardFrames && GroundTiles.CardsDrawn <= after; i++) await r.Frame();
        Check.That(GroundTiles.CardsDrawn > after, "the sheet's card picture was drawn");
        await r.Frames(SettleFrames);
        var card = Image.LoadFromFile(GroundCard.FileFor(Sheet(t)));
        return card.GetRegion(new Rect2I((card.GetWidth() - CardCrop) / 2, (card.GetHeight() - CardCrop) / 2, CardCrop, CardCrop));
    }

    static float Apart(Image a, Image b)
    {
        float sum = 0;
        for (int y = 0; y < a.GetHeight(); y++)
            for (int x = 0; x < a.GetWidth(); x++)
            {
                Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
                sum += Mathf.Abs(p.R - q.R) + Mathf.Abs(p.G - q.G) + Mathf.Abs(p.B - q.B);
            }
        return sum / (a.GetWidth() * a.GetHeight() * 3);
    }

    [ScreenTest]
    static async Task Strokes_on_a_sheet_of_a_big_town_draw_none_of_it_again(TestRunner r)
    {
        var t = await Table(r);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
        int cards = GroundTiles.CardsDrawn;
        var bare = await Card(r, t, cards);
        cards = GroundTiles.CardsDrawn;
        t.S.Do(new LayBuilt { Thing = Town(t, new Vec2(0, 0), BigReach, BigTownSeed) });
        int parts = t.S.View.Built.Single().Parts.Count;
        Check.That(parts >= FewestParts, $"the town is a big one: {parts} parts");
        var built = await Card(r, t, cards);
        await GroundTiles.WhenSettled(r);
        Check.That(Apart(bare, built) > TownShowsFrom, $"the card shows the town: {Apart(bare, built):0.000} from bare ground");
        int drawn = GroundTiles.BuiltLayersDrawn;
        Check.That(drawn > 0, "the town was drawn into the ground's tiles");
        cards = GroundTiles.CardsDrawn;
        await Stroke(r, t, "paint:wood", new Vec2(-OpenHalf, OpenZ), new Vec2(OpenHalf, OpenZ));
        await Stroke(r, t, "raise", new Vec2(-OpenHalf, -OpenZ), new Vec2(OpenHalf, -OpenZ));
        await GroundTiles.WhenSettled(r);
        Check.That(GroundTiles.BuiltLayersDrawn == drawn, $"the strokes had {GroundTiles.BuiltLayersDrawn - drawn} tiles draw the town's parts again");
        var after = await Card(r, t, cards);
        Check.That(Apart(built, after) < SameCardWithin, $"the card keeps the town as drawn: {Apart(built, after):0.000} apart after the strokes");
        Check.That(GroundTiles.BuiltLayersDrawn == drawn, $"and drawing it had {GroundTiles.BuiltLayersDrawn - drawn} tiles draw the town's parts again");
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), NearAcross);
        await r.Frames(SettleFrames);
        await GroundTiles.WhenSettled(r);
        Check.That(GroundTiles.BuiltLayersDrawn > drawn, "a closer look draws the town's parts into finer tiles");
        Check.That(BuiltLayer.MadeWhileDrawing == 0, $"none of them shaped on the frame that drew it: {BuiltLayer.MadeWhileDrawing}");
    }
}
