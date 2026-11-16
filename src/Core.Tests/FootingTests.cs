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

    [Fact]
    public void A_pad_is_cut_into_the_ground_only_where_the_ground_is_fine_enough_to_hold_it()
    {
        var grid = Hill();
        var pad = Pads(grid, Village(Site(grid))).First(p => p.Carved);
        Assert.True(Footings.Fits(pad, 0.4 / MetresToTheUnit));
        Assert.False(Footings.Fits(pad, 20 / MetresToTheUnit));
    }
}
