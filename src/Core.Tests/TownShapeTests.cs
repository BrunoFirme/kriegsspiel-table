using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TownShapeTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 9;
    static readonly Spot Middle = new(30, 30);

    static ReliefGrid Land(Func<double, double, byte> kind, Func<double, double, double>? height = null)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1), v = y / (double)(grid.Rows - 1);
                grid.Kind[y * grid.Cols + x] = kind(u, v);
                grid.H[y * grid.Cols + x] = (float)(height?.Invoke(u, v) ?? 0);
            }
        return grid;
    }

    static readonly ReliefGrid Open = Land((_, _) => GroundKinds.Grass);

    static BuiltSite Site(ReliefGrid grid, int manner = Manners.Continental, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Others = others, Manner = manner };

    static BuiltThing Town(BuiltSite site, int seed = 3, double houses = 0.7, string plan = Plans.Grown, Settlements.Shape? shape = null, double reach = Reach) =>
        Settlements.Lay(site, "s", Middle, reach, seed, houses, 0, Plans.Value(plan), false, null, shape);

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static List<BuiltPart> Homes(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "shop" or "townhouse" or "cottage").ToList();

    static List<List<Spot>> Streets(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane" or "road").Select(Points).ToList();

    static double Long(List<Spot> line) => Enumerable.Range(0, line.Count - 1).Sum(i => (line[i + 1] - line[i]).Length);

    [Fact]
    public async Task A_town_laid_again_on_a_map_whose_scale_was_taken_away_stays_as_it_was_at_once()
    {
        var town = Town(Site(Open));
        var unscaled = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 0, Grid = Open, HeightUnit = 1, Manner = Manners.Continental };
        var again = Task.Run(() => BuiltThings.Remade(unscaled, town, 9));
        Assert.Same(again, await Task.WhenAny(again, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Equal(town.Parts.Count, (await again).Parts.Count);
    }

    [Fact]
    public void A_town_stops_at_the_wood_and_the_water_inside_the_circle_it_was_given()
    {
        var land = Land((u, v) => u > 0.56 ? GroundKinds.Wood : v > 0.6 ? GroundKinds.Water : GroundKinds.Grass);
        var site = Site(land);
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var homes = Homes(Town(site, seed));
            Assert.True(homes.Count > 60, $"seed {seed}: {homes.Count} houses on the open ground");
            Assert.All(homes, home => Assert.True(Centre(home).X < 0.56 * Side + 0.3, "no house stands in the wood"));
            Assert.All(homes, home => Assert.True(Centre(home).Y < 0.6 * Side + 0.3, "no house stands in the water"));
        }
    }

    [Fact]
    public void No_two_towns_come_out_alike_and_none_is_a_filled_circle()
    {
        var towns = Enumerable.Range(1, 5).Select(seed => Town(Site(Open), seed)).ToList();
        var shapes = towns.Select(town => Enumerable.Range(0, 12).Select(i =>
            Homes(town).Select(Centre).Where(p => Math.Abs(Math.IEEERemainder(Math.Atan2(p.Y - Middle.Y, p.X - Middle.X) - i * Math.PI / 6, 2 * Math.PI)) < Math.PI / 12)
                .Select(p => (p - Middle).Length).DefaultIfEmpty(0).Max()).ToList()).ToList();
        Assert.All(shapes, reaches => Assert.True(reaches.Max() > reaches.Min() * 1.25 + 0.5, $"its edge runs from {reaches.Min():0.0} to {reaches.Max():0.0} from the middle"));
        for (int a = 0; a < shapes.Count; a++)
            for (int b = a + 1; b < shapes.Count; b++)
                Assert.True(Enumerable.Range(0, 12).Sum(i => Math.Abs(shapes[a][i] - shapes[b][i])) > Reach * 0.8, $"towns {a + 1} and {b + 1} have the same outline");
        Assert.All(towns, town => Assert.True(Homes(town).Count > 130, $"{Homes(town).Count} homes"));
    }

    [Fact]
    public void Streets_end_on_other_streets_far_more_often_than_they_end_blind()
    {
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var streets = Streets(Town(Site(Open), seed));
            double near = 4 / MetresToTheUnit;
            bool Meets(Spot end, List<Spot> own) =>
                streets.Any(other => other != own && Enumerable.Range(0, other.Count - 1).Any(i => end.FromSegment(other[i], other[i + 1]) < near));
            var ends = streets.SelectMany(street => new[] { (street[0], street), (street[^1], street) }).ToList();
            int met = ends.Count(end => Meets(end.Item1, end.street));
            Assert.True(streets.Count >= 12, $"seed {seed}: {streets.Count} streets");
            Assert.True(met > ends.Count * 0.72, $"seed {seed}: {met} of {ends.Count} street ends meet a street");
        }
    }

    [Fact]
    public void On_a_hillside_a_town_lies_along_the_slope_and_its_longest_street_keeps_the_level()
    {
        var hill = Land((_, _) => GroundKinds.Grass, (_, v) => v * Side * 0.13);
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var town = Town(Site(hill), seed);
            var homes = Homes(town).Select(Centre).ToList();
            double along = homes.Max(p => p.X) - homes.Min(p => p.X), up = homes.Max(p => p.Y) - homes.Min(p => p.Y);
            Assert.True(along > up * 1.15, $"seed {seed}: the houses lie {along:0.0} along the slope and {up:0.0} up it");
            var longest = Streets(town).MaxBy(Long)!;
            Assert.True(Math.Abs(longest[^1].X - longest[0].X) > 2 * Math.Abs(longest[^1].Y - longest[0].Y), $"seed {seed}: the longest street climbs");
        }
    }

    [Fact]
    public void A_village_is_built_on_any_slope_up_to_thirty_degrees_and_none_above_it()
    {
        foreach (double grade in new[] { 0.35, 0.5 })
            foreach (int seed in Enumerable.Range(1, 3))
                Assert.True(Homes(Town(Site(Land((_, _) => GroundKinds.Grass, (_, v) => v * Side * grade)), seed)).Count > 30, $"seed {seed}: houses on a {grade:P0} slope");
        Assert.Empty(Town(Site(Land((_, _) => GroundKinds.Grass, (_, v) => v * Side * 0.65))).Parts);
    }

    [Fact]
    public void A_painted_river_is_crossed_by_one_bridge_and_the_far_bank_is_built_too()
    {
        var river = Land((_, v) => v is > 0.47 and < 0.485 ? GroundKinds.Water : GroundKinds.Grass);
        var towns = Enumerable.Range(1, 4).Select(seed => Town(Site(river), seed)).ToList();
        Assert.All(towns, town =>
        {
            var bridges = town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == "causeway").ToList();
            Assert.Single(bridges);
            Assert.InRange(Long(Points(bridges[0])) * MetresToTheUnit, 15, 80);
            Assert.True(Homes(town).Count(home => Centre(home).Y < 0.47 * Side) > 10 && Homes(town).Count(home => Centre(home).Y > 0.485 * Side) > 10, "houses stand on both banks");
            Assert.All(Homes(town), home => Assert.NotEqual(GroundKinds.Water, Site(river).KindAt(Centre(home))));
        });
    }

    [Fact]
    public void Spread_loosens_a_town_and_Order_straightens_it()
    {
        double Far(BuiltThing town) => Homes(town).Select(home => (Centre(home) - Middle).Length).OrderDescending().Take(20).Average();
        double Bent(BuiltThing town)
        {
            double turned = 0, run = 0;
            foreach (var street in Streets(town))
            {
                run += Long(street);
                for (int i = 1; i + 1 < street.Count; i++)
                    turned += Math.Abs(Math.Atan2((street[i] - street[i - 1]).Unit.X * (street[i + 1] - street[i]).Unit.Y - (street[i] - street[i - 1]).Unit.Y * (street[i + 1] - street[i]).Unit.X,
                        (street[i] - street[i - 1]).Unit.Dot((street[i + 1] - street[i]).Unit)));
            }
            return turned / run;
        }
        double Mean(Func<int, double> of) => Enumerable.Range(1, 4).Average(of);
        double huddled = Mean(seed => Far(Town(Site(Open), seed, shape: new(0, 0.5)))), straggling = Mean(seed => Far(Town(Site(Open), seed, shape: new(1, 0.5))));
        Assert.True(straggling > huddled * 1.08, $"the farthest houses stand {huddled:0.0} out when huddled and {straggling:0.0} when straggling");
        double winding = Mean(seed => Bent(Town(Site(Open), seed, shape: new(0.5, 0)))), laidOut = Mean(seed => Bent(Town(Site(Open), seed, shape: new(0.5, 1))));
        Assert.True(winding > laidOut * 1.4, $"streets turn {winding:0.000} a unit when grown and {laidOut:0.000} when laid out");
        var kept = Town(Site(Open), shape: new(0.2, 0.8, 1));
        Assert.Equal((0.2, 0.8, 1), (kept.Setting(Settlements.Spread, -1), kept.Setting(Settlements.Order, -1), kept.Setting(Settlements.Heading, -1)));
        Assert.Equal(new Settlements.Shape(0.2, 0.8, 1), Settlements.Shape.Of(kept));
    }

    [Fact]
    public void The_line_dragged_is_the_line_of_a_main_street()
    {
        double Off(double heading, double line)
        {
            var mains = Streets(Town(Site(Open), 5, shape: new(0.5, 0.5, heading))).Where(street => street.Any(p => (p - Middle).Length < 0.5) && Long(street) > Reach * 0.4);
            return mains.Min(street => Math.Abs(Math.IEEERemainder(Math.Atan2(street[^1].Y - street[0].Y, street[^1].X - street[0].X) - line, Math.PI / 2))) * 180 / Math.PI;
        }
        Assert.True(Off(0, 0) < 20 && Off(Math.PI / 4, Math.PI / 4) < 20, $"a main street lies {Off(0, 0):0}° off a level drag and {Off(Math.PI / 4, Math.PI / 4):0}° off a slanted one");
        Assert.True(Off(0, Math.PI / 4) > 20, $"dragged level, no main street runs at the slant: {Off(0, Math.PI / 4):0}°");
    }

    [Fact]
    public void A_Soviet_town_is_a_plant_on_the_railway_with_flats_and_barracks_and_a_small_place_is_still_a_village()
    {
        int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);
        var site = Site(Open, Manners.Soviet);
        Assert.Equal((Plans.Grid, Plans.Street), (Plans.Own(Manners.Soviet, 400), Plans.Own(Manners.Soviet, 120)));
        Assert.Equal((Plans.Grid, Plans.Street), (Plans.For(Manners.Soviet, "", 400), Plans.For(Manners.Soviet, Plans.Street, 400)));
        foreach (int seed in Enumerable.Range(1, 3))
        {
            var town = Settlements.Lay(site, "s", Middle, 16, seed, 0.65, 0, Plans.Value(Plans.Own(Manners.Soviet, 16 * MetresToTheUnit)));
            Assert.True(Count(town, PartKinds.Block, "factory") >= 2 && Count(town, PartKinds.Block, "powerhouse") == 1 && Count(town, PartKinds.Block, "coolingtower") >= 1, $"seed {seed}: the plant");
            Assert.True(Count(town, PartKinds.Line, "railway") >= 3, "the line, and sidings into the works");
            Assert.True(Count(town, PartKinds.Block, "flats") >= 6 && Count(town, PartKinds.Block, "barracks") >= 12, $"seed {seed}: {Count(town, PartKinds.Block, "flats")} flats, {Count(town, PartKinds.Block, "barracks")} barracks");
            Assert.Equal(0, Count(town, PartKinds.Block, "church"));
            var hall = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "factory").Max(p => Long(Points(p).Append(Points(p)[0]).ToList()) / 2 * MetresToTheUnit);
            Assert.True(hall > 150, $"its largest hall is {hall:0} m round two sides");
        }
        var village = Settlements.Lay(site, "s", Middle, 5, 2, 0.4, 0, Plans.Value(Plans.Own(Manners.Soviet, 5 * MetresToTheUnit)));
        Assert.Equal((0, 0, 0), (Count(village, PartKinds.Block, "factory"), Count(village, PartKinds.Block, "flats"), Count(village, PartKinds.Block, "barracks")));
        Assert.True(Count(village, PartKinds.Block, "house") > 8 && Count(village, PartKinds.Block, "byre") >= 3);
    }
}
