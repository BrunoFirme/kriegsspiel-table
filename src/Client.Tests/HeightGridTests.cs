using System.Numerics;
using KTable.Client.Hand;

namespace KTable.Client.Tests;

public class HeightGridTests
{
    static HeightGrid Ramp() => HeightGrid.Of(Vector3.Zero, new Vector3(10, 2, 10), new[]
    {
        (new Vector3(0, 0, 0), new Vector3(10, 2, 0), new Vector3(10, 2, 10)),
        (new Vector3(0, 0, 0), new Vector3(10, 2, 10), new Vector3(0, 0, 10)),
    })!;

    [Fact]
    public void The_top_of_a_ramp_is_read_back_where_it_is_and_nowhere_else()
    {
        var g = Ramp();
        Assert.Equal(0f, g.At(0, 5)!.Value, 1);
        Assert.Equal(1f, g.At(5, 5)!.Value, 1);
        Assert.Equal(2f, g.At(10, 5)!.Value, 1);
        Assert.Null(g.At(20, 5));
        Assert.Null(g.At(5, -3));
    }

    [Fact]
    public void Where_two_surfaces_cover_a_point_the_higher_is_the_top()
    {
        var g = HeightGrid.Of(Vector3.Zero, new Vector3(4, 3, 4), new[]
        {
            (new Vector3(0, 0, 0), new Vector3(4, 0, 0), new Vector3(0, 0, 4)),
            (new Vector3(0, 3, 0), new Vector3(4, 3, 0), new Vector3(0, 3, 4)),
        })!;
        Assert.Equal(3f, g.At(1, 1));
    }

    [Fact]
    public void A_mesh_with_no_width_or_depth_has_no_top()
    {
        Assert.Null(HeightGrid.Of(Vector3.Zero, new Vector3(0, 2, 5), Array.Empty<(Vector3, Vector3, Vector3)>()));
        Assert.Null(HeightGrid.Of(Vector3.Zero, new Vector3(5, 2, 5), Array.Empty<(Vector3, Vector3, Vector3)>())!.At(2, 2));
    }
}
