using KTable.Core.Model;

namespace KTable.Core.Tests;

public class MedievalCultureTests
{
    const double Side = 60, MetresToTheUnit = 50, Square = 0.02;
    const int FewestTents = 20, FewestCarts = 20, FewestRuins = 20;
    static readonly Spot Middle = new(30, 30);

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Town(int manner, double radius = 6, double houses = 0.75, double walls = 0, double ravaged = 0) =>
        Settlements.Lay(Site(manner), "s", Middle, radius, 4, houses, walls, Plans.Value(Plans.Offered(manner)[0]), shape: new Settlements.Shape(Ravaged: ravaged));

    static int Count(BuiltThing t, string kind, string cls) => t.Parts.Count(p => p.Kind == kind && p.Class == cls);

    static Spot Axis(BuiltPart block)
    {
        var corners = BuiltGeometry.Points(block.At).Select(Spot.Of).ToList();
        Spot one = corners[1] - corners[0], two = corners[2] - corners[1];
        return (one.Length >= two.Length ? one : two).Unit;
    }

    static bool Along(Spot axis, Spot way) => Math.Abs(axis.X * way.Y - axis.Y * way.X) < Square;

    [Fact]
    public void The_medieval_age_offers_its_five_cultures_seat_for_seat()
    {
        Assert.Equal(Manners.Medieval, Manners.Age(MapStyles.Medieval));
        Assert.Equal("medieval", Manners.AgeName(Manners.Medieval));
        Assert.Equal(new[] { Manners.Gothic, Manners.Rus, Manners.Byzantine, Manners.Saracen, Manners.Mongol }.Order(), Manners.In(Manners.Medieval).Order());
        Assert.Equal(Manners.Gothic, Manners.For(Manners.Medieval, Manners.Roman));
        Assert.Equal(Manners.Rus, Manners.For(Manners.Medieval, Manners.Tribal));
        Assert.Equal(Manners.Byzantine, Manners.For(Manners.Medieval, Manners.Persian));
        Assert.Equal(Manners.Saracen, Manners.For(Manners.Medieval, Manners.Egyptian));
        Assert.Equal(Manners.Mongol, Manners.For(Manners.Medieval, Manners.Chinese));
        Assert.Equal(Manners.Continental, Manners.For(Manners.Later, Manners.Gothic));
    }

