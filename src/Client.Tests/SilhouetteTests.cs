using KTable.Client.Geometry;

namespace KTable.Client.Tests;

public class SilhouetteTests
{
    const int N = 32;

    static bool[] Drawn(Func<int, int, bool> solid)
    {
        var cells = new bool[N * N];
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                cells[y * N + x] = solid(x, y);
        return cells;
    }

    static bool InBox(int x, int y, int from, int to) => x >= from && x <= to && y >= from && y <= to;

    static bool Frame(int x, int y) => InBox(x, y, 4, 27) && !InBox(x, y, 9, 22);

    [Fact]
    public void A_hollow_square_is_covered_from_its_middle_to_short_of_its_outer_edge()
    {
        var cover = Silhouette.HoleCover(Drawn(Frame), N)!;
        Assert.True(cover[16 * N + 16] && cover[9 * N + 9] && cover[22 * N + 22] && cover[6 * N + 16]);
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
                if (cover[y * N + x]) Assert.True(InBox(x, y, 5, 26), $"{x},{y} reaches the frame's outer edge");
    }

    [Fact]
    public void A_hairline_seam_from_the_hole_to_the_edge_is_covered_inside_the_frame()
    {
        var cover = Silhouette.HoleCover(Drawn((x, y) => Frame(x, y) && !(x == 20 && y > 22)), N);
        Assert.NotNull(cover);
        Assert.True(cover![16 * N + 16] && cover[24 * N + 20] && cover[26 * N + 20]);
        Assert.False(cover[30 * N + 20], "the seam outside the frame stays open");
    }

    [Fact]
    public void A_window_walled_off_by_the_pictures_last_row_is_a_hole()
    {
        var cover = Silhouette.HoleCover(Drawn((x, y) => x >= 10 && x <= 14 && y >= 26 && !(x >= 11 && x <= 13 && y >= 27 && y <= 30)), N);
        Assert.NotNull(cover);
        Assert.True(cover![28 * N + 12]);
    }

    [Fact]
    public void Separate_bars_a_solid_block_and_a_triangle_need_no_cover()
    {
        Assert.Null(Silhouette.HoleCover(Drawn((x, y) => x >= 4 && x <= 27 && (y is >= 2 and <= 8 or >= 13 and <= 19 or >= 24 and <= 30)), N));
        Assert.Null(Silhouette.HoleCover(Drawn((x, y) => InBox(x, y, 4, 27)), N));
        Assert.Null(Silhouette.HoleCover(Drawn((x, y) => y >= x), N));
    }

    [Fact]
    public void A_ring_of_dots_wider_apart_than_a_seam_is_not_closed_into_a_hole()
    {
        static bool Dot(int v) => v >= 1 && (v - 1) % 8 < 3;
        static bool Edge(int v) => v <= 3 || v >= 25;
        Assert.Null(Silhouette.HoleCover(Drawn((x, y) => Dot(x) && Dot(y) && (Edge(x) || Edge(y))), N));
    }
}
