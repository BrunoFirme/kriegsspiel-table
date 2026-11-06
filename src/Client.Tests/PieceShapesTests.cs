using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Tests;

public class PieceShapesTests
{
    [Theory]
    [InlineData("box", 4)]
    [InlineData("anything else", 4)]
    [InlineData("hex", 6)]
    [InlineData("circle", 48)]
    [InlineData("rounded", 28)]
    public void Each_shape_has_its_outline_and_none_leaves_its_box(string shape, int points)
    {
        var o = PieceShapes.Outline(shape, 4, 2);
        Assert.Equal(points, o.Count);
        Assert.All(o, p => Assert.True(MathF.Abs(p.X) <= 2.0001f && MathF.Abs(p.Y) <= 1.0001f, $"{p} is outside the box"));
        Assert.Contains(o, p => MathF.Abs(MathF.Abs(p.X) - 2) < 1e-4f);
    }

    [Theory]
    [InlineData("box", 1.9f, 0.9f, true)]
    [InlineData("box", 2.1f, 0f, false)]
    [InlineData("circle", 1.9f, 0.9f, false)]
    [InlineData("circle", 1.4f, 0.5f, true)]
    [InlineData("hex", 0f, 0f, true)]
    [InlineData("hex", 1.9f, 0.9f, false)]
    [InlineData("alpha", 1.9f, 0.9f, true)]
    [InlineData("alpha", 0f, 1.1f, false)]
    public void A_press_counts_where_the_shape_is(string shape, float x, float y, bool inside) =>
        Assert.Equal(inside, PieceShapes.Inside(shape, 4, 2, new Vector2(x, y)));
}
