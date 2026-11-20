using System.Linq;
using System.Threading.Tasks;
using Godot;
using KTable.Core.Model;

namespace KTable.App;

static class TileReachTests
{
    const double UsualSide = 40, UsualPerMetre = 0.02, EarthM = 40_075_000, PlanetWide = 409.6, PlanetHigh = 204.8, PlanetPerMetre = PlanetWide / EarthM;
    const double HouseLongM = 14, HouseDeepM = 9, ApartM = 25, RowM = 2000, TileM = 100;
    const float TilePx = 256, EdgePx = 6;
    const int Houses = (int)(RowM / ApartM) + 1;

    static BuiltThing Row(double perMetre, double fromX, double atZ)
    {
        var parts = Enumerable.Range(0, Houses)
            .Select(i => BuiltSite.Block("house", new Spot(fromX + i * ApartM * perMetre, atZ), new Spot(1, 0), HouseLongM * perMetre, HouseDeepM * perMetre)).ToList();
        return new BuiltThing { Id = "row", Kind = BuiltKinds.Building, Settings = { [BuiltThings.Scale] = perMetre }, Parts = parts };
    }

    static int Within(BuiltPainter.Shape[] shapes, Rect2 uv, float reachUv) => shapes.Count(s => s.Box.Intersects(uv.Grow(reachUv), true));

    static int Drawn(BuiltPainter painter, Rect2 uv) => painter.Layer(uv, TilePx, 0)?.Count ?? 0;

    [NodeTest]
    static Task A_ground_tile_on_a_usual_map_draws_the_shapes_it_drew_before(TestRunner r)
    {
        var row = Row(UsualPerMetre, 2, UsualSide / 2);
        var painter = new BuiltPainter();
        painter.Set([row], UsualSide, UsualSide, UsualPerMetre);
        var shapes = BuiltPainter.ShapesOf(row, UsualSide, UsualSide, (float)UsualPerMetre).ToArray();
        float before = Mathf.Max(BuiltHand.WidestUnits, BuiltHand.WidestM * (float)UsualPerMetre) / (float)UsualSide;
        foreach (double across in new[] { 0.3, 0.6, 1.25, 2.5 })
        {
            float side = (float)(across / UsualSide);
            foreach (double x in new[] { 1.0, 3.0, 20.0, 37.5 })
            {
                var uv = new Rect2((float)(x / UsualSide), 0.5f - side / 2, side, side);
                int was = Within(shapes, uv, before), now = Drawn(painter, uv);
                Check.That(now == was, $"a {across} u tile at x {x} draws {now} shapes where it drew {was}");
            }
        }
        return Task.CompletedTask;
    }

    [NodeTest]
    static Task A_ground_tile_on_a_planet_sized_map_draws_only_the_houses_near_it(TestRunner r)
    {
        double mid = PlanetWide / 2;
        var row = Row(PlanetPerMetre, mid, PlanetHigh / 2);
        var painter = new BuiltPainter();
        painter.Set([row], PlanetWide, PlanetHigh, PlanetPerMetre);
        var shapes = BuiltPainter.ShapesOf(row, PlanetWide, PlanetHigh, (float)PlanetPerMetre).ToArray();
        float sideX = (float)(TileM * PlanetPerMetre / PlanetWide), sideY = (float)(TileM * PlanetPerMetre / PlanetHigh);
        var uv = new Rect2((float)((mid + RowM / 2 * PlanetPerMetre) / PlanetWide), 0.5f - sideY / 2, sideX, sideY);
        float old = BuiltHand.WidestUnits / (float)PlanetWide, widestM = Mathf.Max(BuiltHand.WidestUnits / BuiltHand.UsualPerMetre, BuiltHand.WidestM);
        float reach = Mathf.Max(widestM * (float)PlanetPerMetre / (float)PlanetWide, EdgePx * sideX / TilePx);
        int drawn = Drawn(painter, uv), near = Within(shapes, uv, reach);
        Check.That(Within(shapes, uv, old) == Houses, "the old reach of 1.2 u took in every house of a 2 km row");
        Check.That(drawn == near && drawn > 0 && drawn < Houses / 4, $"a {TileM} m tile in the middle of the row draws {drawn} of {Houses} houses, {near} within reach");
        var far = new Rect2(uv.Position + new Vector2((float)(RowM * PlanetPerMetre / PlanetWide), 0), uv.Size);
        Check.That(Drawn(painter, far) == 0 && !painter.Touches(far, TilePx), "a tile a row's length past its end draws none");
        return Task.CompletedTask;
    }
}
