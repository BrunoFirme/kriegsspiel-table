using KTable.Core.Model;

namespace KTable.Core.Tests;

public class WarGroundTests
{
    const double Side = 60, Metres = 25, FlatSide = 60, FlatMetres = 50;
    const string CastlesBefore = "b7363599612826f6", FortsBefore = "c730fb53d0e65719";
    static readonly Spot Middle = new(30, 30), FlatMiddle = new(30, 30);
    static readonly double Square = Math.Sin(25 * Math.PI / 180);

    static double M(double metres) => metres / Metres;

    static double InMetres(double units) => units * Metres;

    static BuiltSite Land(int manner, Func<double, double, double> metres)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1) * Side, v = y / (double)(grid.Rows - 1) * Side;
                grid.H[y * grid.Cols + x] = (float)M(metres(u, v));
                grid.Kind[y * grid.Cols + x] = GroundKinds.Grass;
            }
        return new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / Metres, Grid = grid, HeightUnit = 1, Manner = manner };
    }

    static double Bump(double u, double v, double cu, double cv, double tall, double wideU, double wideV) =>
        tall * Math.Exp(-((u - cu) * (u - cu) / (wideU * wideU) + (v - cv) * (v - cv) / (wideV * wideV)));

    static double Summit(double u, double v) => Bump(u, v, 30, 30, 80, 10, 10);

    static double Spur(double u, double v) => (60 - u) * Metres * 0.12 + Bump(u, v, 30, 30, 25, 30, 3.5);

    static double Ridge(double u, double v) => Bump(u, v, 30, 30, 60, 400, 6);

    static List<Spot> Of(BuiltPart part) => BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();

    static IEnumerable<List<Spot>> Lines(BuiltThing thing, string cls) => thing.Parts.Where(p => p.Kind == PartKinds.Line && p.Class == cls).Select(Of);

    static IEnumerable<List<Spot>> Areas(BuiltThing thing, string cls) => thing.Parts.Where(p => p.Kind == PartKinds.Area && p.Class == cls).Select(Of);

    static Spot Mean(IReadOnlyList<Spot> points) => new(points.Average(p => p.X), points.Average(p => p.Y));

    static Spot Longest(IReadOnlyList<Spot> ring)
    {
        var pairs = ring.SelectMany(a => ring.Select(b => b - a));
        return pairs.MaxBy(d => d.Length).Unit;
    }

    static double Spread(BuiltSite site, IEnumerable<Spot> points)
    {
        var heights = points.Select(site.HeightAt).ToList();
        return InMetres(heights.Max() - heights.Min());
    }

    static string Plan(BuiltThing castle) => new[] { Castles.Motte, Castles.Enclosure, Castles.Quadrangle, Castles.Concentric, Castles.Spur, Castles.Kremlin, Castles.Kastron,
        Castles.Citadel, Castles.Nested }[(int)castle.Setting("plan", 0)];

    [Fact]
    public void A_spur_castle_lies_along_the_spur_with_its_neck_ditch_across_the_uphill_end()
    {
        var site = Land(Manners.Gothic, Spur);
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var castle = Defences.Lay(site, "s", Defences.Castle, Middle, 3, seed, 0, 0, 1.1);
            Assert.Equal(Castles.Spur, Plan(castle));
            var platform = Areas(castle, Forts.Platform).First();
            Assert.True(Math.Abs(Longest(platform).Y) <= Square, $"seed {seed}: the castle's long axis runs down the spur, not across it");
            var necks = Lines(castle, Castles.Cut).ToList();
            Assert.Single(necks);
            var neck = necks[0];
            Assert.True(neck.All(p => p.X < platform.Min(q => q.X)), $"seed {seed}: the neck ditch lies beyond the uphill end");
            Assert.True(Math.Abs((neck[^1] - neck[0]).Unit.X) <= Square, $"seed {seed}: the neck ditch runs across the spur");
            var keep = castle.Parts.First(p => p.Kind == PartKinds.Block && p.Class == "keep");
            Assert.True(BuiltSite.Round(keep).At.X < Mean(platform).X, $"seed {seed}: the keep stands at the uphill end");
        }
    }

    [Fact]
    public void A_summit_castle_stands_on_the_top_with_its_platform_cut_level()
    {
        var site = Land(Manners.Gothic, Summit);
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var castle = Defences.Lay(site, "s", Defences.Castle, new Spot(30.9, 30.6), 3, seed, 0, 0, 0.3);
            Assert.Contains(Plan(castle), new[] { Castles.Enclosure, Castles.Concentric });
            var at = Spot.Of(BuiltGeometry.Points(castle.Outline)[0]);
            Assert.True(InMetres((at - Middle).Length) < 15, $"seed {seed}: the castle stands {InMetres((at - Middle).Length):0} m from the top");
            Assert.Empty(Areas(castle, Forts.Ditch).Concat(Areas(castle, Castles.Cut)));
            var shaped = Earthworks.Shaped(site.Grid!, [castle], Side, Side, 1 / Metres, 1);
            var platform = Areas(castle, Forts.Platform).First();
            var inside = platform.Select(p => at + (p - at) * 0.6).Append(at).ToList();
            var heights = inside.Select(p => InMetres(shaped.HeightAt(p.X / Side, p.Y / Side))).ToList();
            Assert.True(heights.Max() - heights.Min() < 1, $"seed {seed}: the platform is level within {heights.Max() - heights.Min():0.0} m");
        }
    }

    [Fact]
    public void A_ridge_castle_is_stretched_along_the_crest_with_ditches_cut_across_both_ends()
    {
        var site = Land(Manners.Gothic, Ridge);
        var castle = Defences.Lay(site, "s", Defences.Castle, new Spot(30, 30.2), 3, 2, 0, 0, 0.2);
        var platform = Areas(castle, Forts.Platform).First();
        Assert.True(Math.Abs(Longest(platform).Y) <= Square, "the castle runs along the crest");
        var ditches = Lines(castle, Castles.Cut).ToList();
        Assert.Equal(2, ditches.Count);
        Assert.Contains(ditches, d => d.All(p => p.X < platform.Min(q => q.X)));
        Assert.Contains(ditches, d => d.All(p => p.X > platform.Max(q => q.X)));
    }

    [Fact]
    public void A_contour_hillfort_keeps_one_level_round_the_hilltop()
    {
        var site = Land(Manners.Tribal, Summit);
        var fort = Defences.Lay(site, "s", Defences.Hillfort, new Spot(30.5, 30.4), 5, 3, 0, 0, 0);
        var ramparts = Lines(fort, "rampart").ToList();
        Assert.True(ramparts.Count >= 2, $"{ramparts.Count} ramparts");
        Assert.All(ramparts, r => Assert.True(Spread(site, r) < 3, $"a rampart rises and falls {Spread(site, r):0.0} m"));
        Assert.True(Lines(fort, Castles.Cut).Count() >= 2, "ditches below the ramparts");
        Assert.True(fort.Parts.Count(p => p.Class == "roundhouse") >= 3, "round houses inside");
    }

    [Fact]
    public void A_promontory_fort_cuts_off_its_spur_with_one_bank_and_ditch_across_the_neck()
    {
        var site = Land(Manners.Tribal, Spur);
        var fort = Defences.Lay(site, "s", Defences.Hillfort, Middle, 4, 2, 0, 0, 0);
        var banks = Lines(fort, "rampart").ToList();
        Assert.InRange(banks.Count, 1, 2);
        Assert.All(banks, bank => Assert.True(Math.Abs((bank[^1] - bank[0]).Unit.X) <= Square, "the bank crosses the spur"));
        Assert.All(banks, bank => Assert.True(bank.All(p => p.X < Middle.X), "the bank cuts the neck on the uphill side"));
        var ditch = Lines(fort, Castles.Cut).SelectMany(d => d).ToList();
        Assert.NotEmpty(ditch);
        Assert.True(ditch.Max(p => p.X) < banks.SelectMany(b => b).Min(p => p.X), "the ditch lies outside the bank");
        Assert.True(fort.Parts.Where(p => p.Class == "roundhouse").All(p => BuiltSite.Round(p).At.X > banks.SelectMany(b => b).Max(q => q.X)), "the houses stand behind the bank");
    }

    [Fact]
    public void A_Roman_camp_turns_its_rear_gate_to_the_highest_ground()
    {
        var site = Land(Manners.Roman, (u, v) => (60 - v) * Metres * 0.04);
        var camp = Defences.Lay(site, "s", Defences.Camp, Middle, 4, 1, 0, 0, 0);
        var rear = Lines(camp, "lane").First()[0];
        var gates = Lines(camp, "street").SelectMany(s => s).Append(rear).ToList();
        Assert.Equal(rear, gates.MaxBy(site.HeightAt));
        Assert.True(rear.Y < Middle.Y, "the porta decumana faces up the slope");
    }

    [Fact]
    public void A_Roman_camp_moves_off_ground_a_hill_looks_down_on()
    {
        var site = Land(Manners.Roman, (u, v) => Bump(u, v, 35, 30, 60, 3, 3));
        var camp = Defences.Lay(site, "s", Defences.Camp, Middle, 2.5, 1, 0, 0, 0);
        var at = Spot.Of(BuiltGeometry.Points(camp.Outline)[0]);
        Assert.True((at - new Spot(35, 30)).Length > (Middle - new Spot(35, 30)).Length || site.HeightAt(at) > site.HeightAt(Middle) + M(10), "the camp leaves the foot of the hill");
    }

    static double Plateau(double u, double v) => u < 34 ? 100 : Math.Max(10, 100 - (u - 34) * Metres * 0.8);

    [Fact]
    public void A_bastioned_fort_puts_its_heavy_front_to_the_gentle_side_and_a_light_trace_along_the_cliff()
    {
        var site = Land(Manners.Continental, Plateau);
        var fort = Defences.Lay(site, "s", Defences.Star, new Spot(31, 30), 4, 3, 5, 1, 0);
        var star = Forts.Of(fort)!;
        var east = star.Tips.Where(t => Math.Cos(t.Bearing) > 0.5).ToList();
        var west = star.Tips.Where(t => Math.Cos(t.Bearing) < -0.3).ToList();
        Assert.NotEmpty(east);
        Assert.True(east.Max(t => t.Radius) < west.Min(t => t.Radius), "the bastions on the cliff side are drawn in");
        var ravelins = Areas(fort, Forts.Ravelin).ToList();
        Assert.NotEmpty(ravelins);
        Assert.All(ravelins, r => Assert.True(Mean(r).X < star.Centre.X, "ravelins guard only the approachable front"));
        Assert.Empty(Areas(fort, Forts.Ditch));
        Assert.All(Lines(fort, Castles.Cut).SelectMany(d => d), p => Assert.True(p.X < 34, "no ditch is cut into the cliff"));
        Assert.True(fort.Parts.Any(p => p.Kind == PartKinds.Area && p.Class == Forts.Ground), "the fort stands on its plateau");
    }

    [Fact]
    public void A_fort_overlooked_by_a_near_summit_holds_it_with_a_detached_redoubt()
    {
        var site = Land(Manners.Continental, (u, v) => Bump(u, v, 50, 30, 90, 4, 4));
        var fort = Defences.Lay(site, "s", Defences.Star, new Spot(20, 30), 3, 3, 5, 0, 0);
        var grounds = Areas(fort, Forts.Ground).ToList();
        Assert.Equal(2, grounds.Count);
        Assert.True((Mean(grounds[1]) - new Spot(50, 30)).Length < M(60), "the redoubt holds the summit");
        Assert.Contains(Lines(fort, "track"), t => (t[^1] - new Spot(50, 30)).Length < (t[0] - new Spot(50, 30)).Length);
    }

    [Fact]
    public void Redoubts_take_a_knoll_and_push_fleches_forward_onto_the_open_ground()
    {
        var site = Land(Manners.Continental, (u, v) => Bump(u, v, 31, 30, 30, 2.2, 2.2));
        var redoubt = Defences.Lay(site, "s", Defences.Redoubt, new Spot(28, 30), 1.6, 1, 0, 0, 0);
        var at = Spot.Of(BuiltGeometry.Points(redoubt.Outline)[0]);
        Assert.True(InMetres((at - new Spot(31, 30)).Length) < 30, "the redoubt stands on the knoll");
        var fleches = Lines(redoubt, "fleche").ToList();
        Assert.Equal(2, fleches.Count);
        Assert.All(fleches, f => Assert.True(f[1].X > at.X + M(60), "the fleches stand forward of the knoll"));
    }

    static double RidgeAcross(double u, double v) => Bump(u, v, 30, 30, 60, 6, 400);

    static List<Spot> Fire(BuiltThing thing) => Lines(thing, "trench").MaxBy(t => t.Count)!;

    [Fact]
    public void Fieldworks_follow_the_contour_and_differ_by_doctrine()
    {
        BuiltThing Held(int manner) => Defences.Lay(Land(manner, RidgeAcross), "s", Defences.Strongpoint, new Spot(30.3, 30), 4, 1, 0, 0, 0);
        var site = Land(Manners.Western, RidgeAcross);
        var western = Held(Manners.Western);
        Assert.True(Mean(Fire(western)).X < 30, "the Western main line holds the reverse slope");
        Assert.Contains(western.Parts, p => p.Kind == PartKinds.Sign && p.Class == "lookout");
        Assert.DoesNotContain(western.Parts, p => p.Class is "escarp" or "cave");
        var soviet = Held(Manners.Soviet);
        var escarp = Lines(soviet, "escarp").Single();
        Assert.True(Mean(escarp).X > 30.5, "the anti-tank escarpment is cut on the forward slope");
        Assert.True(Spread(site, escarp) < 3, $"the escarpment follows the contour within {Spread(site, escarp):0.0} m");
        Assert.True(Mean(Fire(soviet)).X > 30, "the Soviet line stands on the forward slope");
        var pacific = Held(Manners.Pacific);
        Assert.Contains(pacific.Parts, p => p.Kind == PartKinds.Block && p.Class == "cave");
        Assert.Contains(pacific.Parts, p => p.Kind == PartKinds.Line && p.Class == "tunnel");
        foreach (var held in new[] { western, soviet, pacific })
            Assert.True(Spread(site, Fire(held)) < 4, $"a fire trench follows the contour within {Spread(site, Fire(held)):0.0} m");
    }

    static double Cliffed(double u, double v) => u < 33 ? 120 : u < 34 ? 120 - (u - 33) * Metres * 2.4 : 60;

    [Fact]
    public void Town_walls_never_run_across_a_face_steeper_than_thirty_degrees()
    {
        var site = Land(Manners.Gothic, Cliffed);
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var town = Settlements.Lay(site, "s", Middle, 7, seed, 0.75, 0.5, Plans.Value(Plans.Offered(Manners.Gothic)[0]));
            var walls = Lines(town, "wall").ToList();
            Assert.NotEmpty(walls);
            foreach (var wall in walls)
                for (int i = 0; i + 1 < wall.Count; i++)
                    Assert.True(site.Downhill((wall[i] + wall[i + 1]) * 0.5).Length < BuiltSite.SteepestBuilt + 0.05, $"seed {seed}: a wall climbs a cliff at {wall[i]}");
        }
    }

    [Fact]
    public void A_town_castle_takes_the_knoll_at_the_edge_of_the_town()
    {
        var knoll = new Spot(39.5, 30);
        var site = Land(Manners.Rus, (u, v) => 40 + Bump(u, v, knoll.X, knoll.Y, 25, 2, 2));
        int castled = 0;
        foreach (int seed in new[] { 1, 2, 3, 4 })
        {
            var town = Settlements.Lay(site, "s", Middle, 12, seed, 0.75, 0.5, Plans.Value(Plans.Offered(Manners.Rus)[0]));
            var platforms = Areas(town, Forts.Platform).ToList();
            if (platforms.Count == 0) continue;
            castled++;
            Assert.True(InMetres((Mean(platforms[0]) - knoll).Length) < 60, $"seed {seed}: the kremlin stands {InMetres((Mean(platforms[0]) - knoll).Length):0} m from the knoll");
        }
        Assert.True(castled >= 2, $"{castled} kremlins");
    }

    [Fact]
    public void A_Saracen_citadel_on_its_hill_wears_a_paved_glacis_and_a_Japanese_mountain_castle_battered_stone_walls()
    {
        var citadel = Defences.Lay(Land(Manners.Saracen, Summit), "s", Defences.Castle, Middle, 3, 1, 0, 0, 0);
        Assert.Equal(Castles.Citadel, Plan(citadel));
        Assert.Single(Areas(citadel, Castles.Talus));
        var yamajiro = Defences.Lay(Land(Manners.Japanese, Summit), "s", Defences.Castle, Middle, 3, 1, 0, 0, 0);
        Assert.Equal(Castles.Nested, Plan(yamajiro));
        Assert.Equal(3, Lines(yamajiro, Castles.Ishigaki).Count());
        var core = Areas(yamajiro, Forts.Platform).Last();
        Assert.True(InMetres((Mean(core) - Middle).Length) < 25, "the inner ward crowns the summit");
    }

    [Fact]
    public void Water_never_climbs_round_a_castle_on_a_hill()
    {
        foreach (var land in new Func<double, double, double>[] { Summit, Spur, Ridge })
            foreach (int manner in new[] { Manners.Gothic, Manners.Rus, Manners.Byzantine, Manners.Saracen, Manners.Japanese })
                Assert.Empty(Areas(Defences.Lay(Land(manner, land), "s", Defences.Castle, Middle, 3, 2, 0, 0, 0), Forts.Ditch));
    }

    static BuiltSite Flat(int manner) =>
        new() { Width = FlatSide, Height = FlatSide, UnitsPerMetre = 1 / FlatMetres, Grid = ReliefGrid.Flat(FlatSide, FlatSide), HeightUnit = FlatSide, Others = [], Manner = manner };

    static string Digest(IEnumerable<BuiltThing> things)
    {
        ulong h = 14695981039346656037;
        void Mix(long v) => h = unchecked((h ^ (ulong)v) * 1099511628211);
        foreach (var thing in things)
        {
            foreach (char c in string.Join(",", thing.Outline)) Mix(c);
            foreach (var part in thing.Parts)
            {
                foreach (char c in part.Kind + "/" + part.Class) Mix(c);
                foreach (int v in part.At) Mix(v);
                Mix(part.Turn);
            }
        }
        return h.ToString("x16");
    }

    static IEnumerable<BuiltThing> FlatCastles() =>
        new[] { Manners.Gothic, Manners.Rus, Manners.Byzantine, Manners.Saracen, Manners.Japanese, Manners.Continental }
            .SelectMany(manner => Enumerable.Range(1, 6).Select(seed => Defences.Lay(Flat(manner), "s", Defences.Castle, FlatMiddle, 3, seed, 0, 0, seed * 0.7)));

    static IEnumerable<BuiltThing> FlatForts() =>
        Enumerable.Range(1, 3).SelectMany(seed => new[]
        {
            Defences.Lay(Flat(Manners.Continental), "s", Defences.Star, FlatMiddle, 4, seed, 5, 1, seed * 0.4),
            Defences.Lay(Flat(Manners.Continental), "s", Defences.Redoubt, FlatMiddle, 1.5, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Byzantine), "s", Defences.Fortress, FlatMiddle, 3, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Roman), "s", Defences.Camp, FlatMiddle, 4, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Persian), "s", Defences.Fortress, FlatMiddle, 3, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Tribal), "s", Defences.Hillfort, FlatMiddle, 2 + seed, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Western), "s", Defences.Strongpoint, FlatMiddle, 4, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Soviet), "s", Defences.Strongpoint, FlatMiddle, 4, seed, 0, 0, seed * 0.4),
            Defences.Lay(Flat(Manners.Pacific), "s", Defences.Battery, FlatMiddle, 3, seed, 0, 0, seed * 0.4),
        });

    [Fact]
    public void Castles_and_forts_on_level_ground_lay_exactly_as_before()
    {
        Assert.Equal(CastlesBefore, Digest(FlatCastles()));
        Assert.Equal(FortsBefore, Digest(FlatForts()));
    }
}
