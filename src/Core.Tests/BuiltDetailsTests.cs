using KTable.Core.Model;

namespace KTable.Core.Tests;

public class BuiltDetailsTests
{
    const double Side = 100, LargestBrush = 80, Slack = 1.5;

    static BuiltSite Site(double metresToTheUnit, int manner) =>
        new() { Width = Side, Height = Side, UnitsPerMetre = 1 / metresToTheUnit, Grid = ReliefGrid.Flat(64, 64), HeightUnit = Side, Others = [], Manner = manner };

    static List<Spot> Stroke() => Enumerable.Range(0, 40).Select(i => new Spot(Side * 0.1 + Side * 0.8 * i / 39, Side / 2)).ToList();

    [Fact]
    public void Country_over_a_wide_map_is_refused_before_any_of_it_is_laid()
    {
        foreach (int manner in new[] { Manners.Continental, Manners.Japanese })
        {
            Assert.True(BuiltDetails.TooMuch(Site(1000, manner), Stroke(), LargestBrush, 0.5, 0.25));
            Assert.True(BuiltDetails.TooMuch(Site(1000, manner), Stroke(), 8, 0.5, 0.25));
            Assert.False(BuiltDetails.TooMuch(Site(50, manner), Stroke(), 8, 0.5, 0.25));
        }
        Assert.True(BuiltDetails.TooMuch(Site(1000, Manners.Continental), Stroke(), LargestBrush, 0.5, 0, shares: true));
        Assert.False(BuiltDetails.TooMuch(Site(50, Manners.Continental), Stroke(), LargestBrush, 0.5, 0, shares: true));
        Assert.False(BuiltDetails.TooMuch(Site(1000, Manners.Continental), [], LargestBrush, 1, 1));
    }

    [Fact]
    public void Country_under_the_ceiling_lays_no_more_than_about_the_ceiling()
    {
        foreach (double metres in new[] { 50.0, 200 })
            foreach (double brush in new[] { 2.0, 8, 30 })
                foreach (int manner in new[] { Manners.Continental, Manners.Japanese })
                {
                    var site = Site(metres, manner);
                    if (BuiltDetails.TooMuch(site, Stroke(), brush, 1, 1)) continue;
                    int parts = BuiltDetails.Lay(site, "s", Stroke(), brush, 3, 1, 1).Parts.Count;
                    Assert.True(parts <= BuiltDetails.MostParts * Slack, $"{metres} m to the unit, brush {brush}, manner {manner}: {parts} parts");
                }
    }
}
