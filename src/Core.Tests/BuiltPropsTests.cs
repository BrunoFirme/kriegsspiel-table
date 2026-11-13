using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltPropsTests
{
    const double Side = 60, MetresToTheUnit = 50;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(ReliefGrid? grid = null) => new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid ?? ReliefGrid.Flat(Side, Side), HeightUnit = Side };

    static List<BuiltPart> Of(BuiltThing t, string kind, string cls) => t.Parts.Where(p => p.Kind == kind && p.Class == cls).ToList();

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static List<BuiltThing> Villages(double radius, double houses, double walls = 0, bool old = false) =>
        Enumerable.Range(1, 6).Select(seed => Settlements.Lay(Site(), "s", Middle, radius, seed, houses, walls, 0, old)).ToList();

    [Fact]
    public void A_square_is_a_market_with_stalls_a_trough_and_goods_and_a_town_adds_fountain_and_stocks()
    {
        var villages = Villages(4, 0.5);
        Assert.All(villages, v =>
        {
            var square = Assert.Single(Of(v, PartKinds.Area, "square"));
            var stalls = Of(v, PartKinds.Sign, "stall");
            Assert.NotEmpty(stalls);
            Assert.All(stalls, s => Assert.True(Enclosures.Inside(Points(square), Centre(s)), "a stall stands on the square"));
            Assert.NotEmpty(Of(v, PartKinds.Sign, "trough"));
            Assert.Empty(Of(v, PartKinds.Sign, "fountain"));
        });
        var towns = Villages(7, 0.7);
        Assert.All(towns, t => Assert.Single(Of(t, PartKinds.Sign, "fountain")));
        Assert.All(towns, t => Assert.Single(Of(t, PartKinds.Sign, "stocks")));
        Assert.All(towns, t => Assert.True(Of(t, PartKinds.Sign, "stall").Count >= 4));
        Assert.Contains(villages.Concat(towns), v => Of(v, PartKinds.Sign, "cart").Count > 0);
        Assert.Contains(villages.Concat(towns), v => Of(v, PartKinds.Sign, "barrels").Count + Of(v, PartKinds.Sign, "crates").Count > 0);
    }

    [Fact]
    public void A_place_has_its_inn_and_smithy_and_the_yards_their_piles_and_privies()
    {
        var villages = Villages(5, 0.5);
        Assert.True(villages.Count(v => Of(v, PartKinds.Block, "inn").Count == 1 && Of(v, PartKinds.Sign, "signboard").Count >= 1) >= 4);
        Assert.True(villages.Count(v => Of(v, PartKinds.Block, "smithy").Count == 1 && Of(v, PartKinds.Sign, "anvil").Count == 1) >= 3);
        Assert.All(villages, v => Assert.True(Of(v, PartKinds.Sign, "woodpile").Count > 0));
        Assert.Contains(villages, v => Of(v, PartKinds.Block, "privy").Count > 0);
        Assert.Contains(villages, v => Of(v, PartKinds.Sign, "hive").Count >= 4);
        Assert.Contains(villages, v => Of(v, PartKinds.Sign, "rock").Count > 0);
    }

    [Fact]
    public void No_lane_cuts_across_the_square_or_the_churchyard()
    {
        Assert.All(Villages(6, 0.6), v =>
        {
            var held = Of(v, PartKinds.Area, "square").Concat(Of(v, PartKinds.Area, "churchyard")).ToList();
            foreach (var lane in Of(v, PartKinds.Line, "lane"))
                foreach (var area in held)
                    Assert.All(Points(lane), p => Assert.False(Enclosures.Inside(Points(area), p), "a lane ends inside " + area.Class));
        });
    }

    [Fact]
    public void A_place_by_the_water_has_jetties_and_boats_and_one_inland_has_none()
    {
        var lake = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < (int)(lake.Rows * 0.42); y++)
            for (int x = 0; x < lake.Cols; x++) lake.Kind[y * lake.Cols + x] = GroundKinds.Water;
        var shore = Enumerable.Range(1, 6).Select(seed => Settlements.Lay(Site(lake), "s", Middle, 5, seed, 0.5, 0, 0)).ToList();
        Assert.All(shore, v =>
        {
            var jetties = Of(v, PartKinds.Line, "jetty");
            Assert.NotEmpty(jetties);
            Assert.All(jetties, j => Assert.True(Points(j)[^1].Y < 60 * 0.42, "a jetty runs out over the water"));
            Assert.True(Of(v, PartKinds.Sign, "boat").Count >= jetties.Count);
        });
        Assert.All(Villages(5, 0.5), v => Assert.Empty(Of(v, PartKinds.Line, "jetty")));
    }

    [Fact]
    public void A_star_fort_has_a_gun_on_each_bastion_and_a_walled_town_its_keep()
    {
        var fort = Forts.Lay(Site(), "s", Middle, 5, 2, 6, 0.5, 0);
        Assert.Equal(6, Of(fort, PartKinds.Sign, "gun").Count);
        Assert.Empty(Of(Forts.Lay(Site(), "s", Middle, 3, 2, 0, 0.5, 0), PartKinds.Sign, "gun"));
        Assert.Empty(Of(Camps.Lay(Site(), "s", Middle, 3, 2, 0), PartKinds.Sign, "gun"));
        var towns = Villages(7, 0.8, 0.5);
        Assert.Contains(towns, t => Of(t, PartKinds.Block, "keep").Count == 1);
        Assert.All(Villages(4, 0.3, 0.5), v => Assert.Empty(Of(v, PartKinds.Block, "keep")));
    }

    [Fact]
    public void An_ancient_place_has_no_gallows()
    {
        Assert.Contains(Enumerable.Range(1, 40).Select(seed => Settlements.Lay(Site(), "s", Middle, 6, seed, 0.6, 0, 0)), t => Of(t, PartKinds.Sign, "gallows").Count == 1);
        Assert.All(Enumerable.Range(1, 40).Select(seed => Settlements.Lay(Site(), "s", Middle, 6, seed, 0.6, 0, 0, old: true)), t => Assert.Empty(Of(t, PartKinds.Sign, "gallows")));
    }
}
