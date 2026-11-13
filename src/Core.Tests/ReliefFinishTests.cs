using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ReliefFinishTests
{
    const int Side = 64;

    static ReliefGrid Level(float height)
    {
        var g = new ReliefGrid(Side, Side);
        Array.Fill(g.H, height);
        return g;
    }

    [Fact]
    public void Lift_all_moves_every_height_and_stops_at_the_table()
    {
        var g = Level(0.01f);
        Assert.False(ReliefFinish.Lift(g, 0.02).Empty);
        Assert.All(g.H, h => Assert.Equal(0.03f, h, 6));
        ReliefFinish.Lift(g, -1);
        Assert.All(g.H, h => Assert.Equal(0f, h));
        Assert.True(ReliefFinish.Lift(g, 0).Empty);
    }

    [Fact]
    public void Roughen_stays_within_its_amount_leaves_water_level_and_repeats_for_a_seed()
    {
        var g = Level(0.05f);
        for (int i = 0; i < Side; i++) g.Kind[i] = GroundKinds.Water;
        var again = g.Copy();
        ReliefFinish.Roughen(g, 0.004, 8, 7);
        ReliefFinish.Roughen(again, 0.004, 8, 7);
        Assert.Equal(g.H, again.H);
        Assert.All(Enumerable.Range(0, Side), i => Assert.Equal(0.05f, g.H[i]));
        Assert.All(g.H, h => Assert.InRange(h, 0.046f - 1e-6f, 0.054f + 1e-6f));
        Assert.True(g.H.Max() - g.H.Min() > 0.002f, "the ground is uneven after it");
        Assert.InRange(g.H.Skip(Side).Average(), 0.049, 0.051);
        var other = Level(0.05f);
        ReliefFinish.Roughen(other, 0.004, 8, 8);
        Assert.NotEqual(g.H[Side * 30 + 30], other.H[Side * 30 + 30]);
    }

    [Fact]
    public void Erode_cuts_where_the_relief_gathers_water_and_leaves_the_slopes()
    {
        var g = new ReliefGrid(Side, Side);
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++) g.H[y * Side + x] = 0.3f * x / (Side - 1) + 0.2f * Math.Abs(y - Side / 2) / (Side / 2f);
        var before = g.Copy();
        var box = ReliefFinish.Erode(g, 0.002, 50, 1000);
        Assert.False(box.Empty);
        int floor = Side / 2 * Side + 10, slope = 4 * Side + 40;
        Assert.InRange(before.H[floor] - g.H[floor], 0.0015f, 0.0045f);
        Assert.Equal(before.H[slope], g.H[slope]);
        Assert.True(ReliefFinish.Erode(Level(0.01f), 0.002, 50, 1000).Empty, "level ground gathers no water");
    }
}
