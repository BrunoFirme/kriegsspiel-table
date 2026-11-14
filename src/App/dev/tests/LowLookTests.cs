using System;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.App;

static class LowLookTests
{
    const float SheetSide = 100, Fov = 60, ScreenSide = 1000, Near = 0.05f, Far = 4000, Above = 50, LowHeight = 5, LowBack = 60;
    const float Within = 0.02f, AboutALevel = 0.9f, FacingRise = 0.5f;
    static readonly Vector2 Middle = new(0.5f, 0.5f), NearSpot = new(0.5f, 0.9f), FarSpot = new(0.5f, 0.1f);

    static Vector3 World(Vector2 uv, float height) => new((uv.X - 0.5f) * SheetSide, height, (uv.Y - 0.5f) * SheetSide);

    static TileSight.Ground Seen(Vector3 eye, Vector3 at, Vector3 up) =>
        new TileSight(new Transform3D(Basis.Identity, eye).LookingAt(at, up), Projection.CreatePerspective(Fov, 1, Near, Far, false), new Vector2(ScreenSide, ScreenSide), Near).Of(World);

    static TileSight.Ground FromAbove => Seen(new Vector3(0, Above, 0), Vector3.Zero, Vector3.Forward);

    static TileSight.Ground FromLow => Seen(new Vector3(0, LowHeight, LowBack), Vector3.Zero, Vector3.Up);

