using KTable.Core.Model;

namespace KTable.Core.Tests;

public class HillFormTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 9;
    static readonly Spot Middle = new(30, 30);

    static ReliefGrid Land(Func<double, double, double> metres, Func<double, double, byte>? kind = null)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1) * Side, v = y / (double)(grid.Rows - 1) * Side;
                grid.H[y * grid.Cols + x] = (float)(metres(u, v) / MetresToTheUnit);
                grid.Kind[y * grid.Cols + x] = kind?.Invoke(u, v) ?? GroundKinds.Grass;
            }
        return grid;
    }

    static double Bump(double u, double v, double cu, double cv, double tall, double wideU, double wideV) =>
        tall * Math.Exp(-((u - cu) * (u - cu) / (wideU * wideU) + (v - cv) * (v - cv) / (wideV * wideV)));

    static BuiltSite Site(ReliefGrid grid, int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Manner = manner };

    static BuiltThing Lay(BuiltSite site, int seed, double radius = Reach, double houses = 0.75) =>
        Settlements.Lay(site, "s", Middle, radius, seed, houses, 0, Plans.Value(Plans.Offered(site.Manner)[0]));

    static Spot Mid(BuiltPart part)
    {
        var at = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        return new Spot(at.Average(p => p.X), at.Average(p => p.Y));
    }

    static List<Spot> Houses(BuiltThing town) => town.Parts.Where(p => p.Kind == PartKinds.Block && p.Class is "house" or "cottage" or "townhouse" or "shop").Select(Mid).ToList();

    [Fact]
    public void A_hill_village_holds_its_church_square_or_castle_on_the_top()
    {
        var hill = Land((u, v) => Bump(u, v, 32, 30, 70, 7, 7));
        var site = Site(hill, Manners.Gothic);
        var top = new Spot(32, 30);
        foreach (int seed in Enumerable.Range(1, 4))
        {
            var town = Lay(site, seed);
            var crown = town.Parts.Where(p => p.Kind == PartKinds.Area && p.Class is "square" or Forts.Platform).Select(Mid).ToList();
            Assert.True(crown.Count > 0, $"seed {seed}: no square or castle");
            Assert.True(crown.Min(p => (p - top).Length) * MetresToTheUnit < 90, $"seed {seed}: the square or castle stands {crown.Min(p => (p - top).Length) * MetresToTheUnit:0} m from the top");
        }
    }

    [Fact]
    public void In_an_alpine_valley_the_village_takes_the_sunny_side()
    {
        var valley = Land((_, v) => Math.Abs(v - 30) * MetresToTheUnit * 0.18);
        var site = Site(valley, Manners.Continental);
        int sunny = 0, shaded = 0;
        foreach (int seed in Enumerable.Range(1, 4))
            foreach (var p in Houses(Lay(site, seed)))
            {
                if (p.Y < 29) sunny++;
                if (p.Y > 31) shaded++;
            }
        Assert.True(sunny > shaded * 1.3, $"{sunny} houses on the sunny side, {shaded} on the shaded side");
    }

    [Fact]
    public void A_kabyle_village_keeps_to_the_ridge_crest_and_a_soviet_one_to_the_valley_floor()
    {
        var ridge = Land((u, v) => Bump(u, v, 30, 30, 60, 400, 5));
        var crest = Houses(Lay(Site(ridge, Manners.NorthAfrican), 2));
        Assert.True(crest.Average(p => Math.Abs(p.Y - 30)) < 3.5, $"Kabyle houses lie {crest.Average(p => Math.Abs(p.Y - 30)) * MetresToTheUnit:0} m off the crest on average");
        var valley = Land((u, v) => 60 - Bump(u, v, 30, 30, 60, 400, 5));
        var floor = Houses(Lay(Site(valley, Manners.Soviet), 2));
        Assert.True(floor.Average(p => Math.Abs(p.Y - 30)) < 3.5, $"Soviet houses lie {floor.Average(p => Math.Abs(p.Y - 30)) * MetresToTheUnit:0} m off the valley floor on average");
    }

    [Fact]
    public void A_mongol_camp_pitches_its_tents_only_on_level_ground()
    {
        var rolling = Land((u, v) => Bump(u, v, 26, 30, 40, 4, 4) + Bump(u, v, 35, 33, 30, 3, 5));
        var site = Site(rolling, Manners.Mongol);
        var camp = Lay(site, 3);
        var tents = camp.Parts.Where(p => p.Kind == PartKinds.Block && p.Class == "roundhouse").Select(Mid).ToList();
        Assert.True(tents.Count > 10, $"{tents.Count} tents");
        Assert.All(tents, t => Assert.True(site.Downhill(t).Length <= 0.09, $"a tent on a {site.Downhill(t).Length:P0} slope"));
    }

    [Fact]
    public void An_old_town_on_the_floodplain_stands_on_its_tell_and_a_marsh_village_on_its_mound()
    {
        static byte River(double u, double v) => u < 24 ? GroundKinds.Water : GroundKinds.Grass;
        var plain = Land((u, _) => 10 + u * 0.02, River);
        var egyptian = Lay(Site(plain, Manners.Egyptian), 1);
        Assert.Contains(egyptian.Parts, p => p.Class == HillForms.Tell);
        var shaped = Earthworks.Shaped(plain, [egyptian], Side, Side, 1 / MetresToTheUnit, 1);
        Assert.True((shaped.HeightAt(0.5, 0.5) - plain.HeightAt(0.5, 0.5)) * MetresToTheUnit > 4, "the tell raises the town");
        var marsh = Land((_, _) => 2, (u, v) => u < 22 ? GroundKinds.Water : u < 25 ? GroundKinds.Marsh : GroundKinds.Grass);
        Assert.Contains(Lay(Site(marsh, Manners.Northern), 1).Parts, p => p.Class == HillForms.Terp);
        Assert.DoesNotContain(Lay(Site(plain, Manners.Continental), 1).Parts, p => p.Class is HillForms.Tell or HillForms.Terp);
    }

    [Fact]
    public void Houses_keep_off_the_floodplain_and_take_the_rise_above_it()
    {
        var river = Land((u, _) => u < 31 ? 10 + u * 0.03 : 10 + 31 * 0.03 + (u - 31) * MetresToTheUnit * 0.06, (u, _) => u < 24 ? GroundKinds.Water : GroundKinds.Grass);
        var site = Site(river, Manners.Continental);
        int low = 0, high = 0;
        foreach (int seed in Enumerable.Range(1, 3))
            foreach (var p in Houses(Lay(site, seed)))
            {
                if (p.X < 30) low++;
                else high++;
            }
        Assert.True(high > low * 2, $"{low} houses on the floodplain, {high} above it");
    }
}
