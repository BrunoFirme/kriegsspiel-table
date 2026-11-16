using KTable.Core.Model;

namespace KTable.Core.Tests;

public class MedievalCultureTests
{
    const double Side = 60, MetresToTheUnit = 50, Square = 0.02;
    const int FewestTents = 20, FewestCarts = 20, FewestRuins = 20, Seeds = 10;
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
    public void A_Rus_town_at_a_flat_confluence_sets_its_kremlin_on_the_tongue_between_the_rivers()
    {
        var waters = new BuiltThing { Id = "w", Sheet = "s", Kind = BuiltKinds.Water };
        waters.Parts.Add(BuiltSite.Line("river", [new Spot(0, 34), new Spot(60, 34)]));
        waters.Parts.Add(BuiltSite.Line("stream", [new Spot(30, 0), new Spot(30, 34)]));
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [waters], Manner = Manners.Rus };
        int kremlins = 0;
        foreach (int seed in Enumerable.Range(1, Seeds))
        {
            var town = Settlements.Lay(site, "s", new Spot(30, 30), 7, seed, 0.75, 0, Plans.Value(Plans.Offered(Manners.Rus)[0]));
            foreach (var platform in town.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == Forts.Platform))
            {
                var ring = BuiltGeometry.Points(platform.At).Select(Spot.Of).ToList();
                var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
                kremlins++;
                Assert.True(middle.Y < 34 && Math.Abs(middle.X - 30) > 0.5 && (middle - new Spot(30, 34)).Length < 4, $"the kremlin at {middle} stands on the tongue between the rivers");
            }
        }
        Assert.True(kremlins > 0, "some towns keep a kremlin");
    }

    [Fact]
    public void A_walled_Rus_town_has_its_kremlin_inside_a_timber_palisade()
    {
        var town = Town(Manners.Rus, radius: 7, walls: 0.5);
        Assert.True(Count(town, PartKinds.Line, "palisade") >= 1, "timber walls");
        Assert.Equal(0, Count(town, PartKinds.Line, "wall"));
        Assert.True(Count(town, PartKinds.Area, Forts.Platform) >= 1 && Count(town, PartKinds.Block, "watchtower") >= 3, "a kremlin");
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
    public void A_walled_medieval_town_grows_suburbs_beyond_its_gates()
    {
        foreach (int culture in new[] { Manners.Gothic, Manners.Saracen })
        {
            const double Radius = 8;
            var town = Town(culture, radius: Radius, walls: 0.5);
            Assert.True(Count(town, PartKinds.Block, "gatehouse") >= 1, Manners.Id(culture) + " has gates");
            Assert.True(town.Parts.Count(p => p.Kind == PartKinds.Block && p.Class is "house" or "cottage" or "townhouse" or "villa" && (BuiltSite.Round(p).At - Middle).Length > Radius) >= 1,
                Manners.Id(culture) + " grows outside its gates");
        }
    }

    static bool Castled(BuiltThing t) => Count(t, PartKinds.Area, Forts.Platform) >= 1;

    const double GateReachM = 12, OnTheCurtainM = 1, NearTheCastleM = 200, PassageM = 32;

    [Fact]
    public void A_walled_japanese_town_lays_its_castle_on_every_plan()
    {
        foreach (string plan in Plans.Offered(Manners.Japanese))
            foreach (int seed in Enumerable.Range(1, 4))
                Assert.True(Castled(Settlements.Lay(Site(Manners.Japanese), "s", Middle, 7, seed, 0.75, 0.5, Plans.Value(plan))), $"{plan} seed {seed}");
    }
    static readonly int[] TownSeeds = [1, 2, 3, 4, 5, 10];

    [Fact]
    public void A_castle_on_the_town_wall_closes_the_circuit_with_the_wall_running_into_its_curtain()
    {
        int castled = 0;
        var towns = new[] { Manners.Gothic, Manners.Byzantine, Manners.Saracen, Manners.Rus }.SelectMany(manner => TownSeeds.Select(seed => (manner, Radius: 7.0, seed)))
            .Concat([(Manners.Rus, 11.0, 8), (Manners.Saracen, 11.0, 10), (Manners.Gothic, 11.0, 1)]);
        foreach (var (manner, radius, seed) in towns)
        {
            var town = Settlements.Lay(Site(manner), "s", Middle, radius, seed, 0.75, 0.5, Plans.Value(Plans.Offered(manner)[0]));
            if (!Castled(town)) continue;
            castled++;
            List<Spot> Of(BuiltPart p) => BuiltGeometry.Points(p.At).Select(Spot.Of).ToList();
            var walls = town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade").Select(Of).ToList();
            var platforms = town.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == Forts.Platform).Select(p => BuiltSite.Round(p).At).ToList();
            var closed = walls.Where(w => w.Count > 3 && (w[0] - w[^1]).Length < 1e-6).ToList();
            var curtains = closed.Where(r => platforms.Any(at => Enclosures.Inside(r, at))).ToList();
            var rings = curtains
                .Concat(town.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is Forts.Ditch or "drymoat").Select(Of).Select(r => r.Append(r[0]).ToList())).ToList();
            var gates = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(p => BuiltSite.Round(p).At).ToList();
            var ways = town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "road" or "lane" or "track").Select(Of).ToList();
            bool Gate(Spot end) => gates.Any(g => (g - end).Length * MetresToTheUnit < GateReachM)
                || ways.Any(w => Enumerable.Range(0, w.Count - 1).Any(i => end.FromSegment(w[i], w[i + 1]) * MetresToTheUnit < GateReachM));
            bool On(Spot end, List<Spot> r) => Enumerable.Range(0, r.Count - 1).Any(i => end.FromSegment(r[i], r[i + 1]) * MetresToTheUnit < OnTheCurtainM);
            bool Flank(List<Spot> h, Spot end)
            {
                var mid = new Spot(h.Take(4).Average(q => q.X), h.Take(4).Average(q => q.Y));
                bool shortFirst = (h[1] - h[0]).Length < (h[2] - h[1]).Length;
                var through = (shortFirst ? h[1] - h[0] : h[2] - h[1]).Unit;
                double side = (shortFirst ? h[2] - h[1] : h[1] - h[0]).Length / 2, off = end - mid == default ? 0 : Math.Abs((end - mid).X * through.Y - (end - mid).Y * through.X);
                return off * MetresToTheUnit < side * MetresToTheUnit + OnTheCurtainM && Math.Abs((end - mid).Dot(through)) * MetresToTheUnit < PassageM;
            }
            var houses = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(Of).Select(h => h.Append(h[0]).ToList()).ToList();
            bool Closed(Spot end, List<Spot> own) => Gate(end) || rings.Any(r => On(end, r)) || walls.Any(w => !ReferenceEquals(w, own) && On(end, w))
                || houses.Any(h => Enclosures.Inside(h, end) || On(end, h) || Flank(h, end));
            foreach (var run in walls.Except(closed))
            {
                foreach (var end in new[] { run[0], run[^1] }.Where(end => platforms.Any(at => (at - end).Length * MetresToTheUnit < NearTheCastleM)))
                    Assert.True(Closed(end, run), $"{Manners.Named(manner).Name} seed {seed} radius {radius}: a wall ends open at {end}");
                if (run.Count > 2)
                    Assert.False(run.Zip(run.Skip(1), (a, b) => (a + b) * 0.5).Any(mid => curtains.Any(r => Enclosures.Inside(r, mid))),
                        $"{Manners.Named(manner).Name} seed {seed} radius {radius}: the town wall runs inside the castle");
            }
        }
        Assert.True(castled >= 10, $"{castled} castled towns");
    }

    [Fact]
    public void Castles_come_as_often_as_they_did_more_in_cities_none_in_villages_and_rarely_lack_in_Rus()
    {
        double Share(int manner, double radius, double walls) => Enumerable.Range(1, Seeds).Count(seed =>
            Castled(Settlements.Lay(Site(manner), "s", Middle, radius, seed, 0.75, walls, Plans.Value(Plans.Offered(manner)[0])))) / (double)Seeds;
        Assert.InRange(Share(Manners.Gothic, 7, 0), 0.25, 0.85);
        Assert.InRange(Share(Manners.Gothic, 7, 0.5), 0.25, 0.85);
        Assert.True(Share(Manners.Gothic, 11, 0.5) >= 0.6, "most cities hold a castle");
        Assert.Equal(0, Share(Manners.Gothic, 2.5, 0));
        Assert.True(Share(Manners.Rus, 7, 0) >= 0.8, "a Rus' town grows round its kremlin");
        Assert.Equal(0, Share(Manners.Mongol, 7, 0));
    }

    [Fact]
    public void A_town_has_rich_quarters_by_its_market_and_poor_ones_at_its_edge()
    {
        var town = Town(Manners.Gothic, radius: 8, houses: 0.85);
        double Mean(string cls) => town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == cls).Select(p => (BuiltSite.Round(p).At - Middle).Length).DefaultIfEmpty(0).Average();
        Assert.True(Count(town, PartKinds.Block, "townhouse") >= 5 && Count(town, PartKinds.Block, "cottage") >= 5, "both rich and poor homes");
        Assert.True(Mean("townhouse") < Mean("cottage"), $"the rich live nearer the middle: {Mean("townhouse"):0.0} against {Mean("cottage"):0.0}");
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