    [NodeTest]
    static Task Seen_from_above_the_ground_is_called_at_the_level_that_gives_a_pixel_its_texel(TestRunner r)
    {
        float perPixel = 2 * Above * Mathf.Tan(Mathf.DegToRad(Fov / 2)) / ScreenSide / SheetSide;
        float level = TileSight.Sharper - MathF.Log2(perPixel * (GroundTiles.TilePx - 2 * GroundTiles.GutterPx));
        float? called = FromAbove.Called(Middle, 0, Vector2.Zero, 0);
        Check.That(called is { } c && Math.Abs(c - level) < Within, $"the middle is called at level {called:0.000} where a texel a pixel is {level:0.000}");
        Check.That(FromAbove.Called(Vector2.Zero, 0, Vector2.Zero, 0) == null, "a corner off the screen is called at no level");
        var tile = new Rect2(Middle, Vector2.One / 8);
        int whole = (int)MathF.Floor(level);
        Check.That(FromAbove.Calls(tile, _ => 0, 0, whole) == TileSight.Call.Finer, $"a tile of level {whole} there is too coarse");
        Check.That(FromAbove.Calls(tile, _ => 0, 0, whole + 1) == TileSight.Call.Met, $"one of level {whole + 1} is fine enough");
        Check.That(FromAbove.Calls(new Rect2(Vector2.Zero, Vector2.One / 8), _ => 0, 0, 0) == TileSight.Call.Unseen, "a tile off the screen calls for nothing");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_low_look_calls_for_finer_ground_near_than_far_and_softens_the_near_ground_first(TestRunner r)
    {
        float near = FromLow.Called(NearSpot, 0, Vector2.Zero, 0)!.Value, far = FromLow.Called(FarSpot, 0, Vector2.Zero, 0)!.Value;
        Check.That(near > far + 1, $"near ground is called at {near:0.00}, far ground at {far:0.00}");
        float nearSoft = FromLow.Called(NearSpot, 0, Vector2.Zero, 1)!.Value, farSoft = FromLow.Called(FarSpot, 0, Vector2.Zero, 1)!.Value;
        Check.That(near - nearSoft > AboutALevel, $"a level of softening takes {near - nearSoft:0.00} off the near ground");
        Check.That(Math.Abs(far - farSoft) < Within, $"and {far - farSoft:0.00} off ground seen so flat that a pixel already spans many texels");
        Check.That(FromLow.Called(FarSpot, 0, Vector2.Zero, 3)!.Value < farSoft - 1, "more softening reaches the far ground too");
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_slope_that_faces_the_eye_is_called_finer_than_level_ground(TestRunner r)
    {
        float level = FromLow.Called(FarSpot, 0, Vector2.Zero, 0)!.Value;
        float facing = FromLow.Called(FarSpot, 0, new Vector2(0, -FacingRise * SheetSide), 0)!.Value;
        Check.That(facing > level + 1, $"the slope is called at {facing:0.00}, level ground there at {level:0.00}");
        float rising(Vector2 uv) => (FarSpot.Y + 0.1f - uv.Y) * FacingRise * SheetSide;
        var tile = new Rect2(FarSpot - Vector2.One / 32, Vector2.One / 16);
        int whole = (int)MathF.Floor(level) + 1;
        Check.That(FromLow.Calls(tile, _ => 0, 0, whole) == TileSight.Call.Met, $"level ground there is met by a tile of level {whole}");
        Check.That(FromLow.Calls(tile, rising, 0, whole) == TileSight.Call.Finer, "the same tile on the slope is too coarse");
        return Task.CompletedTask;
    }

    const double Side = 120, UnitsPerMetre = 0.02;
    const int Lattice = 256, SettleFrames = 12;
    const float LowPitchDegrees = 12, LowAcross = 16, ShortFrom = 0.3f;

    static int Finest(GroundTiles.Sheet sheet, Vector2 uv)
    {
        for (int level = sheet.Levels; level >= 0; level--)
            if (sheet.Baked.ContainsKey(new GroundTiles.Tile(level, (int)(uv.X * (1 << level)), (int)(uv.Y * (1 << level))))) return level;
        return -1;
    }

    static async Task<(int Seen, int Short, float Worst)> Shortfall(TestRunner r, OfflineTable t)
    {
        await r.Frames(SettleFrames);
        await GroundTiles.WhenSettled(r);
        await r.Frames(SettleFrames);
        await GroundTiles.WhenSettled(r);
        var sight = new TileSight(t.Table.Rig.Cam);
        int seen = 0, shortOf = 0;
        float worst = 0;
        foreach (var sheet in GroundTiles.Showing)
        {
            var ground = sight.Of(sheet.World);
            for (int j = 0; j < Lattice; j++)
                for (int i = 0; i < Lattice; i++)
                {
                    var uv = new Vector2(i + 0.5f, j + 0.5f) / Lattice;
                    if (ground.Called(uv, 0, Vector2.Zero, GroundTiles.Relaxed) is not { } called) continue;
                    seen++;
                    float lacks = Math.Min(called, sheet.Levels) - Finest(sheet, uv);
                    worst = Math.Max(worst, lacks);
                    if (lacks > ShortFrom) shortOf++;
                }
        }
        return (seen, shortOf, worst);
    }

    [ScreenTest]
    static async Task A_low_look_over_the_ground_has_the_tiles_its_pixels_call_for(TestRunner r)
    {
        var t = OfflineTable.Make(r, pieces: 0);
        await r.Frame();
        var flat = ReliefGrid.Flat(Side, Side);
        var (sha, _) = ReliefImport.Save(flat, t.S.Store.Root);
        t.S.Relief.Remember(sha, flat);
        t.S.Do(new AddSheet { Sheet = new SheetDef { Id = "g", Name = "Ground", Image = "", Package = UploadAsset.ReliefPackage, Size = new Size2(Side, Side), Relief = sha, Height = 1 } });
        t.S.Do(new SetCalibration { UnitsPerMeter = UnitsPerMetre });
        var rig = t.Table.Rig;
        if (!rig.TopDown) rig.ToggleTopDown();
        rig.Frame(Coords.ToWorld(new Vec2(0, 0)), (float)Side);
        var above = await Shortfall(r, t);
        Check.That(above.Seen > Lattice && above.Short == 0, $"from above {above.Short} of {above.Seen} seen places are short of their tiles, the worst by {above.Worst:0.00} of a level");
        Check.That(GroundTiles.Relaxed == 0, $"from above the ground is drawn at full sharpness, not softened by {GroundTiles.Relaxed}");
        rig.ToggleTopDown();
        rig.Pitch = -Mathf.DegToRad(LowPitchDegrees);
        rig.Frame(Coords.ToWorld(new Vec2(0, 0)), LowAcross);
        var low = await Shortfall(r, t);
        Check.That(low.Seen > Lattice && low.Short == 0, $"in a low look {low.Short} of {low.Seen} seen places are short of their tiles, the worst by {low.Worst:0.00} of a level, softened by {GroundTiles.Relaxed}");
        Check.That(GroundTiles.Settled, "and every tile the look calls for is drawn");
    }
}
