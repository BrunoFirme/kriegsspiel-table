using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TownTidyTests
{
    const double Side = 60, MetresToTheUnit = 50, Radius = 7, DeepestM = 2.5, InsideM = 0.6;
    static readonly Spot Middle = new(30, 30);
    static readonly string[] Homes = ["house", "villa", "shop", "cottage", "cabin", "townhouse", "insula", "domus", "taberna", "longhouse", "roundhouse", "granary", "flats"];
    static readonly string[] Courts = ["villa", "insula", "domus", "cloister"];

    static BuiltSite Site(int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = [], Manner = manner };

    static BuiltThing Town(int manner, int seed, double houses = 0.8, double walls = 0) =>
        Settlements.Lay(Site(manner), "s", Middle, Radius, seed, houses, walls, Plans.Value(Plans.Offered(manner)[0]));

    static List<Spot> Ring(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static double Depth(List<Spot> ring, Spot p)
    {
        if (!Enclosures.Inside(ring, p)) return 0;
        return Enumerable.Range(0, ring.Count).Min(i => p.FromSegment(ring[i], ring[(i + 1) % ring.Count])) * MetresToTheUnit;
    }

    static IEnumerable<Spot> Samples(List<Spot> ring)
    {
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        return ring.Concat(Enumerable.Range(0, ring.Count).Select(i => (ring[i] + ring[(i + 1) % ring.Count]) * 0.5)).Append(middle);
    }

    static double Sunk(List<Spot> a, List<Spot> b) => Math.Max(Samples(a).Max(p => Depth(b, p)), Samples(b).Max(p => Depth(a, p)));

    public static IEnumerable<object[]> Cultures() => Manners.All.Select(m => new object[] { m.Number });

    [Theory]
    [MemberData(nameof(Cultures))]
    public void No_home_stands_sunk_into_another(int manner)
    {
        foreach (int seed in new[] { 1, 2 })
        {
            var homes = Town(manner, seed).Parts.Where(p => p.Kind == PartKinds.Block && Homes.Contains(p.Class)).Select(p => (Part: p, Ring: Ring(p))).ToList();
            for (int i = 0; i < homes.Count; i++)
                for (int j = i + 1; j < homes.Count; j++)
                {
                    if ((homes[i].Ring[0] - homes[j].Ring[0]).Length * MetresToTheUnit > 60) continue;
                    double sunk = Sunk(homes[i].Ring, homes[j].Ring);
                    Assert.True(sunk <= DeepestM, $"{Manners.Id(manner)} seed {seed}: a {homes[i].Part.Class} stands {sunk:0.0} m into a {homes[j].Part.Class} at {homes[i].Ring[0]}");
                }
        }
    }

    [Theory]
    [MemberData(nameof(Cultures))]
    public void No_yard_thing_stands_inside_a_building(int manner)
    {
        var town = Town(manner, 3);
        var blocks = town.Parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 6).Select(p => (Part: p, Ring: Ring(p))).ToList();
        foreach (var sign in town.Parts.Where(p => p.Kind == PartKinds.Sign && p.Class is "woodpile" or "oven" or "dungheap" or "washing" or "barrels" or "hens"))
        {
            var at = Ring(sign)[0];
            Assert.DoesNotContain(blocks, b => !Courts.Contains(b.Part.Class) && Depth(b.Ring, at) > InsideM);
        }
    }

    [Fact]
    public void A_ward_wall_stops_at_the_houses_it_meets()
    {
        var town = Town(Manners.Chinese, 1);
        var blocks = town.Parts.Where(p => p.Kind == PartKinds.Block && p.At.Count >= 6).Select(Ring).ToList();
        var walls = town.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == "wardwall").Select(Ring).ToList();
        Assert.NotEmpty(walls);
        foreach (var wall in walls)
            for (int i = 0; i + 1 < wall.Count; i++)
                foreach (int k in Enumerable.Range(0, 9))
                    Assert.DoesNotContain(blocks, b => Depth(b, wall[i] + (wall[i + 1] - wall[i]) * (k / 8.0)) > 1.5);
    }

    [Theory]
    [InlineData(Manners.NorthAfrican)]
    [InlineData(Manners.Ottoman)]
    [InlineData(Manners.Saracen)]
    [InlineData(Manners.Egyptian)]
    public void A_town_that_keeps_no_pigs_has_no_sty(int manner)
    {
        foreach (int seed in new[] { 1, 2, 3 })
            Assert.DoesNotContain(Town(manner, seed, 0.4).Parts, p => p.Class is "pigsty" or "pigs");
    }

    [Fact]
    public void A_temple_is_one_building_with_its_obelisks_and_its_avenue_of_sphinxes()
    {
        var town = Town(Manners.Egyptian, 1);
        int temple = town.Parts.FindIndex(p => p.Kind == PartKinds.Block && p.Class == "church");
        var whole = Groups.Of(town, temple);
        Assert.True(town.Parts[temple].Group > 0);
        var sphinxes = Enumerable.Range(0, town.Parts.Count).Where(i => town.Parts[i].Class == Hallmarks.Sphinx).ToList();
        Assert.True(sphinxes.Count >= 4);
        Assert.All(sphinxes, i => Assert.Contains(i, whole));
        Assert.Contains(whole, i => town.Parts[i].Class == "obelisk");
        Assert.Contains(whole, i => town.Parts[i].Class == "churchyard");
        Assert.All(whole, i => Assert.True((BuiltSite.Round(town.Parts[i]).At - BuiltSite.Round(town.Parts[temple]).At).Length * MetresToTheUnit < 200));
    }

    [Fact]
    public void A_church_is_one_building_with_its_steeple_and_its_yard()
    {
        var town = Town(Manners.Continental, 2);
        int church = town.Parts.FindIndex(p => p.Kind == PartKinds.Block && p.Class == "church");
        var whole = Groups.Of(town, church).Select(i => town.Parts[i].Class).ToList();
        Assert.Contains("steeple", whole);
        Assert.Contains("churchyard", whole);
        Assert.DoesNotContain("house", whole);
    }

    [Fact]
    public void A_house_is_one_building_with_what_stands_in_its_yard()
    {
        var town = Town(Manners.Continental, 2, 0.4);
        var grouped = town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "house" && p.Group > 0).ToList();
        Assert.NotEmpty(grouped);
        foreach (var house in grouped.Take(20))
        {
            var with = town.Parts.Where(p => p.Group == house.Group).ToList();
            Assert.Single(with, p => p.Kind == PartKinds.Block && p.Class == "house");
            Assert.All(with, p => Assert.True((BuiltSite.Round(p).At - BuiltSite.Round(house).At).Length * MetresToTheUnit < 150, p.Class));
        }
    }

    [Fact]
    public void A_building_moves_and_turns_as_one_and_stays_one_when_the_place_is_laid_again()
    {
        var town = Town(Manners.Egyptian, 1);
        int temple = town.Parts.FindIndex(p => p.Kind == PartKinds.Block && p.Class == "church");
        var whole = Groups.Of(town, temple);
        var moved = BuiltThings.WithPartsMoved(town, whole, 4000, -2000);
        Assert.All(whole, i =>
        {
            Assert.True(moved.Parts[i].Kept);
            Assert.Equal(town.Parts[i].At[0] + 4000, moved.Parts[i].At[0]);
            Assert.Equal(town.Parts[i].At[1] - 2000, moved.Parts[i].At[1]);
        });
        Assert.All(Enumerable.Range(0, town.Parts.Count).Except(whole), i => Assert.Equal(town.Parts[i].At, moved.Parts[i].At));
        var turned = BuiltThings.WithPartsTurned(town, whole, Math.PI / 2);
        double Apart(BuiltThing t, int a, int b) => (BuiltSite.Round(t.Parts[a]).At - BuiltSite.Round(t.Parts[b]).At).Length;
        Assert.All(whole, i => Assert.Equal(Apart(town, temple, i), Apart(turned, temple, i), 2));
        var again = BuiltThings.Remade(Site(Manners.Egyptian), moved, 99);
        var kept = again.Parts.Where(p => p.Kept).ToList();
        Assert.Equal(whole.Count, kept.Count);
        Assert.Single(kept.Select(p => p.Group).Distinct());
        Assert.DoesNotContain(again.Parts, p => !p.Kept && p.Group == kept[0].Group);
        var less = BuiltThings.WithoutParts(town, whole);
        Assert.Equal(town.Parts.Count - whole.Count, less.Parts.Count);
        Assert.DoesNotContain(less.Parts, p => p.Class == Hallmarks.Sphinx);
    }

    [Fact]
    public void A_look_set_on_a_house_by_hand_is_kept_when_the_place_is_laid_again()
    {
        var town = Town(Manners.Continental, 2);
        int house = town.Parts.FindIndex(p => p.Kind == PartKinds.Block && p.Class == "house");
        var set = Looks.With(town, house, 4, "mansard", "slate");
        Assert.Equal((4, "mansard", "slate", true), (set.Parts[house].Storeys, set.Parts[house].Roof, set.Parts[house].Cover, set.Parts[house].Kept));
        Assert.Equal(0, town.Parts[house].Storeys);
        var again = BuiltThings.Remade(Site(Manners.Continental), set, 77);
        Assert.Single(again.Parts, p => p.Kept && p.Storeys == 4 && p.Roof == "mansard" && p.Cover == "slate" && p.At.SequenceEqual(set.Parts[house].At));
        var odd = Looks.With(town, house, 99, "igloo", null);
        Assert.Equal((Looks.MostStoreys, (string?)null), (odd.Parts[house].Storeys, odd.Parts[house].Roof));
        Assert.False(Looks.Takes(town.Parts.First(p => p.Kind == PartKinds.Sign)));
    }

    static BuiltThing Road(BuiltSite site, string cls, string? surface, params Spot[] through) => BuiltRoads.Lay(site, "s", through, cls, false, surface);

    static BuiltSite Beside(int manner, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = Side, Others = others, Manner = manner };

    [Fact]
    public void A_road_drawn_from_a_dead_end_carries_that_road_on_as_one()
    {
        var first = Road(Beside(Manners.Continental), "road", Looks.Gravel, new Spot(10, 30), new Spot(20, 30));
        first.Id = "w-1";
        var longer = BuiltRoads.Extended(Beside(Manners.Continental), first, [new Spot(20.6, 30.4), new Spot(30, 34)], "road", false, null);
        Assert.NotNull(longer);
        Assert.Equal("w-1", longer!.Id);
        var line = Assert.Single(longer.Parts, p => p.Kind == PartKinds.Line && p.Class == "road");
        var run = Ring(line);
        Assert.True((run[0] - new Spot(10, 30)).Length < 0.01 && (run[^1] - new Spot(30, 34)).Length < 0.01, "it runs from the old road's start to the new road's end");
        Assert.Equal(Looks.Gravel, line.Surface);
        Assert.Equal(3 * 2, longer.Outline.Count);
        var back = BuiltRoads.Extended(Beside(Manners.Continental), first, [new Spot(2, 26), new Spot(9.5, 30.3)], "road", false, Looks.Cobbles);
        Assert.True((Ring(back!.Parts.Single(p => p.Kind == PartKinds.Line))[0] - new Spot(2, 26)).Length < 0.01, "a road ending at the old road's start carries it on that way");
        Assert.Equal(Looks.Cobbles, back.Parts.Single(p => p.Kind == PartKinds.Line).Surface);
        Assert.Null(BuiltRoads.Extended(Beside(Manners.Continental), first, [new Spot(15, 20), new Spot(15, 29.8)], "road", false, null));
        Assert.Null(BuiltRoads.Extended(Beside(Manners.Continental), first, [new Spot(20.6, 30.4), new Spot(30, 34)], "track", false, null));
        var crossing = Road(Beside(Manners.Continental), "road", null, new Spot(20, 20), new Spot(20, 40));
        Assert.Null(BuiltRoads.Extended(Beside(Manners.Continental, crossing), first, [new Spot(20.6, 30.4), new Spot(30, 34)], "road", false, null));
        var track = Road(Beside(Manners.Continental, first), "track", null, new Spot(20.8, 30.6), new Spot(26, 40));
        Assert.True((Ring(track.Parts.Single(p => p.Kind == PartKinds.Line))[0] - new Spot(20, 30)).Length < 0.01, "a way of another kind starts on the dead end it was drawn from");
    }

    [Fact]
    public void A_road_keeps_the_surface_it_was_given_and_a_town_s_streets_are_not_all_alike()
    {
        var road = Road(Beside(Manners.Continental), "road", Looks.Cobbles, new Spot(10, 30), new Spot(20, 30));
        Assert.Equal(Looks.Cobbles, road.Parts.Single(p => p.Kind == PartKinds.Line).Surface);
        Assert.Equal(Looks.Cobbles, BuiltThings.Remade(Beside(Manners.Continental), road, 3).Parts.Single(p => p.Kind == PartKinds.Line).Surface);
        Assert.Equal(Looks.Mud, Looks.WithSurface(road, 0, Looks.Mud).Parts[0].Surface);
        Assert.Null(Looks.WithSurface(road, 0, "lava").Parts[0].Surface);
        Assert.Null(Road(Beside(Manners.Continental), "road", "lava", new Spot(10, 30), new Spot(20, 30)).Parts.Single(p => p.Kind == PartKinds.Line).Surface);
        foreach (int manner in new[] { Manners.Gothic, Manners.Continental, Manners.Roman, Manners.Rus, Manners.Saracen })
        {
            var ways = Town(manner, 2).Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "street" or "lane").ToList();
            Assert.True(ways.Select(p => p.Class + ":" + p.Surface).Distinct().Count() >= 3, $"{Manners.Id(manner)} streets: {string.Join(", ", ways.Select(p => p.Class + ":" + p.Surface).Distinct())}");
        }
    }

    static BuiltSite Hillside(double grade)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++) grid.H[y * grid.Cols + x] = (float)(y / (double)(grid.Rows - 1) * Side * grade);
        return new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Others = [], Manner = Manners.Gothic };
    }

    [Fact]
    public void A_castle_on_a_hillside_has_a_dry_ditch_and_one_on_the_level_may_have_a_moat()
    {
        var steep = Enumerable.Range(1, 12).Select(seed => Defences.Lay(Hillside(0.12), "s", Defences.Castle, Middle, 3, seed, 0, 0, 0)).Where(c => c.Parts.Count > 0).ToList();
        Assert.NotEmpty(steep);
        Assert.All(steep, castle => Assert.DoesNotContain(castle.Parts, p => p.Class == Forts.Ditch));
        var level = Enumerable.Range(1, 12).Select(seed => Defences.Lay(Hillside(0), "s", Defences.Castle, Middle, 3, seed, 0, 0, 0)).ToList();
        Assert.Contains(level, castle => castle.Parts.Any(p => p.Class == Forts.Ditch));
    }

    [Fact]
    public void A_castle_s_gatehouse_stands_in_its_curtain_with_the_wall_meeting_its_flanks()
    {
        int gates = 0;
        foreach (int seed in Enumerable.Range(1, 12))
        {
            var castle = Defences.Lay(Hillside(0), "s", Defences.Castle, Middle, 3, seed, 0, 0, 0);
            var ends = castle.Parts.Where(p => p.Kind == PartKinds.Line && p.Class is "wall" or "palisade").Select(Ring).Where(w => (w[0] - w[^1]).Length > 1e-9).SelectMany(w => new[] { w[0], w[^1] }).ToList();
            foreach (var house in castle.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "gatehouse").Select(Ring))
            {
                Spot middle = (house[0] + house[2]) * 0.5, through = (house[3] - house[0]).Unit;
                double deep = (house[3] - house[0]).Length;
                var beside = ends.Where(e => (e - middle).Length * MetresToTheUnit < 12).ToList();
                if (beside.Count == 0) continue;
                gates++;
                Assert.All(beside, end => Assert.True(Math.Abs((end - middle).Dot(through)) <= deep / 2 + 1 / MetresToTheUnit,
                    $"seed {seed}: the curtain ends {Math.Abs((end - middle).Dot(through)) * MetresToTheUnit:0.0} m before or behind the gatehouse's middle, outside its {deep * MetresToTheUnit:0.0} m depth"));
            }
        }
        Assert.True(gates >= 4, $"{gates} gatehouses met their curtain");
    }

    [Fact]
    public void A_steading_keeps_its_houses_clear_of_each_other()
    {
        foreach (int seed in Enumerable.Range(1, 6))
        {
            var homes = Town(Manners.Tribal, seed, 0.6).Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "longhouse" or "roundhouse" or "granary").Select(Ring).ToList();
            for (int i = 0; i < homes.Count; i++)
                for (int j = i + 1; j < homes.Count; j++)
                    Assert.True(Sunk(homes[i], homes[j]) <= 1, $"seed {seed}: two steading houses overlap at {homes[i][0]}");
        }
    }
}
