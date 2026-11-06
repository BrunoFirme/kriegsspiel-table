using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GroundNetworkTests
{
    const int Side = 256;
    const double CellMetres = 10;

    static readonly byte Field = GroundKinds.Of("field")!.Value;

    static (byte[] Kinds, byte[] Salt, byte[] Detail, float[] Heights) Farmland(int buildings)
    {
        int n = Side * Side;
        var kinds = new byte[n];
        var salt = new byte[n];
        var detail = new byte[n];
        Array.Fill(kinds, Field);
        Array.Fill(detail, GroundKinds.Levels(50, buildings));
        return (kinds, salt, detail, new float[n]);
    }

    static void Column(byte[] kinds, int x, int width, byte kind)
    {
        for (int y = 0; y < Side; y++)
            for (int dx = 0; dx < width; dx++) kinds[y * Side + x + dx] = kind;
    }

    static GroundSettlement Plan((byte[] Kinds, byte[] Salt, byte[] Detail, float[] Heights) g) =>
        GroundNetwork.Plan(Side, Side, g.Kinds, g.Salt, g.Detail, g.Heights, CellMetres, 1);

    static int CellsWith(GroundSettlement s, int channel, Func<byte, bool> test)
    {
        int count = 0;
        for (int i = 0; i < Side * Side; i++) if (test(s.Lines[i * GroundNetwork.Channels + channel])) count++;
        return count;
    }

    [Fact]
    public void Farms_on_farmland_with_buildings_get_lanes_to_the_road()
    {
        var g = Farmland(100);
        Column(g.Kinds, Side / 2, 2, GroundKinds.Road);
        var s = Plan(g);
        Assert.True(s.Farms > 10, $"only {s.Farms} farms");
        Assert.True(s.Lanes > 0, "no lanes were laid");
        Assert.True(CellsWith(s, 2, b => b != 0) > 0, "no lane reached the line texture");
    }

    [Fact]
    public void Farms_close_together_share_a_lane_instead_of_each_running_alone()
    {
        var g = Farmland(100);
        Column(g.Kinds, 2, 2, GroundKinds.Road);
        var s = Plan(g);
        Assert.True(CellsWith(s, 2, b => b == GroundNetwork.Lane) > 0, "no lane carries more than one farm");
    }

    [Fact]
    public void No_buildings_means_no_farms_and_no_lanes()
    {
        var g = Farmland(0);
        Column(g.Kinds, Side / 2, 2, GroundKinds.Road);
        var s = Plan(g);
        Assert.Equal(0, s.Farms);
        Assert.Equal(0, CellsWith(s, 3, b => b != 0));
    }

    [Fact]
    public void A_lane_never_crosses_water_to_reach_a_road()
    {
        var g = Farmland(100);
        Column(g.Kinds, 20, 2, GroundKinds.Road);
        Column(g.Kinds, 60, 6, GroundKinds.Water);
        var s = Plan(g);
        for (int y = 0; y < Side; y++)
            for (int x = 61; x < 65; x++)
                Assert.Equal(0, s.Lines[(y * Side + x) * GroundNetwork.Channels + 2]);
    }

    [Fact]
    public void The_same_ground_plans_the_same_settlement()
    {
        var g = Farmland(60);
        Column(g.Kinds, Side / 3, 2, GroundKinds.Road);
        var a = Plan(g);
        var b = Plan(g);
        Assert.Equal(a.Lines, b.Lines);
        Assert.Equal(a.Sites, b.Sites);
    }

    [Fact]
    public void A_farm_marks_the_cells_round_it_with_its_site_and_its_turn()
    {
        var g = Farmland(100);
        Column(g.Kinds, Side / 2, 2, GroundKinds.Road);
        var s = Plan(g);
        int marked = 0;
        for (int i = 0; i < Side * Side; i++) if (s.Sites[i * GroundNetwork.Channels + 3] != 0) marked++;
        Assert.True(marked > s.Farms, "farm sites were not spread to the cells round them");
    }
}
