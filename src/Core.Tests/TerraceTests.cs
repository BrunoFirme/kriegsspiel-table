using KTable.Core.Model;

namespace KTable.Core.Tests;

public class TerraceTests
{
    const double Side = 60, MetresToTheUnit = 5;
    static readonly Spot Middle = new(30, 30);

    static double M(double metres) => metres / MetresToTheUnit;

    static ReliefGrid Ground(Func<double, double, double> metres)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)M(metres(x / (double)(grid.Cols - 1) * Side, y / (double)(grid.Rows - 1) * Side));
            }
        return grid;
    }

    static ReliefGrid Southward(double grade) => Ground((_, v) => (Side - v) * MetresToTheUnit * grade);

    static BuiltSite Site(ReliefGrid grid, int manner, params BuiltThing[] others) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Manner = manner, Others = others };

    static BuiltThing Village(BuiltSite site, int seed = 2, double radius = 32, string plan = Plans.Grown) => Settlements.Lay(site, "s", Middle, radius, seed, 0.6, 0, Plans.Value(plan));

    static List<Spot> Points(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static IEnumerable<BuiltPart> Of(BuiltThing thing, string kind, string cls) => thing.Parts.Where(p => p.Kind == kind && p.Class == cls);

    static double Metres(BuiltSite site, Spot p) => site.HeightAt(p) * MetresToTheUnit;

    static double Shaped(ReliefGrid grid, Spot p) => grid.HeightAt(p.X / Side, p.Y / Side) * MetresToTheUnit;

    static IEnumerable<Spot> Along(List<Spot> line, double step)
    {
        for (int i = 0; i + 1 < line.Count; i++)
        {
            int n = Math.Max(1, (int)Math.Ceiling((line[i + 1] - line[i]).Length / step));
            for (int k = i == 0 ? 0 : 1; k <= n; k++) yield return line[i] + (line[i + 1] - line[i]) * (k / (double)n);
        }
    }

    static Spot[] Square(Spot at, double half) => [at + new Spot(-half, -half), at + new Spot(half, -half), at + new Spot(half, half), at + new Spot(-half, half)];

    static long Crofts(BuiltThing thing, out int laid)
    {
        long print = 1469598103934665603;
        var crofts = thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is "garden" or "orchard" or "vineyard" || p.Kind == PartKinds.Line && p.Class is "fence" or Settlements.GardenWall).ToList();
        foreach (var part in crofts)
        {
            foreach (char c in part.Kind + part.Class) print = unchecked((print ^ c) * 1099511628211);
            foreach (int v in part.At) print = unchecked((print ^ v) * 1099511628211);
        }
        laid = crofts.Count;
        return print;
    }

    [Theory]
    [InlineData(Manners.Continental, 1, -7867602886509093976)]
    [InlineData(Manners.Continental, 2, -6676117797518313561)]
    [InlineData(Manners.Gothic, 1, -2540007225856792836)]
    [InlineData(Manners.Gothic, 2, -7556240457702595788)]
    [InlineData(Manners.Japanese, 1, -2225689359190366538)]
    [InlineData(Manners.Japanese, 2, -747109015943584402)]
    [InlineData(Manners.Byzantine, 1, -2577191356276636305)]
    [InlineData(Manners.Byzantine, 2, 4495243651824070980)]
    [InlineData(Manners.Ottoman, 1, 666647855115390232)]
    [InlineData(Manners.Ottoman, 2, -823003193771283792)]
    [InlineData(Manners.Tribal, 1, -4570546320770639782)]
    [InlineData(Manners.Tribal, 2, -6309213583293217346)]
    public void Crofts_on_level_ground_lay_exactly_as_before_terraces(int manner, int seed, long before)
    {
        var place = Village(Site(Ground((_, _) => 40), manner), seed, 18);
        Assert.Equal(before, Crofts(place, out int laid));
        Assert.True(laid > 0, "the place has its crofts");
        Assert.DoesNotContain(place.Parts, p => Terraces.Shapes(p) || Watercourses.Runs(p) || p.Class == Terraces.Wall);
    }

    [Fact]
    public void Terraces_on_a_hillside_run_along_the_contour()
    {
        var dome = Ground((u, v) => 60 * Math.Exp(-((u - 30) * (u - 30) + v * v) / (2 * 30 * 30)));
        foreach (var grid in new[] { Southward(0.3), dome })
        {
            var site = Site(grid, Manners.Continental);
            var walls = Enumerable.Range(1, 3).SelectMany(seed => Of(Village(site, seed), PartKinds.Line, Terraces.Wall)).ToList();
            Assert.True(walls.Count > 20, $"{walls.Count} terrace walls");
            foreach (var wall in walls)
            {
                var at = Points(wall);
                for (int i = 0; i + 1 < at.Count; i++)
                {
                    var run = at[i + 1] - at[i];
                    var fall = site.Downhill((at[i] + at[i + 1]) * 0.5);
                    if (run.Length < M(1) || fall.Length < 0.05) continue;
                    double off = Math.Asin(Math.Min(1, Math.Abs(run.Unit.Dot(fall.Unit)))) * 180 / Math.PI;
                    Assert.True(off < 10, $"a terrace wall runs {off:0.0} degrees off the contour");
                }
                var mid = at[at.Count / 2];
                var across = (at[Math.Min(at.Count - 1, at.Count / 2 + 1)] - at[Math.Max(0, at.Count / 2 - 1)]).Unit.Left;
                Assert.True(site.HeightAt(mid + across * M(0.5)) < site.HeightAt(mid - across * M(0.5)), "a terrace wall has its low side on the left");
            }
        }
    }

    [Fact]
    public void The_kind_of_terrace_follows_the_culture()
    {
        var plot = Square(Middle, M(24));
        double Riser(int manner, double grade)
        {
            var site = Site(Southward(grade), manner);
            var walls = Terraces.Cut(site, plot, Terraces.Garden).Where(p => p.Class == Terraces.Wall).Select(w => Metres(site, Points(w)[Points(w).Count / 2])).OrderByDescending(h => h).ToList();
            Assert.True(walls.Count >= 3, $"{walls.Count} walls in the plot of culture {manner}");
            return Enumerable.Range(0, walls.Count - 2).Average(i => walls[i] - walls[i + 1]);
        }
        Assert.InRange(Riser(Manners.Roman, 0.15), 0.75, 1.25);
        Assert.InRange(Riser(Manners.Roman, 0.5), 2, 2.5);
        Assert.InRange(Riser(Manners.Gothic, 0.15), 2, 2.6);
        Assert.InRange(Riser(Manners.Gothic, 0.15) / 0.15, 13, 18);
        Assert.InRange(Riser(Manners.Tribal, 0.4), 2.75, 3.1);
        var japan = Terraces.Cut(Site(Southward(0.2), Manners.Japanese), plot, Terraces.Garden);
        Assert.Contains(japan, p => p.Kind == PartKinds.Area && p.Class == Terraces.Paddy);
        Assert.DoesNotContain(japan, p => p.Kind == PartKinds.Area && p.Class == Terraces.Terrace);
        Assert.Contains(Terraces.Cut(Site(Southward(0.2), Manners.Japanese), plot, "orchard"), p => p.Class == Terraces.Terrace);
        Assert.NotEmpty(Terraces.Cut(Site(Southward(0.7), Manners.Nippon), plot, Terraces.Garden));
        Assert.Empty(Terraces.Cut(Site(Southward(0.7), Manners.Byzantine), plot, Terraces.Garden));
        foreach (int none in new[] { Manners.Egyptian, Manners.Mongol, Manners.Rus, Manners.American, Manners.Soviet })
            Assert.Empty(Terraces.Cut(Site(Southward(0.3), none), plot, Terraces.Garden));
        Assert.Empty(Terraces.Cut(Site(Southward(0.08), Manners.Roman), plot, Terraces.Garden));
    }

    [Fact]
    public void Paddies_lie_level_and_step_down_the_hill()
    {
        var grid = Southward(0.2);
        var site = Site(grid, Manners.Japanese);
        var paddies = Terraces.Cut(site, Square(Middle, M(24)), Terraces.Garden).Where(p => p.Class == Terraces.Paddy).ToList();
        var thing = new BuiltThing { Sheet = "s", Kind = BuiltKinds.Settlement };
        thing.Parts.AddRange(paddies);
        var shaped = Earthworks.Shaped(grid, [thing], Side, Side, 1 / MetresToTheUnit, 1);
        var levels = new List<double>();
        foreach (var paddy in paddies)
        {
            var ring = Points(paddy);
            var mid = Terraces.Middle(ring);
            var inner = ring.Select(p => mid + (p - mid) * 0.7).Append(mid).Select(p => Shaped(shaped, p)).ToList();
            Assert.True(inner.Max() - inner.Min() < 0.05, $"a paddy lies level: {inner.Min():0.00} to {inner.Max():0.00} m");
            levels.Add(inner.Average());
        }
        Assert.True(levels.Distinct().Count() > 3, "the paddies step down the hill");
        foreach (int manner in new[] { Manners.Japanese, Manners.Pacific, Manners.Nippon })
            Assert.True(Enumerable.Range(1, 3).Sum(seed => Of(Village(Site(grid, manner), seed, 18), PartKinds.Area, Terraces.Paddy).Count()) >= 6, $"a flight of rice terraces by the place of culture {manner}");
    }

    [Fact]
    public void Vineyards_on_terraces_face_the_sun_on_slopes_up_to_thirty_degrees()
    {
        foreach (double grade in new[] { 0.3, 0.55 })
        {
            var places = new[] { Manners.Byzantine, Manners.Ottoman }.SelectMany(manner => Enumerable.Range(1, 3).Select(seed => Village(Site(Southward(grade), manner), seed, 18))).ToList();
            var site = Site(Southward(grade), Manners.Byzantine);
            var vineyards = places.SelectMany(t => Of(t, PartKinds.Area, Terraces.Vineyard).Select(v => (Place: t, Ring: Points(v)))).ToList();
            Assert.True(vineyards.Count > 0, $"no vineyard on a {grade:0%} slope");
            foreach (var (place, ring) in vineyards)
            {
                Assert.True(Terraces.Fall(site, ring).Down.Y > 0.9, "the vineyard faces south");
                Assert.Contains(Of(place, PartKinds.Line, Terraces.Wall), w => Enclosures.Inside(ring, Points(w)[Points(w).Count / 2]));
            }
        }
    }

    static double Bed(double u) => (60 - u) * 0.01 + Math.Clamp(34 - u, 0, 8) * 0.07;

    static ReliefGrid Valley() => Ground((u, v) => (Bed(u) + Math.Abs(v - 30) * 0.15) * MetresToTheUnit + 40);

    static BuiltThing Brook()
    {
        var brook = new BuiltThing { Id = "w", Sheet = "s", Kind = "water" };
        brook.Parts.Add(BuiltSite.Line("stream", [new Spot(0, 30), new Spot(60, 30)]));
        return brook;
    }

    static void NeverRises(ReliefGrid bare, IReadOnlyList<BuiltThing> things, BuiltPart course, string what)
    {
        var shaped = Earthworks.Shaped(bare, things, Side, Side, 1 / MetresToTheUnit, 1);
        var line = Along(Points(course), M(1)).ToList();
        var bed = line.Select(p => Shaped(shaped, p)).ToList();
        Assert.All(Enumerable.Range(1, bed.Count - 1), i => Assert.True(bed[i] <= bed[i - 1] + 0.1, $"the {what} rises at {line[i]}: {bed[i - 1]:0.00} to {bed[i]:0.00} m"));
        double low = double.MaxValue;
        foreach (var p in line)
        {
            low = Math.Min(low, Shaped(bare, p));
            Assert.True(Shaped(bare, p) - low < 1.5, $"the {what} is laid along falling ground, not dug through a rise at {p}");
        }
    }

    [Fact]
    public void A_mill_takes_the_steepest_fall_of_its_stream_with_the_weir_upstream_and_a_leat_that_never_rises()
    {
        var bare = Valley();
        var site = Site(bare, Manners.Continental, Brook());
        var mills = Enumerable.Range(1, 8).Select(seed => Settlements.Lay(site, "s", new Spot(30, 22), 12, seed, 0.6, 0, 0)).Where(t => Of(t, PartKinds.Sign, "wheel").Any()).ToList();
        Assert.NotEmpty(mills);
        foreach (var town in mills)
        {
            var wheel = Points(Of(town, PartKinds.Sign, "wheel").Single())[0];
            Assert.InRange(wheel.X, 25, 35);
            var weir = Terraces.Middle(Points(Of(town, PartKinds.Line, Watercourses.Weir).Single()));
            Assert.True(weir.X < wheel.X - M(20) && Bed(weir.X) > Bed(wheel.X), $"the weir at {weir} stands upstream of the wheel at {wheel}");
            NeverRises(bare, [town, site.Others[0]], Of(town, PartKinds.Line, Watercourses.Leat).Single(), "leat");
        }
    }

    [Fact]
    public void An_irrigation_channel_and_a_garden_rill_never_rise()
    {
        var bare = Southward(0.15);
        int channels = 0;
        foreach (int manner in new[] { Manners.Saracen, Manners.Persian, Manners.NorthAfrican })
            for (int seed = 1; seed <= 3; seed++)
            {
                var place = Village(Site(bare, manner), seed, 18);
                foreach (var channel in Of(place, PartKinds.Line, Watercourses.Channel))
                {
                    NeverRises(bare, [place], channel, "channel");
                    channels++;
                }
            }
        Assert.True(channels >= 6, $"{channels} channels");
    }

    static double RimFall(BuiltSite site, BuiltPart pond)
    {
        var rim = Points(pond).Select(p => Metres(site, p)).ToList();
        double span = Points(pond).Max(p => Points(pond).Max(q => (p - q).Length)) * MetresToTheUnit;
        return rim.Max() - rim.Min() - Math.Max(0, span * 0.05 - 0.8);
    }

    [Fact]
    public void A_pond_lies_in_a_hollow_or_behind_a_dam_across_a_fold_and_never_as_a_pit_on_a_slope()
    {
        var bowl = Ground((u, v) => (Side - v) * MetresToTheUnit * 0.1 - 8 * Math.Exp(-((u - 16) * (u - 16) + (v - 36) * (v - 36)) / (2 * 8 * 8)));
        var fold = Ground((u, v) => (Side - v) * MetresToTheUnit * 0.1 + Math.Min(Math.Abs(u - 34), 8) * MetresToTheUnit * 0.15);
        var bottom = Enumerable.Range(0, 120 * 120).Select(i => new Spot(i % 120 * 0.5, i / 120 * 0.5)).Where(p => (p - new Spot(16, 36)).Length < 12)
            .MinBy(p => bowl.HeightAt(p.X / Side, p.Y / Side));
        int inHollow = 0, held = 0;
        foreach (var grid in new[] { Southward(0.15), bowl, fold })
            foreach (int manner in new[] { Manners.Continental, Manners.Gothic })
                for (int seed = 1; seed <= 8; seed++)
                {
                    var site = Site(grid, manner);
                    var place = Village(site, seed, 18);
                    foreach (var pond in Of(place, PartKinds.Area, "pond"))
                    {
                        Assert.True(RimFall(site, pond) <= 0.8, $"a pond on ground falling {RimFall(site, pond):0.0} m across its rim beyond the gentle fall of level ground");
                        if (grid == bowl && (Terraces.Middle(Points(pond)) - bottom).Length < M(12)) inHollow++;
                    }
                    var dams = Of(place, PartKinds.Line, Ponds.Dam).ToList();
                    foreach (var reservoir in Of(place, PartKinds.Area, Ponds.Held))
                    {
                        var ring = Points(reservoir);
                        double level = ring.Select(p => Metres(site, p)).OrderBy(h => h).ElementAt(ring.Count / 2);
                        Assert.All(ring, p => Assert.True(Metres(site, p) <= level + 0.25, $"the water at {p} would stand above its level"));
                        var dam = dams.Select(Points).MinBy(d => (Terraces.Middle(d) - Terraces.Middle(ring)).Length)!;
                        Assert.True(dam.All(p => Metres(site, p) >= level), "the dam is tied into ground above the water");
                        var shaped = Earthworks.Shaped(grid, [place], Side, Side, 1 / MetresToTheUnit, 1);
                        var middle = Terraces.Middle(ring);
                        var water = ring.Select(p => middle + (p - middle) * 0.6).Select(p => Shaped(shaped, p)).ToList();
                        Assert.True(water.Max() - water.Min() < 0.05, $"the held water lies level: {water.Min():0.00} to {water.Max():0.00} m");
                        held++;
                    }
                }
        Assert.True(inHollow > 0, "a pond lies in the hollow");
        Assert.True(held > 0, "water is held behind a dam across the fold");
    }

    [Fact]
    public void A_roman_aqueduct_follows_the_contour_crosses_a_side_valley_on_arcades_and_never_runs_uphill()
    {
        const double Scale = 25;
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1) * Side, v = y / (double)(grid.Rows - 1) * Side;
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
                grid.H[y * grid.Cols + x] = (float)(((Side - v) * Scale * 0.08 + 40 - 6 * Math.Exp(-Math.Pow((u - 40) / 1.2, 2))) / Scale);
            }
        var site = new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / Scale, Grid = grid, HeightUnit = 1, Manner = Manners.Roman };
        var town = Settlements.Lay(site, "s", new Spot(48, 36), 10, 3, 0.7, 0, 0);
        var aqueduct = Points(Of(town, PartKinds.Line, Hallmarks.Aqueduct).Single());
        var arcades = Of(town, PartKinds.Line, Watercourses.Arcade).Select(Points).ToList();
        double Height(Spot p) => site.HeightAt(p) * Scale;
        Assert.NotEmpty(arcades);
        Assert.All(arcades, c => Assert.True((c[1] - c[0]).Length < M(120) * MetresToTheUnit / Scale, "an arcade spans the valley, not the hillside"));
        Assert.True(Height(aqueduct[0]) >= Height(aqueduct[^1]) - 0.3, "the source is not below the town's tank");
        double run = Enumerable.Range(0, aqueduct.Count - 1).Sum(i => (aqueduct[i + 1] - aqueduct[i]).Length), done = 0;
        for (int i = 0; i + 1 < aqueduct.Count; i++)
        {
            Spot a = aqueduct[i], b = aqueduct[i + 1];
            double level = Height(aqueduct[^1]) + Watercourses.AqueductFall * (run - done) * Scale;
            Assert.True(Math.Abs(Height(a) - level) < 1, $"the aqueduct leaves the contour at {a}: ground {Height(a):0.0} m, channel {level:0.0} m");
            foreach (var p in Enumerable.Range(0, 11).Select(k => a + (b - a) * (k / 10.0)))
            {
                Assert.True(Height(p) <= level + 2.6, $"the channel at {p} would run under a rise");
                bool arcaded = arcades.Any(c => p.FromSegment(c[0], c[1]) < 1e-3);
                Assert.True(arcaded || Height(p) >= level - 3.2, $"the channel at {p} crosses a dip of {level - Height(p):0.0} m without an arcade");
            }
            done += (b - a).Length;
        }
    }

    [Fact]
    public void A_qanat_rises_toward_the_hills_from_its_outlet_above_the_village_and_the_best_houses_stand_near_it()
    {
        var site = Site(Ground((_, v) => (Side - v) * MetresToTheUnit * 0.06), Manners.Persian);
        double near = 0, far = 0;
        int nearCount = 0, farCount = 0;
        for (int seed = 1; seed <= 4; seed++)
        {
            var place = Village(site, seed, 16);
            var qanat = Points(Of(place, PartKinds.Line, Hallmarks.Qanat).First());
            Assert.True(qanat[0].Y < Middle.Y && (qanat[0] - Middle).Length < M(16 * MetresToTheUnit) * 1.1, $"the outlet at {qanat[0]} lies just above the village");
            Assert.All(Enumerable.Range(1, qanat.Count - 1), i => Assert.True(site.HeightAt(qanat[i]) > site.HeightAt(qanat[i - 1]), "the shaft line climbs toward the hills"));
            foreach (var home in place.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "villa"))
            {
                var ring = Points(home);
                double size = (ring[1] - ring[0]).Length * (ring[2] - ring[1]).Length;
                if ((Terraces.Middle(ring) - qanat[0]).Length < 16 * 0.45) (near, nearCount) = (near + size, nearCount + 1);
                else (far, farCount) = (far + size, farCount + 1);
            }
        }
        Assert.True(nearCount > 0 && near / nearCount > far / Math.Max(1, farCount), $"houses by the outlet {near / Math.Max(1, nearCount):0.0} against {far / Math.Max(1, farCount):0.0}");
    }

    [Fact]
    public void A_qanat_beside_a_short_ridge_runs_out_from_the_town_in_a_few_shafts_instead_of_wandering_along_the_crest()
    {
        double Crest(double u) => Math.Exp(-Math.Pow(Math.Max(0, Math.Abs(u - 30) - 12) / 4, 2));
        var site = Site(Ground((u, v) => 45 * Math.Exp(-Math.Pow((v - 25) / 5, 2)) * Crest(u)), Manners.Persian);
        for (int seed = 1; seed <= 6; seed++)
        {
            var place = Village(site, seed, 16);
            foreach (var qanat in Of(place, PartKinds.Line, Hallmarks.Qanat).Select(Points))
                Assert.All(Enumerable.Range(1, qanat.Count - 1), i => Assert.True((qanat[i] - Middle).Length > (qanat[i - 1] - Middle).Length, "each shaft lies farther from the town"));
            Assert.InRange(Of(place, PartKinds.Sign, Hallmarks.Qanat).Count(), 1, 24);
        }
    }

    [Fact]
    public void A_persian_garden_on_a_slope_steps_down_in_terraces_from_its_pavilion_to_a_pool()
    {
        var site = Site(Southward(0.12), Manners.Persian);
        var place = Enumerable.Range(1, 4).Select(seed => Village(site, seed, 18)).First(t => Of(t, PartKinds.Area, Terraces.Pool).Any());
        var pool = Terraces.Middle(Points(Of(place, PartKinds.Area, Terraces.Pool).Single()));
        var rill = Points(Of(place, PartKinds.Line, Watercourses.Channel).MaxBy(c => Points(c).Count == 2 ? (Points(c)[1] - Points(c)[0]).Length : 0)!);
        var pavilion = place.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "public").Select(p => Terraces.Middle(Points(p))).MinBy(p => (p - rill[0]).Length);
        Assert.True(site.HeightAt(pavilion) > site.HeightAt(pool) + M(4), "the pavilion stands on the top terrace above the pool");
        Assert.True((rill[^1] - pool).Length < M(8) && site.HeightAt(rill[0]) > site.HeightAt(rill[^1]), "the rill runs down to the pool");
        int steps = Of(place, PartKinds.Area, Terraces.Terrace).Count(t => Enclosures.Inside(Points(t), Terraces.Middle(rill)) || Points(t).Any(p => (p - pool).Length < M(60)));
        Assert.InRange(steps, 3, 6);
    }
}
