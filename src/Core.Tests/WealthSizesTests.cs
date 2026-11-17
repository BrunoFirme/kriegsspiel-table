using KTable.Core.Model;

namespace KTable.Core.Tests;

public class WealthSizesTests
{
    const double Side = 100, PerMetre = 1, LongM = 10, DeepM = 8, PoorScale = 0.8, RichScale = 1.25, Close = 0.01, Gap = 0.3;
    const double Rounding = 0.002, TownSide = 60, MetresToTheUnit = 25, VillageReach = 6, VillageHouses = 0.3;
    static readonly Spot At = new(50, 50), Middle = new(30, 30);

    static double Scale(double wealth) => PoorScale + (RichScale - PoorScale) * wealth;

    static readonly double RichBy = Scale((Wealth.RichFrom + 1) / 2) / Scale((Wealth.RichFrom + Wealth.PoorBelow) / 2);
    static readonly double PoorBy = Scale(Wealth.PoorBelow / 2) / Scale((Wealth.RichFrom + Wealth.PoorBelow) / 2);

    static BuiltSite Site(params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = PerMetre, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1, Others = others };

    static BuiltPart House(Spot at, string cls = "house") => BuiltSite.Block(cls, at, new Spot(1, 0), LongM, DeepM);

    static BuiltThing Lot(params BuiltPart[] more) =>
        new() { Id = "t", Kind = BuiltKinds.Settlement, Settings = { [BuiltThings.Scale] = PerMetre }, Parts = [House(At), .. more] };

