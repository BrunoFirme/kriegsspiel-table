using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;
using Json = KTable.Core.Json;

namespace KTable.App;

static class GroundStrokeTests
{
    const int SettleFrames = 40, CommitFrames = 60, MostFramesToShow = 4, ClearOfTheLabelPx = 80;
    const float ViewAcross = 4, BrushRadius = 2, WaterIsBluerBy = 0.04f, AlikeWithin = 0.04f;
    const double HouseM = 60, AsideAt = -12;
    const double Side = 40, FirstStrokeAt = -15;

    static Color Middle(TestRunner r)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        return shot.GetPixel(shot.GetWidth() / 2, shot.GetHeight() / 2);
    }

    static Color BesideTheMiddle(TestRunner r)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        return shot.GetPixel(shot.GetWidth() / 2 + ClearOfTheLabelPx, shot.GetHeight() / 2 + ClearOfTheLabelPx);
    }

    static bool Water(Color c) => c.B > c.R + WaterIsBluerBy;

    static bool Alike(Color a, Color b) => Mathf.Abs(a.R - b.R) + Mathf.Abs(a.G - b.G) + Mathf.Abs(a.B - b.B) < AlikeWithin;

    static async Task<OfflineTable> Ground(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), ViewAcross);
        return t;
    }

    [ScreenTest]
    static async Task What_a_dab_leaves_is_what_a_full_redraw_of_the_ground_shows(TestRunner r)
    {
        var t = await Ground(r);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = BrushRadius;
        await r.Frames(SettleFrames);
        t.Table.Relief.Press(new Vec2(FirstStrokeAt, 0), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        var left = Middle(r);
        GroundTiles.StaleEverything();
        await r.Frames(CommitFrames);
        Check.That(Water(left) && Alike(left, Middle(r)), $"the dab left {left.ToHtml(false)}, a full redraw shows {Middle(r).ToHtml(false)}");
    }

    [ScreenTest]
    static async Task A_building_laid_and_removed_leaves_the_ground_as_it_was(TestRunner r)
    {
        var t = await Ground(r);
        var sheet = t.S.View.Sheets.Single();
        var site = BuildTool.Site(t.S, sheet);
        BuiltThing House(double at) =>
            BuiltThings.Building(site, sheet.Id, "house", Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(at, at))), new Spot(1, 0), site.M(HouseM), site.M(HouseM));
        t.S.Do(new LayBuilt { Thing = House(AsideAt) });
        await r.Frames(CommitFrames);
        var bare = Middle(r);
        t.S.Do(new LayBuilt { Thing = House(0) });
        await r.Frames(CommitFrames);
        var built = Middle(r);
        GroundTiles.StaleEverything();
        await r.Frames(CommitFrames);
        Check.That(!Alike(bare, built) && Alike(built, Middle(r)), $"the house shows whole at once: {built.ToHtml(false)}, after a full redraw {Middle(r).ToHtml(false)}, bare ground {bare.ToHtml(false)}");
        t.S.Do(new RemoveBuilt { Id = t.S.View.Built[^1].Id });
        await r.Frames(CommitFrames);
        Check.That(Alike(bare, Middle(r)), $"removing it leaves bare ground: {Middle(r).ToHtml(false)} against {bare.ToHtml(false)}");
    }

    const double TownReach = 5, BesideTheTown = 9;
    const int TownSeed = 7, StrokeSteps = 8;

    const double PlotAt = 9, PlotHalf = 2;
    const float NearAcross = 1, FarAcross = 12, NearShare = 0.3f, FarShare = 0.1f, SameLookWithin = 0.06f;

    static Color Mean(TestRunner r, float share)
    {
        var shot = r.GetViewport().GetTexture().GetImage();
        int half = (int)(shot.GetHeight() * share), count = 0;
        float red = 0, green = 0, blue = 0;
        for (int y = shot.GetHeight() / 2 - half; y <= shot.GetHeight() / 2 + half; y++)
            for (int x = shot.GetWidth() / 2 - half; x <= shot.GetWidth() / 2 + half; x++)
            {
                var c = shot.GetPixel(x, y);
                red += c.R;
                green += c.G;
                blue += c.B;
                count++;
            }
        return new Color(red / count, green / count, blue / count);
    }

    [ScreenTest]
    static async Task A_garden_and_a_yard_are_the_same_colour_near_and_from_afar(TestRunner r)
    {
        var t = await Ground(r);
        var sheet = t.S.View.Sheets.Single();
        var at = new Vec2(PlotAt, PlotAt);
        var middle = Spot.Of(BuiltGeometry.ToSheet(sheet, at));
        var plot = new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) }.Select(c => new Spot(middle.X + c.Item1 * PlotHalf, middle.Y + c.Item2 * PlotHalf)).ToArray();
        foreach (string cls in new[] { "garden", "yard" })
        {
            t.S.Do(new LayBuilt { Thing = new BuiltThing { Id = "plot-" + cls, Sheet = sheet.Id, Kind = BuiltKinds.Details, Parts = { BuiltSite.Area(cls, plot) } } });
            t.Table.Rig.Frame(Coords.ToWorld(at), NearAcross);
            await r.Frames(CommitFrames * 2);
            var near = Mean(r, NearShare);
            t.Table.Rig.Frame(Coords.ToWorld(at), FarAcross);
            await r.Frames(CommitFrames * 2);
            var far = Mean(r, FarShare);
            float apart = Mathf.Abs(near.R - far.R) + Mathf.Abs(near.G - far.G) + Mathf.Abs(near.B - far.B);
            Check.That(apart < SameLookWithin, $"a {cls} is {near.ToHtml(false)} near and {far.ToHtml(false)} from afar");
            t.S.Do(new RemoveBuilt { Id = "plot-" + cls });
        }
    }

    [ScreenTest]
    static async Task A_stroke_beside_a_town_draws_none_of_the_town_again(TestRunner r)
    {
        var t = await Ground(r);
        var sheet = t.S.View.Sheets.Single();
        t.S.Do(new LayBuilt { Thing = Settlements.Lay(BuildTool.Site(t.S, sheet), sheet.Id, Spot.Of(BuiltGeometry.ToSheet(sheet, new Vec2(0, 0))), TownReach, TownSeed, 1, 0, 0) });
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = BrushRadius;
        await r.Frames(CommitFrames * 2);
        int drawn = GroundTiles.BuiltLayersDrawn;
        Check.That(drawn > 0, "the town was drawn into the ground's tiles");
        t.Table.Relief.Press(new Vec2(-BesideTheTown, BesideTheTown), false, false);
        for (int i = 1; i <= StrokeSteps; i++)
        {
            t.Table.Relief.Motion(new Vec2(-BesideTheTown + i * 2 * BesideTheTown / StrokeSteps, BesideTheTown));
            await r.Frames(2);
        }
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        Check.That(GroundTiles.BuiltLayersDrawn == drawn, $"the stroke had {GroundTiles.BuiltLayersDrawn - drawn} tiles draw the town's parts again");
        var middle = Middle(r);
        GroundTiles.StaleEverything();
        await r.Frames(CommitFrames);
        Check.That(GroundTiles.BuiltLayersDrawn == drawn && Alike(middle, Middle(r)), $"a full redraw of the ground keeps the town as drawn: {middle.ToHtml(false)} then {Middle(r).ToHtml(false)}");
    }

    static IEnumerable<ReliefSurface> Surfaces(Node under) => under.GetChildren().SelectMany(c => c is ReliefSurface s ? [s] : Surfaces(c));

    [ScreenTest]
    static async Task Paint_over_a_map_picture_is_part_of_the_sheet_s_own_surface(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "p", Name = "Map", Image = "picture-not-here", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), ViewAcross);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = BrushRadius;
        await r.Frames(SettleFrames);
        Check.That(!Water(BesideTheMiddle(r)), $"the map shows before the dab, seen {BesideTheMiddle(r).ToHtml(false)}");
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        Check.That(Water(BesideTheMiddle(r)), $"the water shows on the map, seen {BesideTheMiddle(r).ToHtml(false)}");
        var surfaces = Surfaces(r.GetTree().Root).ToList();
        Check.That(surfaces.Count > 0 && surfaces.All(s => s.PaintBlocks == 0), $"no second surface lies over the map: {surfaces.Sum(s => s.PaintBlocks)} of them");
    }

    [ScreenTest]
    static async Task A_dab_of_water_shows_under_the_brush_within_a_few_frames(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = 0.02 });
        var modern = Json.Clone(t.S.View.Lighting);
        modern.MapStyle = MapStyles.Modern;
        t.S.Do(new SetLighting { Lighting = modern });
        t.Table.ReliefShaped += (_, c) => t.S.Do(c);
        if (!t.Table.Rig.TopDown) t.Table.Rig.ToggleTopDown();
        t.Table.Rig.Frame(Coords.ToWorld(new Vec2(0, 0)), ViewAcross);
        t.Table.SetTool(Tool.Relief);
        var cfg = t.Table.Settings;
        cfg.ReliefMode = "paint:water";
        cfg.ReliefPaint = "brush";
        cfg.ReliefBrush = BrushRadius;
        await r.Frames(SettleFrames);
        t.Table.Relief.Press(new Vec2(FirstStrokeAt, 0), false, false);
        t.Table.Relief.Release();
        await r.Frames(CommitFrames);
        Check.That(!Water(Middle(r)), $"the middle is paper before the dab, seen {Middle(r).ToHtml(false)}");
        t.Table.Relief.Press(new Vec2(0, 0), false, false);
        int frames = 0;
        while (frames < SettleFrames && !Water(Middle(r)))
        {
            await r.Frame();
            frames++;
        }
        t.Table.Relief.Release();
        Check.That(frames <= MostFramesToShow, $"the water shows {frames} frames after the dab, seen {Middle(r).ToHtml(false)}");
    }
}
