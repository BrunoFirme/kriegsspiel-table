using System.Diagnostics;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public class FootingTests
{
    const double Side = 60, MetresToTheUnit = 5, Grade = 0.35;
    static readonly Spot Middle = new(30, 30);

    static ReliefGrid Hill(double grade = Grade)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)(y / (double)(grid.Rows - 1) * Side * grade);
            }
        return grid;
    }

    static BuiltSite Site(ReliefGrid grid, int manner = Manners.Continental) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Manner = manner };

    static BuiltThing Village(BuiltSite site, int seed = 2) => Settlements.Lay(site, "s", Middle, 40 / MetresToTheUnit * 4, seed, 0.6, 0, Plans.Value(Plans.Grown));

    static double Bare(ReliefGrid grid, Spot p) => grid.HeightAt(p.X / Side, p.Y / Side);

    static List<Footings.Pad> Pads(ReliefGrid grid, BuiltThing thing) => Footings.Of(thing, p => Bare(grid, p), MetresToTheUnit, 1 / MetresToTheUnit).ToList();

    static double Metres(double units) => units * MetresToTheUnit;

    [Fact]
    public void Houses_on_a_hillside_stand_on_level_pads_cut_into_the_slope_and_removing_the_place_heals_the_ground()
    {
        var grid = Hill();
        var town = Village(Site(grid));
        var pads = Pads(grid, town);
        Assert.True(Footings.Marked(town), "a place laid now is marked as fitting its ground");
        Assert.True(pads.Count > 20, $"{pads.Count} pads");
        var shaped = Earthworks.Shaped(grid, [town], Side, Side, 1 / MetresToTheUnit, 1);
        double Worked(Spot p) => shaped.HeightAt(p.X / Side, p.Y / Side);
        foreach (var pad in pads.Where(p => p.Carved).Take(25))
        {
            var mid = new Spot(pad.Ring.Average(p => p.X), pad.Ring.Average(p => p.Y));
            Assert.InRange(Metres(Worked(mid) - pad.Level), -0.3, 0.3);
            double low = pad.Ring.Min(p => Bare(grid, p)), high = pad.Ring.Max(p => Bare(grid, p));
            Assert.True(pad.Level > low && pad.Level < high - (high - low) * 0.5, "a pad is cut into the slope more than it is built out");
        }
        var bare = Earthworks.Shaped(grid, [], Side, Side, 1 / MetresToTheUnit, 1);
        Assert.Equal(Bare(grid, Middle), bare.HeightAt(0.5, 0.5), 4);
    }

    [Fact]
    public void Houses_on_the_shoulder_of_a_tell_stand_on_level_pads_cut_into_the_mound()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                bool river = x < grid.Cols * 0.12;
                grid.Kind[y * grid.Cols + x] = river ? GroundKinds.Water : GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)((river ? 1 : 2 + x / (double)(grid.Cols - 1) * 0.25) / MetresToTheUnit);
            }
        var town = Settlements.Lay(Site(grid, Manners.Egyptian), "s", Middle, 24, 1, 0.8, 0, Plans.Value(Plans.Offered(Manners.Egyptian)[0]));
        Assert.Contains(town.Parts, p => p.Class == HillForms.Tell);
        var bare = town.Clone();
        bare.Settings.Remove(Footings.Mark);
        var mound = Earthworks.Shaped(grid, [bare], Side, Side, 1 / MetresToTheUnit, 1);
        var shaped = Earthworks.Shaped(grid, [town], Side, Side, 1 / MetresToTheUnit, 1);
        static List<Spot> Inner(List<Spot> ring) => ring.Select(p => p + (new Spot(ring.Average(q => q.X), ring.Average(q => q.Y)) - p) * 0.4).ToList();
        double Across(ReliefGrid g, List<Spot> ring) => Metres(ring.Max(p => g.HeightAt(p.X / Side, p.Y / Side)) - ring.Min(p => g.HeightAt(p.X / Side, p.Y / Side)));
        var shoulder = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "villa").Select(p => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList()).Select(Inner)
            .Where(ring => Across(mound, ring) > 0.5).ToList();
        Assert.True(shoulder.Count >= 3, $"{shoulder.Count} houses on the shoulder");
        Assert.True(shoulder.Count(ring => Across(shaped, ring) < Across(mound, ring) * 0.5) >= shoulder.Count * 0.8, "houses on the shoulder stand level");
    }

    [Fact]
    public void A_house_moved_by_hand_takes_its_footing_with_it()
    {
        var grid = Hill();
        var town = Village(Site(grid));
        int house = town.Parts.FindIndex(p => p.Kind == PartKinds.Block && p.Class == "house");
        var before = Pads(grid, town).Select(p => new Spot(p.Ring.Average(q => q.X), p.Ring.Average(q => q.Y))).ToList();
        var moved = BuiltThings.WithPartMoved(town, house, 2000, 0);
        var after = Pads(grid, moved).Select(p => new Spot(p.Ring.Average(q => q.X), p.Ring.Average(q => q.Y))).ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.Single(after.Except(before));
    }

    [Fact]
    public void A_town_laid_before_footings_keeps_its_ground_exactly()
    {
        var grid = Hill();
        var town = Village(Site(grid));
        var old = town.Clone();
        old.Settings.Remove(Footings.Mark);
        Assert.Empty(Pads(grid, old));
        Assert.DoesNotContain(Earthworks.Seen(grid, [old], Side, Side, 1 / MetresToTheUnit, 1)[0].Parts, p => p.Class == Footings.Revetment);
    }

    [Fact]
    public void Retaining_walls_stand_where_a_pad_edge_drops_a_metre_and_carry_their_ticks_on_the_low_side()
    {
        var grid = Hill(0.45);
        var town = Village(Site(grid));
        var walls = Earthworks.Seen(grid, [town], Side, Side, 1 / MetresToTheUnit, 1)[0].Parts.Where(p => p.Class == Footings.Revetment).ToList();
        Assert.True(walls.Count > 10, $"{walls.Count} retaining walls");
        foreach (var wall in walls.Take(20))
        {
            var at = BuiltGeometry.Points(wall.At).Select(Spot.Of).ToList();
            var along = (at[^1] - at[0]).Unit;
            var mid = (at[0] + at[^1]) * 0.5;
            Assert.True(Bare(grid, mid + along.Left * 0.4) <= Bare(grid, mid - along.Left * 0.4) + 1e-9, "the left of a retaining wall is its low side");
        }
    }

    [Fact]
    public void Each_culture_meets_the_slope_its_own_way()
    {
        var grid = Hill();
        var stone = Pads(grid, Village(Site(grid, Manners.Byzantine)));
        Assert.All(stone, pad => Assert.False(pad.Carved, "a stone ground floor holds the slope itself"));
        var yards = Pads(grid, Village(Site(grid, Manners.Saracen)));
        Assert.All(yards, pad => Assert.InRange(pad.Level, pad.Ring.Min(p => Bare(grid, p)), Bare(grid, new Spot(pad.Ring.Average(p => p.X), pad.Ring.Average(p => p.Y)))));
        Assert.Empty(Pads(grid, Village(Site(grid, Manners.Pacific))));
    }

    const double TownM = 520, BigSide = 60, BigMetres = 25, LayMostS = 2.5, WorksMostS = 3;

    [Fact]
    [Trait("Category", "Timing")]
    public void A_city_on_a_steep_hillside_is_laid_and_fitted_to_its_ground_within_the_time_of_a_drag()
    {
        var grid = ReliefGrid.Flat(BigSide, BigSide);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++) grid.H[y * grid.Cols + x] = (float)(y / (double)(grid.Rows - 1) * BigSide * 0.4);
        var site = new BuiltSite { Width = BigSide, Height = BigSide, UnitsPerMetre = 1 / BigMetres, Grid = grid, HeightUnit = 1, Manner = Manners.Gothic };
        var watch = Stopwatch.StartNew();
        var city = Settlements.Lay(site, "s", new Spot(30, 30), TownM / BigMetres, 4, 0.8, 0, Plans.Value(Plans.Grown));
        var laid = watch.Elapsed;
        Assert.True(laid < TimeSpan.FromSeconds(LayMostS), $"laying took {laid.TotalSeconds:0.00} s");
        watch.Restart();
        Earthworks.Lay(grid, [city], BigSide, BigSide, 1 / BigMetres, 1);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(WorksMostS), $"fitting its works took {watch.Elapsed.TotalSeconds:0.00} s");
    }

    [Fact]
    public void A_pad_is_cut_into_the_ground_only_where_the_ground_is_fine_enough_to_hold_it()
    {
        var grid = Hill();
        var pad = Pads(grid, Village(Site(grid))).First(p => p.Carved);
        Assert.True(Footings.Fits(pad, 0.4 / MetresToTheUnit));
        Assert.False(Footings.Fits(pad, 20 / MetresToTheUnit));
    }

    static ReliefGrid Cliff()
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1);
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)(u * Side * 0.08 + 30 / MetresToTheUnit / (1 + Math.Exp((u - 0.55) * 90)));
            }
        return grid;
    }

    static List<Spot> Ring(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    [Fact]
    public void A_house_on_a_brink_or_steep_ground_stands_with_its_door_by_the_street_and_at_most_two_storeys_of_cellar()
    {
        var grid = Cliff();
        foreach (int manner in new[] { Manners.American, Manners.Gothic, Manners.Byzantine, Manners.Continental })
            foreach (int seed in Enumerable.Range(1, 3))
            {
                var site = Site(grid, manner);
                var town = Village(site, seed);
                var houses = town.Parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 6 && !p.Kept).ToList();
                Assert.True(houses.Count > 20, $"{Manners.Named(manner).Id} seed {seed}: {houses.Count} buildings");
                var footing = Slopes.Of(manner).House;
                foreach (var house in houses)
                {
                    var ring = Ring(house);
                    double Up(Spot p) => Metres(Bare(grid, p));
                    double low = ring.Min(Up), high = ring.Max(Up), floor = Footings.Floor(footing, low, high, (Up(ring[0]) + Up(ring[1])) / 2, Footings.StoreyM);
                    Assert.True(floor - low <= Footings.CellarMostM + 0.5, $"{Manners.Named(manner).Id} {house.Class}: {floor - low:0.0} m under its floor");
                    Assert.True(floor - (Up(ring[0]) + Up(ring[1])) / 2 <= Footings.StoopM + 0.5, $"{Manners.Named(manner).Id} {house.Class}: door {floor - (Up(ring[0]) + Up(ring[1])) / 2:0.0} m above its street");
                }
            }
    }

    [Fact]
    public void A_bank_house_takes_its_floor_from_the_street_it_fronts_and_sinks_at_most_a_storey_into_the_hill()
    {
        Assert.Equal(13, Footings.Floor(Footing.Bank, 10, 13, 13, Footings.StoreyM), 6);
        Assert.Equal(13 - Footings.StoreyM, Footings.Floor(Footing.Bank, 10, 13, 10, Footings.StoreyM), 6);
        Assert.Equal(11, Footings.Floor(Footing.StoneBase, 10, 13, 11, Footings.StoreyM), 6);
        Assert.False(Footings.Sits(Footing.Bank, 0, 9, 9), "a cellar three storeys deep");
        Assert.False(Footings.Sits(Footing.Bank, 0, 5, 0), "a door two metres over its street");
        Assert.True(Footings.Sits(Footing.Bank, 0, 5, 5));
        Assert.True(Footings.Sits(Footing.Stilts, 0, 4, 0), "a house on posts is climbed by a ladder");
    }

    [Fact]
    public void Towns_whose_builders_bank_their_cuts_in_earth_lay_no_masonry_retaining_walls()
    {
        var grid = Hill(0.45);
        foreach (int manner in new[] { Manners.American, Manners.Rus, Manners.Tribal })
            Assert.DoesNotContain(Earthworks.Seen(grid, [Village(Site(grid, manner))], Side, Side, 1 / MetresToTheUnit, 1)[0].Parts, p => p.Class == Footings.Revetment);
        Assert.Contains(Earthworks.Seen(grid, [Village(Site(grid, Manners.Gothic))], Side, Side, 1 / MetresToTheUnit, 1)[0].Parts, p => p.Class == Footings.Revetment);
    }
}