    static List<Spot> Ring(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static BuiltThing Rich(BuiltThing thing, BuiltSite? site = null) => ZoneOverrides.WithWealth(thing, [0], WealthLevel.Rich, site ?? Site());

    static double Grown(BuiltThing before, BuiltThing after, int part = 0) => BuiltThings.Sides(after.Parts[part]).Long / BuiltThings.Sides(before.Parts[part]).Long;

    static bool Cross(Spot a, Spot b, Spot c, Spot d)
    {
        static double Turn(Spot o, Spot p, Spot q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        return Turn(a, b, c) * Turn(a, b, d) < 0 && Turn(c, d, a) * Turn(c, d, b) < 0;
    }

    static bool Overlap(List<Spot> a, List<Spot> b) => a.Any(p => Enclosures.Inside(b, p)) || b.Any(p => Enclosures.Inside(a, p))
        || a.Select((p, i) => (p, q: a[(i + 1) % a.Count])).Any(e => b.Select((p, i) => (p, q: b[(i + 1) % b.Count])).Any(f => Cross(e.p, e.q, f.p, f.q)));

    static List<Spot> Shrunk(List<Spot> ring, double by)
    {
        var middle = new Spot(ring.Average(p => p.X), ring.Average(p => p.Y));
        return ring.Select(p => p + (middle - p).Unit * by).ToList();
    }

    static double FromLine(Spot p, Spot a, Spot b)
    {
        var along = (b - a).Unit;
        var off = p - a;
        return Math.Abs(off.X * along.Y - off.Y * along.X);
    }

    static string Shape(BuiltThing thing) => string.Join("\n", thing.Parts.Select(p =>
    {
        var copy = p.Clone();
        copy.Kept = false;
        return Json.Serialize(copy);
    }));

    static BuiltSite TownSite(int manner) =>
        new() { Width = TownSide, Height = TownSide, UnitsPerMetre = 1 / MetresToTheUnit, Grid = ReliefGrid.Flat(TownSide, TownSide), HeightUnit = 1, Manner = manner };

    static BuiltThing Village(int manner, int seed)
    {
        var town = Settlements.Lay(TownSite(manner), "s", Middle, VillageReach, seed, VillageHouses, 0, 0);
        town.Id = "v";
        return town;
    }

    static List<int> Homes(BuiltThing town) => Enumerable.Range(0, town.Parts.Count).Where(i => WealthSizes.Sized(town.Parts[i]) && town.Parts[i].Class == "house").ToList();

    [Fact]
    public void A_rich_house_grows_and_a_poor_one_shrinks_by_the_generator_s_wealth_scale_in_every_culture()
    {
        Assert.Equal(1.154, RichBy, 3);
        Assert.Equal(0.850, PoorBy, 3);
        foreach (var culture in Manners.All)
        {
            var lot = Lot();
            lot.Settings[Manners.Setting] = culture.Number;
            Assert.Equal(RichBy, WealthSizes.Factor(WealthLevel.Middling, WealthLevel.Rich), 9);
            Assert.Equal(PoorBy, WealthSizes.Factor(WealthLevel.Middling, WealthLevel.Poor), 9);
            Assert.Equal(RichBy, Grown(lot, Rich(lot)), 3);
            Assert.Equal(PoorBy, Grown(lot, ZoneOverrides.WithWealth(lot, [0], WealthLevel.Poor, Site())), 3);
        }
        var cottage = new BuiltThing { Id = "c", Settings = { [BuiltThings.Scale] = PerMetre }, Parts = [House(At, "cottage")] };
        Assert.Equal(1 / PoorBy, Grown(cottage, ZoneOverrides.WithWealth(cottage, [0], WealthLevel.Middling, Site())), 3);
        Assert.Equal(RichBy / PoorBy, Grown(cottage, ZoneOverrides.WithWealth(cottage, [0], WealthLevel.Rich, Site())), 3);
    }

    [Fact]
    public void A_building_that_is_not_a_dwelling_keeps_its_size()
    {
        foreach (string cls in new[] { "church", "barn", "roundhouse" })
        {
            var thing = new BuiltThing { Id = "b", Settings = { [BuiltThings.Scale] = PerMetre }, Parts = [House(At, cls)] };
            var rich = Rich(thing);
            Assert.Equal(thing.Parts[0].At, rich.Parts[0].At);
            Assert.Null(rich.Parts[0].LaidAt);
        }
    }

    [Fact]
    public void A_house_grows_and_shrinks_about_its_street_front()
    {
        var lot = Lot();
        foreach (var level in new[] { WealthLevel.Rich, WealthLevel.Poor })
        {
            var set = ZoneOverrides.WithWealth(lot, [0], level, Site());
            List<Spot> was = Ring(lot.Parts[0]), now = Ring(set.Parts[0]);
            Assert.True(FromLine(now[0], was[0], was[1]) < Close && FromLine(now[1], was[0], was[1]) < Close);
            Assert.True(((now[0] + now[1]) * 0.5 - (was[0] + was[1]) * 0.5).Length < Close);
            Assert.True((now[3] - now[0]).Dot(was[3] - was[0]) > 0);
        }
    }

    [Fact]
    public void A_rich_house_grows_only_as_far_as_its_neighbour_lets_it()
    {
        var beside = House(At + new Spot(LongM + Gap, 0));
        var lot = Lot(beside);
        var rich = Rich(lot);
        double by = Grown(lot, rich);
        Assert.InRange(by, 1 + Close, RichBy - Close);
        Assert.False(Overlap(Shrunk(Ring(rich.Parts[0]), Close), Ring(beside)));
        Assert.Equal(lot.Parts[1].At, rich.Parts[1].At);
        var row = Lot(House(At + new Spot(LongM, 0)));
        Assert.Equal(row.Parts[0].At, Rich(row).Parts[0].At);
    }

    [Fact]
    public void A_rich_house_stops_short_of_a_street_and_of_water_behind_it()
    {
        double halfStreet = Grades.HalfM("street"), back = At.Y - DeepM / 2 - 1;
        foreach (var (cls, half) in new[] { ("street", halfStreet), ("river", 7.5) })
        {
            var line = BuiltSite.Line(cls, [new Spot(At.X - 40, back - half), new Spot(At.X + 40, back - half)]);
            var mine = Rich(Lot(line));
            var theirs = Rich(Lot(), Site(new BuiltThing { Id = "w", Kind = BuiltKinds.Water, Parts = [line] }));
            foreach (var set in new[] { mine, theirs })
            {
                double by = Grown(Lot(), set);
                Assert.InRange(by, 1 + Close, RichBy - Close);
                Assert.All(Ring(set.Parts[0]), p => Assert.True(p.Y >= back - Close));
            }
        }
    }

    [Fact]
    public void A_rich_house_stays_on_the_map()
    {
        var lot = Lot();
        var edge = new BuiltSite { Width = At.X + LongM / 2 + Close, Height = Side, UnitsPerMetre = PerMetre, Grid = ReliefGrid.Flat(Side, Side), HeightUnit = 1 };
        Assert.InRange(Grown(lot, Rich(lot, edge)), 1, 1 + 2 * Close / LongM + Close);
    }

    [Fact]
    public void The_house_s_own_shed_and_yard_stay_outside_its_walls()
    {
        var shed = BuiltSite.Block("shed", At - new Spot(0, DeepM / 2 + 1 + 1.5), new Spot(1, 0), 4, 3);
        var hens = BuiltSite.Sign("hens", At + new Spot(LongM / 2 + 0.4, 0));
        var lot = Lot(shed, hens);
        Groups.Tie(lot.Parts, 0);
        var rich = Rich(lot);
        Assert.InRange(Grown(lot, rich), 1 + Close, RichBy - Close);
        Assert.False(Overlap(Ring(rich.Parts[0]), Ring(shed)));
        Assert.False(Enclosures.Inside(Ring(rich.Parts[0]), Ring(hens)[0]));
        Assert.Equal(Shape(lot).Split('\n')[1..], Shape(rich).Split('\n')[1..]);
    }

    [Fact]
    public void Rich_then_middling_gives_back_the_house_exactly_as_it_was_laid()
    {
        var lot = Lot();
        var rich = Rich(lot);
        Assert.Equal(lot.Parts[0].At, rich.Parts[0].LaidAt);
        var poor = ZoneOverrides.WithWealth(rich, [0], WealthLevel.Poor, Site());
        Assert.Equal(lot.Parts[0].At, poor.Parts[0].LaidAt);
        var back = ZoneOverrides.WithWealth(poor, [0], WealthLevel.Middling, Site());
        Assert.Equal(Shape(lot), Shape(back));
        var moved = BuiltThings.WithPartMoved(rich, 0, 500, -300);
        var home = ZoneOverrides.WithWealth(moved, [0], WealthLevel.Middling, Site());
        Assert.Equal(BuiltThings.WithPartMoved(lot, 0, 500, -300).Parts[0].At, home.Parts[0].At);
        var stretched = BuiltThings.WithPartStretched(rich, 0, LongM * 2, DeepM);
        var kept = ZoneOverrides.WithWealth(stretched, [0], WealthLevel.Middling, Site());
        Assert.Equal(stretched.Parts[0].At, kept.Parts[0].At);
        Assert.Null(kept.Parts[0].LaidAt);
    }

    [Fact]
    public void A_whole_village_made_rich_and_back_is_the_village_it_was()
    {
        foreach (int manner in new[] { Manners.Gothic, Manners.Continental, Manners.Roman, Manners.Chinese })
        {
            var town = Village(manner, 3);
            var site = TownSite(manner);
            var homes = Homes(town);
            Assert.NotEmpty(homes);
            var rich = ZoneOverrides.WithWealth(town, homes, WealthLevel.Rich, site);
            Assert.Contains(homes, i => rich.Parts[i].LaidAt != null);
            foreach (int i in homes)
            {
                List<Spot> was = Ring(town.Parts[i]), now = Ring(rich.Parts[i]);
                Assert.True(FromLine(now[0], was[0], was[1]) < Rounding && FromLine(now[1], was[0], was[1]) < Rounding);
                var own = Groups.Of(rich, i).Where(k => k != i && rich.Parts[k].Kind != PartKinds.Line).Select(k => rich.Parts[k]);
                Assert.DoesNotContain(own, p => p.Kind == PartKinds.Sign ? Enclosures.Inside(now, Ring(p)[0]) && !Enclosures.Inside(was, Ring(p)[0])
                    : !Overlap(was, Ring(p)) && Overlap(Shrunk(now, Rounding), Ring(p)));
            }
            Assert.Equal(Shape(town), Shape(ZoneOverrides.WithWealth(rich, homes, WealthLevel.Middling, site)));
        }
    }

    [Fact]
    public void The_wealth_brush_resizes_every_house_it_steps_and_steps_them_back()
    {
        var lot = Lot();
        var richer = ZoneOverrides.Brushed(lot, [At], 1, true, Site())!;
        Assert.Equal(RichBy, Grown(lot, richer), 3);
        var poorer = ZoneOverrides.Brushed(richer, [At], 1, false, Site())!;
        Assert.Equal(Shape(lot), Shape(poorer));
        Assert.Equal(PoorBy, Grown(lot, ZoneOverrides.Brushed(poorer, [At], 1, false, Site())!), 3);
    }

    static bool Fenced(BuiltPart area, BuiltPart fence) =>
        area.Kind == PartKinds.Area && area.At.Count == 8 && area.At.Take(2).SequenceEqual(fence.At.Take(2)) && area.At.Skip(6).SequenceEqual(fence.At.Skip(2));

    [Fact]
    public void A_rich_lot_trades_its_fence_for_the_garden_wall_the_generator_gives_and_back()
    {
        int found = 0;
        foreach (int seed in Enumerable.Range(1, 12))
        {
            var town = Village(Manners.Gothic, seed);
            foreach (int i in Homes(town))
            {
                var group = Groups.Of(town, i);
                if (group.Count < 2) continue;
                int fence = group.FirstOrDefault(k => town.Parts[k] is { Kind: PartKinds.Line, Class: "fence", At.Count: 4 } line
                    && group.Any(a => Fenced(town.Parts[a], line)), -1);
                if (fence < 0) continue;
                var croft = group.Select(a => town.Parts[a]).First(a => Fenced(a, town.Parts[fence]));
                var rich = ZoneOverrides.WithWealth(town, [i], WealthLevel.Rich, TownSite(Manners.Gothic));
                var c = BuiltGeometry.Points(croft.At);
                Assert.Equal(Settlements.GardenWall, rich.Parts[fence].Class);
                Assert.Equal(BuiltGeometry.Flat([c[0], c[3], c[2], c[1]]), rich.Parts[fence].At);
                Assert.Equal(Shape(town), Shape(ZoneOverrides.WithWealth(rich, [i], WealthLevel.Middling, TownSite(Manners.Gothic))));
                Assert.Equal("fence", ZoneOverrides.WithWealth(rich, [i], WealthLevel.Poor, TownSite(Manners.Gothic)).Parts[fence].Class);
                found++;
            }
        }
        Assert.True(found > 0);
        var bare = Lot();
        Assert.Equal(bare.Parts.Count, Rich(bare).Parts.Count);
    }
}