    [Fact]
    public void Christian_churches_face_east_and_mosques_the_qibla()
    {
        foreach (int christian in new[] { Manners.Gothic, Manners.Byzantine, Manners.Rus })
        {
            var churches = Town(christian, radius: 8, walls: 0.5).Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "church").ToList();
            Assert.NotEmpty(churches);
            Assert.All(churches, church => Assert.True(Along(Axis(church), new Spot(1, 0)), Manners.Id(christian) + " church runs east and west"));
        }
        var mosques = Town(Manners.Saracen, radius: 8).Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "church").ToList();
        Assert.NotEmpty(mosques);
        Assert.All(mosques, mosque => Assert.True(Along(Axis(mosque), Spot.Toward(Settlements.Qibla)), "a mosque is turned to the qibla"));
    }

    [Fact]
    public void A_Gothic_town_has_its_market_cross_friary_and_spire()
    {
        var town = Town(Manners.Gothic, radius: 8);
        Assert.True(Count(town, PartKinds.Sign, "marketcross") >= 1, "a market cross");
        Assert.True(Count(town, PartKinds.Block, "cloister") >= 1, "a friary");
        Assert.True(Count(town, PartKinds.Block, "steeple") >= 1, "a spire");
        Assert.Equal(Defences.Castle, Defences.Fort(Manners.Gothic).Id);
    }

    [Fact]
    public void Saracen_and_Mongol_places_keep_no_pigs()
    {
        foreach (var place in new[] { Town(Manners.Saracen, houses: 0.4), Town(Manners.Mongol, houses: 0.4) })
            Assert.Equal(0, Count(place, PartKinds.Block, "pigsty") + Count(place, PartKinds.Sign, "pigs"));
    }

    [Fact]
    public void A_Mongol_camp_is_tents_round_the_great_tent_and_a_laager_rings_them_with_carts()
    {
        var camp = Town(Manners.Mongol);
        Assert.True(Count(camp, PartKinds.Block, "roundhouse") >= FewestTents, "felt tents");
        Assert.Equal(1, Count(camp, PartKinds.Block, "greathouse"));
        Assert.Equal(0, Count(camp, PartKinds.Block, "church") + Count(camp, PartKinds.Area, "garden") + Count(camp, PartKinds.Area, "orchard"));
        var laager = Defences.Lay(Site(Manners.Mongol), "s", Defences.Laager, Middle, 4, 4, 0, 0, 0);
        Assert.Equal(Defences.Laager, Defences.KindOf(laager));
        Assert.True(Count(laager, PartKinds.Sign, "cart") >= FewestCarts, "a ring of carts");
        Assert.True(Count(laager, PartKinds.Block, "roundhouse") >= FewestTents, "tents inside the carts");
    }

    [Fact]
    public void A_walled_Rus_town_has_its_kremlin_inside_a_timber_palisade()
    {
        var town = Town(Manners.Rus, radius: 7, walls: 0.5);
        Assert.True(Count(town, PartKinds.Line, "palisade") >= 1, "timber walls");
        Assert.Equal(0, Count(town, PartKinds.Line, "wall"));
        Assert.True(Count(town, PartKinds.Block, "keep") >= 1, "a kremlin");
        Assert.True(Count(town, PartKinds.Area, "drymoat") >= 1, "a dry ditch round the kremlin");
    }

    [Fact]
    public void Ravaged_burns_its_share_of_homes_keeps_the_churches_and_survives_laying_again()
    {
        Assert.Equal(0, Count(Town(Manners.Gothic), PartKinds.Block, Settlements.Ruin));
        var burned = Town(Manners.Gothic, ravaged: 1);
        Assert.Equal(0, Count(burned, PartKinds.Block, "house"));
        Assert.True(Count(burned, PartKinds.Block, Settlements.Ruin) >= FewestRuins, "homes burned");
        Assert.True(Count(burned, PartKinds.Block, "church") >= 1, "the church stands");
        Assert.Equal(1, burned.Setting(Settlements.Ravaged, 0));
        var again = BuiltThings.Remade(Site(Manners.Gothic), burned, burned.Seed);
        Assert.Equal(Count(burned, PartKinds.Block, Settlements.Ruin), Count(again, PartKinds.Block, Settlements.Ruin));
    }

    [Fact]
    public void A_walled_medieval_town_grows_suburbs_beyond_its_gates_and_holds_a_castle_on_its_wall()
    {
        foreach (int culture in new[] { Manners.Gothic, Manners.Saracen })
        {
            const double Radius = 8;
            var town = Town(culture, radius: Radius, walls: 0.5);
            Assert.True(Count(town, PartKinds.Block, "gatehouse") >= 1, Manners.Id(culture) + " has gates");
            Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block && p.Class == "house" && (BuiltSite.Round(p).At - Middle).Length > Radius) >= 1, Manners.Id(culture) + " grows outside its gates");
            Assert.True(Count(town, PartKinds.Block, "watchtower") + Count(town, PartKinds.Block, "keep") >= 1, Manners.Id(culture) + " holds a castle");
        }
    }

    [Fact]
    public void Each_medieval_culture_builds_the_works_of_its_own_town()
    {
        Assert.True(Count(Town(Manners.Gothic, radius: 8), PartKinds.Block, "markethall") >= 1, "a Gothic market hall");
        Assert.True(Count(Town(Manners.Saracen, radius: 8), PartKinds.Block, "markethall") >= 1, "a covered suq");
        var byzantine = Town(Manners.Byzantine, radius: 8);
        Assert.True(Count(byzantine, PartKinds.Sign, "column") >= 4, "a colonnaded street");
        Assert.True(Count(byzantine, PartKinds.Line, "wardwall") >= 1, "a walled monastery");
        var rus = Town(Manners.Rus, radius: 8);
        Assert.True(Count(rus, PartKinds.Block, "church") >= 3, "a Rus' town of many churches");
        Assert.True(Count(rus, PartKinds.Block, "shed") >= 10, "stores in the yards");
    }

    [Fact]
    public void The_medieval_age_has_gallows_and_town_walls_but_no_guns_bastions_or_chaussees()
    {
        var things = Catalogue.For(false, false, false, true).SelectMany(s => s.Entries).Select(e => e.Id).ToList();
        Assert.Contains("gallows", things);
        Assert.Contains("stocks", things);
        Assert.Contains("marketcross", things);
        Assert.DoesNotContain("gun", things);
        Assert.DoesNotContain("shot", things);
        Assert.DoesNotContain(Defences.Rings(Manners.Medieval), ring => ring.Key == Defences.Bastions);
        Assert.DoesNotContain("chaussee", Defences.Routes(Manners.Medieval));
        Assert.DoesNotContain("railway", Defences.Routes(Manners.Medieval));
        Assert.Equal(Defences.Laager, Defences.Fort(Manners.Mongol).Id);
        Assert.All(new[] { Manners.Byzantine, Manners.Saracen }, m => Assert.Equal(Defences.Fortress, Defences.Fort(m).Id));
    }
}
