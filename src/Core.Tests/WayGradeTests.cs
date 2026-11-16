using KTable.Core.Model;

namespace KTable.Core.Tests;

public class WayGradeTests
{
    const double Side = 60, MetresToTheUnit = 5, PerMetre = 1 / MetresToTheUnit, Slack = 0.02;
    static readonly Spot Middle = new(30, 30);

    static ReliefGrid Land(Func<double, double, double> metres)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)(metres(x / (double)(grid.Cols - 1) * Side, y / (double)(grid.Rows - 1) * Side) * PerMetre);
            }
        return grid;
    }

    static ReliefGrid Hillside(double grade) => Land((_, y) => y * MetresToTheUnit * grade);

    static BuiltSite Site(ReliefGrid grid, int manner = Manners.Continental) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = PerMetre, Grid = grid, HeightUnit = 1, Manner = manner };

    static BuiltThing Village(BuiltSite site, int seed = 2, string plan = Plans.Grown, double houses = 0.6) =>
        Settlements.Lay(site, "s", Middle, 160 * PerMetre, seed, houses, 0, Plans.Value(plan));

    static IReadOnlyList<(BuiltPart Part, Profile Profile)> Graded(ReliefGrid grid, params BuiltThing[] things) => WayGrading.Profiles(grid, things, Side, Side, PerMetre, 1);

    static IEnumerable<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of);

    static bool Covered(BuiltThing thing, Spot at, params string[] classes) =>
        thing.Parts.Where(p => p.Kind == PartKinds.Line && classes.Contains(p.Class)).Any(p => Points(p).Zip(Points(p).Skip(1)).Any(leg => at.FromSegment(leg.First, leg.Second) < 1.5 * PerMetre));

    [Fact]
    public void On_a_steep_hillside_a_village_s_ways_are_cut_or_filled_at_most_a_metre_and_a_half_and_hold_their_grade_or_climb_by_stairs()
    {
        var grid = Hillside(0.4);
        var site = Site(grid);
        var town = Village(site);
        var ways = Graded(grid, town).Where(w => w.Part.Class is "street" or "lane").ToList();
        Assert.True(ways.Count > 5, $"{ways.Count} ways");
        Assert.Contains(town.Parts, p => p.Class == Grades.Stairs);
        foreach (var (part, p) in ways)
        {
            double grade = Grades.Of(site.Manner, part.Class);
            for (int i = 0; i < p.Count; i++) Assert.InRange(p.ChangeM(i), -Grades.HeldM - Slack, Grades.HeldM + Slack);
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double rise = Math.Abs(p.Level[i + 1] - p.Level[i]), run = p.Along[i + 1] - p.Along[i];
                if (rise <= grade * run + Slack || p.Shared(i)) continue;
                Assert.True(p.Stepped(i) && Covered(town, (p.At[i] + p.At[i + 1]) * 0.5, Grades.Stairs, Grades.Ramp),
                    $"a {part.Class} climbs {rise / run:P0} at {p.At[i]} without stairs, over its {grade:P0}: {p.Holds[i]} {p.Holds[i + 1]} run {run:0.000} m, levels {p.Level[i]:0.00} {p.Level[i + 1]:0.00} bare {p.Bare[i]:0.00} {p.Bare[i + 1]:0.00} station {i} of {p.Count}");
            }
        }
    }

    [Fact]
    public void Ways_that_meet_share_one_height_and_the_ground_is_graded_to_it()
    {
        var grid = Hillside(0.4);
        var town = Village(Site(grid), 3);
        var ways = Graded(grid, town).Where(w => w.Part.Class is "street" or "lane" or "road").ToList();
        var shaped = Earthworks.Shaped(grid, [town], Side, Side, PerMetre, 1);
        int met = 0;
        foreach (var (part, p) in ways)
            foreach (int end in new[] { 0, p.Count - 1 })
            {
                var other = ways.Where(w => w.Profile != p).Select(w => w.Profile).FirstOrDefault(o => Enumerable.Range(0, o.Count).Any(k => (o.At[k] - p.At[end]).Length < 0.3 * PerMetre));
                if (other == null) continue;
                int k = Enumerable.Range(0, other.Count).MinBy(k => (other.At[k] - p.At[end]).Length);
                Assert.True(Math.Abs(other.Level[k] - p.Level[end]) < 0.3, $"a {part.Class} meets another way {other.Level[k] - p.Level[end]:0.00} m apart at {p.At[end]}");
                met++;
            }
        Assert.True(met > 4, $"{met} meetings");
        double worst = 0;
        foreach (var (_, p) in ways)
            for (int i = 0; i < p.Count; i += 5)
                worst = Math.Max(worst, Math.Abs(shaped.HeightAt(p.At[i].X / Side, p.At[i].Y / Side) * MetresToTheUnit - p.Level[i]));
        Assert.True(worst < 0.5, $"the ground under a way lies {worst:0.00} m off its grade");
    }

    [Fact]
    public void A_town_on_level_ground_has_no_stairs_and_its_ways_leave_the_ground_alone()
    {
        var grid = Land((_, _) => 20);
        foreach (string plan in new[] { Plans.Grown, Plans.Grid, Plans.Maze })
        {
            var town = Village(Site(grid, Manners.Gothic), 4, plan);
            Assert.DoesNotContain(town.Parts, p => p.Class is Grades.Stairs or Grades.Ramp or Grades.Holloway or Grades.Cutting or Grades.Embankment or Grades.Tunnel or Grades.Viaduct);
            Assert.All(Graded(grid, town), w => Assert.All(w.Profile.Holds, h => Assert.NotEqual(Hold.Natural, h)));
            var shaped = Earthworks.Shaped(grid, [town], Side, Side, PerMetre, 1);
            Assert.All(Graded(grid, town).SelectMany(w => w.Profile.At), at => Assert.Equal(20, shaped.HeightAt(at.X / Side, at.Y / Side) * MetresToTheUnit, 1));
        }
    }

    const double Wide = 60, WideMetres = 50;

    static BuiltSite WideSite(ReliefGrid grid, int manner) => new() { Width = Wide, Height = Wide, UnitsPerMetre = 1 / WideMetres, Grid = grid, HeightUnit = 1, Manner = manner };

    static ReliefGrid WideLand(Func<double, double, double> metres)
    {
        var grid = ReliefGrid.Flat(Wide, Wide);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
                grid.H[y * grid.Cols + x] = (float)(metres(x / (double)(grid.Cols - 1) * Wide * WideMetres, y / (double)(grid.Rows - 1) * Wide * WideMetres) / WideMetres);
        return grid;
    }

    static List<Spot> Line(BuiltThing thing, string cls) => thing.Parts.Where(p => p.Class == cls).SelectMany(Points).ToList();

    static int Hairpins(List<Spot> line, double withinM)
    {
        int bends = 0;
        for (int i = 1; i + 1 < line.Count; i++)
        {
            var into = line[i] - line[i - 1];
            int j = i + 1;
            double gone = 0;
            while (j < line.Count && gone < withinM / WideMetres)
            {
                if ((line[j] - line[i]).Unit.Dot(into.Unit) < -0.85)
                {
                    bends++;
                    i = j;
                    break;
                }
                gone += (line[j] - line[j - 1]).Length;
                j++;
            }
        }
        return bends;
    }

    [Fact]
    public void A_road_that_finds_its_way_up_a_steep_hillside_climbs_in_hairpin_bends_within_its_grade()
    {
        var grid = WideLand((_, y) => Math.Clamp(y - 1000, 0, 1000) * 0.25);
        var site = WideSite(grid, Manners.Continental);
        var road = BuiltRoads.Lay(site, "s", [new Spot(30, 10), new Spot(30, 50)], "road", true);
        var line = Line(road, "road");
        Assert.True(Hairpins(line, 120) >= 6, $"{Hairpins(line, 120)} hairpin bends: {string.Join(" ", line)}");
        var p = WayGrading.Profiles(grid, [road], Wide, Wide, 1 / WideMetres, 1).Single().Profile;
        double grade = Grades.Of(Manners.Continental, "road");
        Assert.DoesNotContain(Hold.Natural, p.Holds);
        for (int i = 0; i + 1 < p.Count; i++) Assert.True(Math.Abs(p.Level[i + 1] - p.Level[i]) <= grade * (p.Along[i + 1] - p.Along[i]) + Slack, $"the road climbs too steeply at {p.At[i]}");
        Assert.All(Enumerable.Range(0, p.Count), i => Assert.InRange(p.ChangeM(i), -Grades.HeldM - Slack, Grades.HeldM + Slack));
        var gentle = WideLand((_, y) => y * 0.03);
        var straight = Line(BuiltRoads.Lay(WideSite(gentle, Manners.Continental), "s", [new Spot(30, 10), new Spot(30, 50)], "road", true), "road");
        Assert.Equal(0, Hairpins(straight, 120));
        Assert.True(straight.Max(at => Math.Abs(at.X - 30)) < 0.3, "over gentle ground the road runs straight");
    }

    [Fact]
    public void A_railway_holds_its_ruling_grade_through_cuttings_and_embankments_a_tunnel_under_a_ridge_and_a_viaduct_over_a_valley()
    {
        double Ground(double x, double _) => 100 + 70 * Math.Exp(-Math.Pow((x - 1000) / 130, 2)) - 45 * Math.Exp(-Math.Pow((x - 2000) / 160, 2)) + 6 * Math.Sin(x / 90);
        var grid = WideLand(Ground);
        var rail = BuiltRoads.Lay(WideSite(grid, Manners.Western), "s", [new Spot(4, 30), new Spot(56, 30)], "railway", false);
        Assert.Contains(rail.Parts, p => p.Class == Grades.Tunnel && Points(p).All(at => Math.Abs(at.X * WideMetres - 1000) < 500));
        Assert.Contains(rail.Parts, p => p.Class == Grades.Viaduct && Points(p).Min(at => at.X) * WideMetres < 2000 && Points(p).Max(at => at.X) * WideMetres > 2000);
        Assert.Contains(rail.Parts, p => p.Kind == PartKinds.Area && p.Class == Grades.Cutting);
        Assert.Contains(rail.Parts, p => p.Kind == PartKinds.Area && p.Class == Grades.Embankment);
        Assert.True(rail.Parts.Count(p => p.Class == "railway") >= 2, "the line is split at the tunnel");
        foreach (var (_, p) in WayGrading.Profiles(grid, [rail], Wide, Wide, 1 / WideMetres, 1))
        {
            Assert.Contains(p.Grade, new[] { Grades.MainLine, Grades.MountainLine, Grades.BranchLine });
            for (int i = 0; i + 1 < p.Count; i++)
                if (p.Written(i)) Assert.True(Math.Abs(p.Level[i + 1] - p.Level[i]) <= p.Grade * (p.Along[i + 1] - p.Along[i]) + Slack, $"the line climbs too steeply at {p.At[i]}");
            Assert.All(Enumerable.Range(0, p.Count).Where(i => p.Holds[i] != Hold.Free), i => Assert.InRange(p.ChangeM(i), -Grades.CuttingMostM - Slack, Grades.EmbankingMostM + Slack));
            Assert.DoesNotContain(Hold.Natural, p.Holds);
        }
    }

    [Fact]
    public void Old_lanes_on_slopes_sink_into_holloways_only_where_the_culture_and_age_wore_them()
    {
        var steep = WideLand((_, y) => y * 0.15);
        var flat = WideLand((_, y) => y * 0.02);
        int Sunk(ReliefGrid grid, int manner, string cls = "track") =>
            BuiltRoads.Lay(WideSite(grid, manner), "s", [new Spot(30, 10), new Spot(30, 30)], cls, false).Parts.Count(p => p.Class == Grades.Holloway);
        foreach (int manner in new[] { Manners.Gothic, Manners.Northern, Manners.Continental, Manners.American, Manners.Rus }) Assert.True(Sunk(steep, manner) > 0, Manners.Id(manner));
        foreach (int manner in new[] { Manners.Roman, Manners.Saracen, Manners.Western, Manners.Japanese, Manners.Soviet }) Assert.Equal(0, Sunk(steep, manner));
        Assert.Equal(0, Sunk(flat, Manners.Gothic));
        Assert.Equal(0, Sunk(steep, Manners.Gothic, "road"));
        var hollow = BuiltRoads.Lay(WideSite(steep, Manners.Gothic), "s", [new Spot(30, 10), new Spot(30, 30)], "lane", false);
        var cut = Earthworks.Shaped(steep, [hollow], Wide, Wide, 1 / WideMetres, 1);
        double Sink(Spot at) => (steep.HeightAt(at.X / Wide, at.Y / Wide) - cut.HeightAt(at.X / Wide, at.Y / Wide)) * WideMetres;
        var sunk = Points(hollow.Parts.First(p => p.Class == Grades.Holloway)).ToList();
        var middle = (sunk[sunk.Count / 2] + sunk[sunk.Count / 2 - 1]) * 0.5;
        Assert.InRange(Sink(middle), 1, 3);
        Assert.True(Sink(middle + new Spot(0.2, 0)) < 0.2, "the banks of a holloway stand at the ground beside it");
    }

    [Fact]
    public void A_bridge_keeps_its_approaches_at_the_road_while_the_river_beside_it_is_cut_into_its_banks()
    {
        var grid = Land((_, _) => 20);
        var river = new BuiltThing { Id = "w", Sheet = "s", Kind = BuiltKinds.Water };
        river.Parts.Add(BuiltSite.Line("river", [new Spot(5, 30), new Spot(55, 30)]));
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = PerMetre, Grid = grid, HeightUnit = 1, Manner = Manners.Continental, Others = [river] };
        var road = BuiltRoads.Lay(site, "s", [new Spot(30, 10), new Spot(30, 50)], "road", false);
        Assert.Contains(road.Parts, p => p.Class == "bridge");
        var shaped = Earthworks.Shaped(grid, [river, road], Side, Side, PerMetre, 1);
        double Metres(double x, double y) => shaped.HeightAt(x / Side, y / Side) * MetresToTheUnit;
        double bank = 10.5 * PerMetre;
        Assert.Equal(20, Metres(30, 30 - bank), 1);
        Assert.Equal(20, Metres(30, 30 + bank), 1);
        Assert.True(Metres(42, 30 - bank) < 19.7, $"the bank beside the bridge is cut to {Metres(42, 30 - bank):0.00} m");
        Assert.True(Metres(30, 30) < 19, $"the bed under the bridge lies at {Metres(30, 30):0.00} m");
    }

    [Fact]
    public void A_road_laid_now_is_cut_through_a_rise_and_banked_over_a_dip_to_hold_its_grade()
    {
        double Ground(double xM) => 20 + 5 * Math.Exp(-Math.Pow((xM - 120) / 20, 2)) - 5 * Math.Exp(-Math.Pow((xM - 200) / 20, 2));
        var grid = Land((x, _) => Ground(x * MetresToTheUnit));
        var road = BuiltRoads.Lay(Site(grid), "s", [new Spot(40 * PerMetre, 30), new Spot(280 * PerMetre, 30)], "road", false);
        var line = Points(road.Parts.First(p => p.Class == "road")).ToList();
        var shaped = Earthworks.Shaped(grid, [road], Side, Side, PerMetre, 1);
        double OnRoad(double xM) => shaped.HeightAt(xM * PerMetre / Side, line.MinBy(p => Math.Abs(p.X - xM * PerMetre)).Y / Side) * MetresToTheUnit;
        Assert.True(OnRoad(120) < Ground(120) - 0.5, $"the crest under the road is cut to {OnRoad(120):0.00} m from {Ground(120):0.00}");
        Assert.True(OnRoad(200) > Ground(200) + 0.5, $"the dip under the road is banked to {OnRoad(200):0.00} m from {Ground(200):0.00}");
        Assert.Equal(Ground(120), shaped.HeightAt(120 * PerMetre / Side, 0.7) * MetresToTheUnit, 2);
    }
}
