using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ReliefRateTests
{
    const int Side = 65;
    const double Total = 0.01;

    static float Centre(ReliefGrid g) => g.H[Side / 2 * Side + Side / 2];

    static ReliefGrid Held(double radiusCells, BrushMode mode, double? limit = null, float from = 0, int ticks = 1)
    {
        var g = new ReliefGrid(Side, Side);
        Array.Fill(g.H, from);
        var at = (0.5, 0.5);
        for (int i = 0; i < ticks; i++)
            ReliefBrush.Stroke(g, [at, at], radiusCells, new Brush(mode, 0, 1, Rise: Total / ReliefBrush.Dabs(g, at, at, radiusCells), Limit: limit));
        return g;
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    [InlineData(24)]
    public void A_held_brush_adds_its_share_at_the_centre_whatever_its_size(double radiusCells) =>
        Assert.Equal(Total, Centre(Held(radiusCells, BrushMode.Raise)), 6);

    [Fact]
    public void A_stroke_spreads_the_same_share_thinner_than_a_held_brush()
    {
        var g = new ReliefGrid(Side, Side);
        (double, double) from = (0.2, 0.5), to = (0.8, 0.5);
        ReliefBrush.Stroke(g, [from, to], 4, new Brush(BrushMode.Raise, 0, 1, Rise: Total / ReliefBrush.Dabs(g, from, to, 4)));
        Assert.InRange(Centre(g), Total / 100, Total / 2);
    }

    [Fact]
    public void Raising_stops_at_its_ceiling_and_leaves_higher_ground_alone()
    {
        var g = Held(6, BrushMode.Raise, limit: 0.025, ticks: 10);
        Assert.Equal(0.025f, Centre(g), 6);
        Assert.All(g.H, h => Assert.True(h <= 0.025f + 1e-6f));
        Assert.Equal(0.04f, Centre(Held(6, BrushMode.Raise, limit: 0.025, from: 0.04f, ticks: 3)), 6);
    }

    [Fact]
    public void Lowering_stops_at_its_floor_and_never_goes_under_the_table()
    {
        Assert.Equal(0.02f, Centre(Held(6, BrushMode.Lower, limit: 0.02, from: 0.05f, ticks: 10)), 6);
        Assert.Equal(0f, Centre(Held(6, BrushMode.Lower, from: 0.015f, ticks: 10)), 6);
        Assert.Equal(0.01f, Centre(Held(6, BrushMode.Lower, limit: 0.02, from: 0.01f, ticks: 3)), 6);
    }
}
