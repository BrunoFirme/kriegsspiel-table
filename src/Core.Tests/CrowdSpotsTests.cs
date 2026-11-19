using KTable.Core.Model;

namespace KTable.Core.Tests;

public class CrowdSpotsTests
{
    const double Upm = 0.1;
    static readonly Vec2[] Square = [new(0, 0), new(4, 0), new(4, 3), new(0, 3)];
    static CrowdPlace Market => new(0, "square", new Vec2(2, 1.5), CrowdUse.Market, true, 18, null, 0, Square);

    [Fact]
    public void People_at_a_square_stand_on_their_own_spots_across_its_whole_ground_and_off_its_edge()
    {
        var spots = CrowdSpots.Of(Market, Upm);
        Assert.True(spots.Length > 300, $"a 40 by 30 m square holds {spots.Length} spots");
        Assert.All(spots, s => Assert.True(CrowdSpots.Inside(Square, s)));
        Assert.All(spots, s => Assert.True(s.X >= 0.4 - 1e-9 && s.X <= 3.6 + 1e-9 && s.Z >= 0.4 - 1e-9 && s.Z <= 2.6 + 1e-9, $"{s} keeps 4 m off the edge"));
        var taken = Enumerable.Range(0, spots.Length).Select(id => CrowdSpots.For(spots, id)).ToList();
        Assert.Equal(spots.Length, taken.Distinct().Count());
        double least = taken.Take(200).SelectMany((a, i) => taken.Take(200).Skip(i + 1).Select(b => (a - b).Length)).Min();
        Assert.True(least >= (CrowdSpots.ApartM - 2 * CrowdSpots.JitterM) * Upm - 1e-9, $"two people stand {least / Upm:0.00} m apart");
        Assert.True(taken.Take(100).Max(s => (s - Market.At).Length) > 1.0, "the first hundred spread over the square, not round its middle");
    }

    [Fact]
    public void Each_cart_parks_in_its_own_bay_along_the_edge_facing_along_it()
    {
        var bays = Enumerable.Range(0, 20).Select(n => CrowdSpots.Bay(Market, n, Upm)!.Value).ToList();
        Assert.Equal(20, bays.Select(b => b.At).Distinct().Count());
        foreach (var (at, along) in bays)
        {
            double edge = new[] { at.X, 4 - at.X, at.Z, 3 - at.Z }.Min();
            Assert.InRange(edge, CrowdSpots.BayInsetM * Upm - 1e-9, CrowdSpots.BayInsetM * Upm + 1e-9);
            Assert.True(Math.Abs(along.X) > 0.99 || Math.Abs(along.Z) > 0.99, "a bay lies along a side");
        }
        Assert.Null(CrowdSpots.Bay(new CrowdPlace(0, "street", new Vec2(0, 0), CrowdUse.Street, true, 5, null, 0), 3, Upm));
    }

    [Fact]
    public void A_walker_keeps_to_a_lane_beside_the_route_and_steps_onto_it_at_both_ends()
    {
        var route = new CrowdPath([new Vec2(0, 0), new Vec2(0, 10)]);
        foreach (int seed in new[] { 2, 7, 40, 91 })
        {
            var (start, h0) = route.At(0);
            Assert.Equal(start, CrowdSpots.Laned(start, h0, 0, route.Length, seed, Upm));
            var (mid, h) = route.At(5);
            var laned = CrowdSpots.Laned(mid, h, 5, route.Length, seed, Upm);
            double off = Math.Abs(laned.X - mid.X) / Upm;
            Assert.InRange(off, CrowdSpots.LeastLaneM, CrowdSpots.LeastLaneM + CrowdSpots.LaneSpreadM + 1e-9);
            Assert.Equal(mid.Z, laned.Z, 6);
        }
        Assert.NotEqual(Math.Sign(CrowdSpots.Laned(new Vec2(0, 5), 0, 5, 10, 2, Upm).X), Math.Sign(CrowdSpots.Laned(new Vec2(0, 5), 0, 5, 10, 3, Upm).X));
    }

    [Fact]
    public void A_well_takes_a_handful_of_washers_and_the_rest_wash_at_home()
    {
        const double side = 80;
        var g = new GameState { ScenarioId = "t" };
        g.Sheets.Add(new SheetDef { Id = "s", Size = new Size2(side, side) });
        g.Calibration.UnitsPerMeter = Upm;
        var site = new BuiltSite { Width = side, Height = side, UnitsPerMetre = Upm, Grid = ReliefGrid.Flat(side, side), HeightUnit = side, Others = [], Manner = Manners.Continental };
        var built = Settlements.Lay(site, "s", new Spot(side / 2, side / 2), 16, 5, 0.8, 0, Plans.Value(Plans.Offered(Manners.Continental)[0]));
        built.Id = "w-1";
        g.Built.Add(built);
        var town = CrowdRoster.Towns(g, id => CrowdDress.Find(id) is { } n ? CrowdDress.Flat(n) : null, 0).Single();
        var washers = town.Residents.Where(r => r.Entry.Calling == CrowdBaseline.Washer).ToList();
        var atWells = washers.Where(r => r.Work >= 0).GroupBy(r => r.Work).ToList();
        if (town.Of(CrowdUse.Well).Any() && washers.Count > 0) Assert.NotEmpty(atWells);
        Assert.All(atWells, w => Assert.True(w.Count() <= 8, $"{w.Count()} washers at one well"));
    }

    [Fact]
    public void A_walker_moves_between_the_clock_s_whole_seconds()
    {
        var route = new CrowdPath([new Vec2(0, 0), new Vec2(0, 50)]);
        var town = new CrowdTown { Thing = "t", People = CrowdDress.Flat(Nationalities.Named("continental")!), Coat = "#000000", Places = [], Residents = [] };
        var walker = new CrowdResident { Entry = new CrowdEntry { Id = 1, Seed = 4 }, Home = 0, Part = 0, Rank = 0, SpeedM = 1.3 };
        var span = new CrowdSpan(100, 200, -1, route, 0);
        var at = new[] { 120.0, 120.25, 120.5, 120.75, 121.0 }.Select(s => CrowdDay.At(town, walker, span, s, Upm, out var w) ? w.Stride : double.NaN).ToList();
        for (int k = 1; k < at.Count; k++)
            Assert.Equal(0.25 * 1.3 * Upm, at[k] - at[k - 1], 6);
    }
}

