using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltFitTests
{
    const double Side = 60, MetresToTheUnit = 50, Village = 6, RoomM = 6;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others };

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static IEnumerable<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of);

    static Spot Centre(BuiltPart part) => new(Points(part).Average(p => p.X), Points(part).Average(p => p.Y));

    static BuiltThing Line(string kind, string cls, params Spot[] through) => new() { Kind = kind, Parts = { BuiltSite.Line(cls, through) } };

    [Fact]
    public void A_village_is_more_than_rows_of_houses()
    {
        var villages = Enumerable.Range(1, 5).Select(seed => Settlements.Lay(Site(), "s", Middle, Village, seed, 0.4, 0, 0)).ToList();
        Assert.All(villages, v =>
        {
            Assert.Equal(1, Count(v, PartKinds.Area, BuiltThings.Quarter));
            Assert.Equal(1, Count(v, PartKinds.Block, "steeple"));
            Assert.Equal(1, Count(v, PartKinds.Area, "churchyard"));
            Assert.True(Count(v, PartKinds.Sign, "tree") > 3, "trees stand about the place");
            Assert.True(Count(v, PartKinds.Area, "garden") + Count(v, PartKinds.Area, "orchard") > 3, "houses have their crofts");
        });
        Assert.Contains(villages, v => Count(v, PartKinds.Block, "shed") > 0);
        Assert.Contains(villages, v => Count(v, PartKinds.Block, "barn") > 0);
        Assert.Contains(villages, v => Count(v, PartKinds.Area, "pond") > 0);
        Assert.Contains(villages, v => v.Parts.Any(p => p.Kind == PartKinds.Block && p.Class == "house" && p.At.Count == 12));
        Assert.Contains(villages, v => Count(v, PartKinds.Line, "fence") > 1);
    }

    [Fact]
    public void An_ell_is_a_house_with_a_wing_behind_it()
    {
        var ell = BuiltSite.Ell("house", new Spot(10, 10), new Spot(1, 0), new Spot(0, 1), 4, 2, 1.5, 1, atEnd: true);
        var points = Points(ell).ToList();
        Assert.Equal(6, points.Count);
        Assert.Equal(12, points.Max(p => p.Y), 3);
        Assert.Equal(12, points.Max(p => p.X), 3);
        Assert.Equal(8, points.Min(p => p.X), 3);
    }

    [Fact]
    public void Houses_keep_off_a_built_river_and_a_mill_stands_by_it()
    {
        var river = Line(BuiltKinds.Water, "river", new Spot(5, 27), new Spot(55, 27));
        var site = Site(river);
        var villages = Enumerable.Range(1, 6).Select(seed => Settlements.Lay(site, "s", Middle, Village, seed, 0.6, 0, 0)).ToList();
        Assert.All(villages, v => Assert.All(v.Parts.Where(p => p.Kind == PartKinds.Block && p.Class != "mill"), p => Assert.True(Math.Abs(Centre(p).Y - 27) > site.M(RoomM), p.Class)));
        Assert.All(villages, v => Assert.All(v.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == "lane"), p => Assert.True(Points(p).All(q => q.Y > 27) || Points(p).All(q => q.Y < 27))));
        Assert.Contains(villages, v => Count(v, PartKinds.Sign, "wheel") == 1 && Count(v, PartKinds.Block, "mill") >= 1);
    }

    [Fact]
    public void A_place_and_the_country_keep_out_of_a_place_already_there()
    {
        var first = Settlements.Lay(Site(), "s", Middle, 5, 3, 0.6, 0, 0);
        var second = Settlements.Lay(Site(first), "s", new Spot(37, 30), 5, 4, 0.6, 0, 0);
        Assert.True(Count(second, PartKinds.Block, "house") > 5);
        Assert.All(second.Parts.Where(p => p.Kind == PartKinds.Block), p => Assert.True((Centre(p) - Middle).Length >= 5));
        var country = BuiltDetails.Lay(Site(first), "s", [Middle], 14, 5, 0.8, 0.6);
        var hedges = country.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "hedge" or "ditch").ToList();
        Assert.NotEmpty(hedges);
        Assert.All(hedges, p => Assert.All(Points(p), q => Assert.True((q - Middle).Length >= 5 - 0.01)));
    }

    [Fact]
    public void A_town_beside_a_road_grows_a_street_through_its_middle()
    {
        var road = Line(BuiltKinds.Road, "road", new Spot(25, 5), new Spot(25, 55));
        var town = Settlements.Lay(Site(road), "s", Middle, Village, 9, 0.7, 0, 0);
        Assert.Contains(town.Parts, p => p.Kind == PartKinds.Line && p.Class == "street" && Points(p).Any(q => (q - Middle).Length < 0.01));
        Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block && p.Class == "house" && Centre(p).X > Middle.X) > 10, "houses stand on the far half too");
    }

    [Fact]
    public void A_fort_is_furnished_and_a_redoubt_only_flies_its_flag()
    {
        var fort = Forts.Lay(Site(), "s", Middle, 5, 2, 5, 0.5, 0);
        Assert.Equal(1, Count(fort, PartKinds.Sign, "flag"));
        Assert.Equal(1, Count(fort, PartKinds.Block, "magazine"));
        Assert.Equal(1, Count(fort, PartKinds.Sign, "well"));
        Assert.Equal(1, Count(fort, PartKinds.Block, "public"));
        var redoubt = Forts.Lay(Site(), "s", Middle, 2, 2, 0, 0.5, 0);
        Assert.Equal(1, Count(redoubt, PartKinds.Sign, "flag"));
        Assert.Equal(0, Count(redoubt, PartKinds.Block, "magazine"));
        Assert.Equal(5 * Forts.Reach, BuiltThings.Round(fort).Reach, 2);
    }

    [Fact]
    public void Farms_and_hedges_come_with_trees_ricks_and_orchards()
    {
        var country = BuiltDetails.Lay(Site(), "s", [new Spot(12, 12), new Spot(48, 48)], 22, 3, 0.6, 0.8);
        Assert.True(Count(country, PartKinds.Block, "farm") > 3);
        Assert.True(Count(country, PartKinds.Sign, "tree") > 10, "hedgerow and yard trees");
        Assert.True(Count(country, PartKinds.Sign, "rick") > 0);
        Assert.True(Count(country, PartKinds.Area, "orchard") > 0);
        Assert.Equal(Json.Serialize(country), Json.Serialize(BuiltDetails.Lay(Site(), "s", [new Spot(12, 12), new Spot(48, 48)], 22, 3, 0.6, 0.8)));
    }
}
