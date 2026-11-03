using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class UvPaintTests
{
    static bool Painted(byte[] p, int w, int x, int y) => p[(y * w + x) * 4 + 3] != 0;

    [Fact]
    public void A_triangle_paints_the_texels_it_covers_and_only_those()
    {
        var paint = new byte[16 * 16 * 4];
        int n = UvPaint.Fill(paint, 16, 16, new[] { 0f, 0f, 0.5f, 0f, 0f, 0.5f }, new[] { 0 }, 10, 20, 30, 255);
        Assert.True(n > 20);
        Assert.True(Painted(paint, 16, 1, 1));
        Assert.False(Painted(paint, 16, 12, 12));
        Assert.Equal(new byte[] { 10, 20, 30, 255 }, paint.Skip((1 * 16 + 1) * 4).Take(4).ToArray());
    }

    [Fact]
    public void Only_the_chosen_triangles_are_painted_whichever_way_they_wind()
    {
        var paint = new byte[16 * 16 * 4];
        var uv = new[] { 0f, 0f, 0f, 0.4f, 0.4f, 0f, 1f, 1f, 0.6f, 1f, 1f, 0.6f };
        UvPaint.Fill(paint, 16, 16, uv, new[] { 1 }, 1, 1, 1, 255);
        Assert.False(Painted(paint, 16, 1, 1));
        Assert.True(Painted(paint, 16, 14, 14));
    }

    [Fact]
    public void Edges_reach_a_little_past_the_line_so_seams_do_not_show_and_flat_triangles_are_skipped()
    {
        var paint = new byte[8 * 8 * 4];
        UvPaint.Fill(paint, 8, 8, new[] { 0f, 0f, 1f, 0f, 0f, 1f }, new[] { 0 }, 1, 1, 1, 255);
        Assert.True(Painted(paint, 8, 7, 0));
        Assert.Equal(0, UvPaint.Fill(new byte[8 * 8 * 4], 8, 8, new[] { 0f, 0f, 0.5f, 0.5f, 1f, 1f }, new[] { 0 }, 1, 1, 1, 255));
        Assert.Equal(0, UvPaint.Fill(new byte[8 * 8 * 4], 8, 8, new[] { 0f, 0f }, new[] { 0 }, 1, 1, 1, 255));
    }

    [Fact]
    public void Colours_are_kept_premultiplied_as_the_paint_layer_holds_them()
    {
        Assert.Equal(((byte)128, (byte)0, (byte)0, (byte)128), UvPaint.Premultiplied(1, 0, 0, 0.5f));
    }
}
