using System.Numerics;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class HandleMathTests
{
    static readonly Vector2 Min = new(0, 0), Max = new(10, 20);

    [Fact]
    public void The_eight_handles_sit_on_the_corners_and_the_edge_middles_and_each_has_one_across_from_it()
    {
        var h = HandleMath.Points(Min, Max);
        Assert.Equal(new[] { new Vector2(0, 0), new(10, 0), new(10, 20), new(0, 20), new(5, 0), new(10, 10), new(5, 20), new(0, 10) }, h);
        Assert.Equal(new[] { 2, 3, 0, 1, 6, 7, 4, 5 }, Enumerable.Range(0, 8).Select(HandleMath.Opposite));
    }

    [Fact]
    public void A_corner_scales_both_ways_alike_unless_free_and_an_edge_its_own_way_only()
    {
        var h = HandleMath.Points(Min, Max);
        Assert.Equal((2f, 2f), HandleMath.Scale(2, h[0], h[2], new Vector2(20, 40), free: false));
        Assert.Equal((2f, 0.5f), HandleMath.Scale(2, h[0], h[2], new Vector2(20, 10), free: true));
        Assert.Equal((1f, 3f), HandleMath.Scale(6, h[4], h[6], new Vector2(99, 60), free: false));
        Assert.Equal((0.5f, 1f), HandleMath.Scale(5, h[7], h[5], new Vector2(5, -99), free: false));
    }

    [Fact]
    public void A_layer_scaled_to_nothing_or_past_its_anchor_keeps_a_hundredth_and_its_flip()
    {
        var h = HandleMath.Points(Min, Max);
        Assert.Equal((0.01f, 1f), HandleMath.Scale(5, h[7], h[5], new Vector2(0, 10), free: false));
        Assert.Equal((-0.5f, 1f), HandleMath.Scale(5, h[7], h[5], new Vector2(-5, 10), free: false));
        Assert.Equal((1f, 1f), HandleMath.Scale(2, Vector2.Zero, Vector2.Zero, new Vector2(5, 5), free: false));
    }

    [Fact]
    public void A_crop_stays_inside_the_image_two_pixels_across_and_on_whole_pixels()
    {
        var start = new float[] { 10, 10, 50, 50 };
        Assert.Equal(new float[] { 0, 10, 60, 50 }, HandleMath.Crop(7, start, new Vector2(-30, 99), 100, 100));
        Assert.Equal(new float[] { 10, 10, 2, 50 }, HandleMath.Crop(5, start, new Vector2(3, 0), 100, 100));
        Assert.Equal(new float[] { 10, 10, 90, 90 }, HandleMath.Crop(2, start, new Vector2(500, 500), 100, 100));
        Assert.Equal(new float[] { 12, 10, 48, 50 }, HandleMath.Crop(0, start, new Vector2(12.4f, 9.6f), 100, 100));
    }

    static (double, double, double, double) R((double x, double y, double w, double h) b) =>
        (Math.Round(b.x, 9), Math.Round(b.y, 9), Math.Round(b.w, 9), Math.Round(b.h, 9));

    [Fact]
    public void The_name_box_moves_whole_or_by_its_edges_and_never_leaves_the_face()
    {
        Assert.Equal((0.8, 0, 0.2, 0.1), R(HandleMath.Box(9, 0.1, 0.1, 0.2, 0.1, 0.9, -0.5)));
        Assert.Equal((0.1, 0.1, 0.02, 0.1), R(HandleMath.Box(5, 0.1, 0.1, 0.2, 0.1, -0.5, 0)));
        Assert.Equal((0, 0.1, 1, 0.1), R(HandleMath.Box(5, 0.1, 0.1, 0.2, 0.1, 5, 0)));
    }

    [Fact]
    public void A_press_finds_the_point_or_the_edge_it_is_near()
    {
        var square = new List<Vector2> { new(0, 0), new(10, 0), new(10, 10), new(0, 10) };
        Assert.Equal(2, HandleMath.NearestPoint(square, new Vector2(11, 9), 3));
        Assert.Equal(-1, HandleMath.NearestPoint(square, new Vector2(5, 5), 3));
        Assert.Equal(3, HandleMath.NearestEdge(square, new Vector2(1, 5), 3));
        Assert.Equal(-1, HandleMath.NearestEdge(square, new Vector2(5, 5), 3));
        var line = new List<Vector2> { new(0, 0), new(10, 0) };
        Assert.Equal(0, HandleMath.NearestEdge(line, new Vector2(5, 1), 3));
        Assert.Equal(new Vector2(10, 0), HandleMath.ClosestOnSegment(new Vector2(20, 5), line[0], line[1]));
    }

    [Fact]
    public void A_constrained_line_turns_to_the_nearest_step_and_keeps_its_length()
    {
        var b = HandleMath.Constrain(Vector2.Zero, new Vector2(10, 1));
        Assert.Equal(0, b.Y, 4);
        Assert.Equal(new Vector2(10, 1).Length(), b.X, 4);
        var diag = HandleMath.Constrain(Vector2.Zero, new Vector2(10, 9));
        Assert.Equal(diag.X, diag.Y, 4);
    }
}
