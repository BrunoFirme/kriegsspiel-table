using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GroundRoadsTests
{
    const int Side = 96;
    static readonly byte Field = GroundKinds.Of("field")!.Value;

    static (byte[] Kinds, byte[] Detail) Ragged(int centre, byte kind = GroundKinds.Road)
    {
        int n = Side * Side;
        var kinds = new byte[n];
        var detail = new byte[n];
        Array.Fill(kinds, Field);
        var rng = new Random(7);
        for (int x = 0; x < Side; x++)
        {
            int half = 2 + rng.Next(0, 2);
            for (int y = centre - half; y <= centre + half; y++)
            {
                kinds[y * Side + x] = kind;
                detail[y * Side + x] = GroundKinds.Levels(100, 0);
            }
        }
        return (kinds, detail);
    }

    static (double Angle, double Offset) Line(byte[] axes, int x, int y)
    {
        int i = (y * Side + x) * GroundRoads.Channels;
        return (axes[i] / 255.0 * Math.PI, (axes[i + 1] - 128) / GroundRoads.OffsetScale);
    }

    [Fact]
    public void A_ragged_painted_road_yields_one_straight_centreline()
    {
        const int centre = 48;
        var (kinds, detail) = Ragged(centre);
        var axes = GroundRoads.Axes(Side, Side, kinds, detail);
        for (int x = 15; x < Side - 15; x += 7)
            for (int y = centre - 4; y <= centre + 4; y++)
            {
                var (angle, offset) = Line(axes, x, y);
                Assert.True(Math.Min(angle, Math.PI - angle) < 0.08, $"axis at {x},{y} turned {angle:0.000} rad");
                double lineY = y + 0.5 + offset * Math.Cos(angle);
                Assert.True(Math.Abs(lineY - (centre + 0.5)) < 0.6, $"centreline at {x},{y} sits at {lineY:0.00}, not {centre + 0.5}");
            }
    }

    [Fact]
    public void Cells_far_from_any_road_carry_no_axis()
    {
        var (kinds, detail) = Ragged(48);
        var axes = GroundRoads.Axes(Side, Side, kinds, detail);
        Assert.Equal(0, axes[(5 * Side + 40) * GroundRoads.Channels + 3]);
    }

    [Fact]
    public void A_bridge_keeps_its_flag_and_its_class()
    {
        var (kinds, detail) = Ragged(48, GroundKinds.Bridge);
        var axes = GroundRoads.Axes(Side, Side, kinds, detail);
        int i = (48 * Side + 48) * GroundRoads.Channels;
        Assert.Equal(GroundRoads.Present | GroundRoads.BridgeFlag, axes[i + 3]);
        Assert.Equal(255, axes[i + 2]);
    }

    [Fact]
    public void Axes_derived_in_a_window_match_the_whole_grid_around_the_box()
    {
        var (kinds, detail) = Ragged(48);
        var whole = GroundRoads.Axes(Side, Side, kinds, detail);
        var box = new CellBox(40, 40, 50, 50);
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++) kinds[y * Side + x] = GroundKinds.Road;
        var expected = GroundRoads.Axes(Side, Side, kinds, detail);
        GroundRoads.Axes(Side, Side, kinds, detail, box, whole);
        Assert.Equal(expected, whole);
    }
}
