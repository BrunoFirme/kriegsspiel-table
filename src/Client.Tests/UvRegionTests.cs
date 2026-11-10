using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class UvRegionTests
{
    const int Side = 64;

    sealed class Mesh
    {
        public readonly List<float> Pos = new(), Uv = new();

        public Mesh Quad(float[] a, float[] b, float[] c, float[] d, float u0, float v0, float u1, float v1)
        {
            Tri(a, b, c, (u0, v0), (u1, v0), (u1, v1));
            Tri(a, c, d, (u0, v0), (u1, v1), (u0, v1));
            return this;
        }

        void Tri(float[] a, float[] b, float[] c, (float u, float v) ta, (float u, float v) tb, (float u, float v) tc)
        {
            Pos.AddRange(a);
            Pos.AddRange(b);
            Pos.AddRange(c);
            Uv.AddRange(new[] { ta.u, ta.v, tb.u, tb.v, tc.u, tc.v });
        }

        public UvSurface Surface() => new(Pos, Uv, Side, Side);
    }

    static float[] P(float x, float y, float z) => new[] { x, y, z };

    static Mesh Cube()
    {
        var m = new Mesh();
        float s = 1 / 3f, h = 0.5f, g = 0.02f;
        (float u, float v) Cell(int i) => (i % 3 * s + g, i / 3 * h + g);
        void Face(int i, float[] a, float[] b, float[] c, float[] d)
        {
            var (u, v) = Cell(i);
            m.Quad(a, b, c, d, u, v, u + s - 2 * g, v + h - 2 * g);
        }
        Face(0, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1));
        Face(1, P(1, 0, 1), P(1, 0, 0), P(1, 1, 0), P(1, 1, 1));
        Face(2, P(1, 0, 0), P(0, 0, 0), P(0, 1, 0), P(1, 1, 0));
        Face(3, P(0, 0, 0), P(0, 0, 1), P(0, 1, 1), P(0, 1, 0));
        Face(4, P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), P(0, 1, 0));
        Face(5, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1));
        return m;
    }

    static byte[] Look(Func<int, int, (byte r, byte g, byte b)> at)
    {
        var px = new byte[Side * Side * 4];
        for (int y = 0; y < Side; y++)
            for (int x = 0; x < Side; x++)
            {
                var (r, g, b) = at(x, y);
                int o = (y * Side + x) * 4;
                (px[o], px[o + 1], px[o + 2], px[o + 3]) = (r, g, b, 255);
            }
        return px;
    }

    static bool Filled(byte[] paint, float u, float v) => paint[((int)(v * Side) * Side + (int)(u * Side)) * 4 + 3] == 255;

    static int Fill(UvSurface s, byte[] paint, byte[]? look, int tri, float match = 0.1f, float bend = 180) =>
        UvRegion.Fill(s, paint, look, new RegionSeed(tri, 1 / 3f, 1 / 3f), match, bend, 200, 30, 30, 255);

    [Fact]
    public void A_fill_takes_the_colour_clicked_and_stops_where_the_colour_changes()
    {
        var s = new Mesh().Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), 0, 0, 1, 1).Surface();
        var look = Look((x, _) => x < Side / 2 ? ((byte)120, (byte)120, (byte)120) : ((byte)40, (byte)40, (byte)140));
        var paint = new byte[Side * Side * 4];
        Assert.True(Fill(s, paint, look, 1) > 0);
        Assert.True(Filled(paint, 0.1f, 0.5f));
        Assert.False(Filled(paint, 0.9f, 0.5f));
    }

    [Fact]
    public void A_line_painted_across_a_plain_model_walls_the_fill_in()
    {
        var s = new Mesh().Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), 0, 0, 1, 1).Surface();
        var look = Look((_, _) => (128, 128, 128));
        var paint = new byte[Side * Side * 4];
        for (int y = 0; y < Side; y++) paint[(y * Side + Side / 2) * 4 + 3] = paint[(y * Side + Side / 2) * 4] = 255;
        Fill(s, paint, look, 1);
        Assert.True(Filled(paint, 0.1f, 0.2f) && Filled(paint, 0.1f, 0.9f));
        Assert.False(Filled(paint, 0.8f, 0.5f));
    }

    [Fact]
    public void A_fill_runs_over_the_seams_onto_every_face_of_a_plain_cube()
    {
        var s = Cube().Surface();
        var paint = new byte[Side * Side * 4];
        Fill(s, paint, Look((_, _) => (128, 128, 128)), 0);
        for (int face = 0; face < 6; face++) Assert.True(Filled(paint, face % 3 / 3f + 0.16f, face / 3 * 0.5f + 0.25f), $"face {face} is filled");
    }

    [Fact]
    public void Stopping_at_bends_keeps_the_fill_on_the_face_clicked()
    {
        var s = Cube().Surface();
        var paint = new byte[Side * Side * 4];
        Fill(s, paint, Look((_, _) => (128, 128, 128)), 0, bend: 45);
        Assert.True(Filled(paint, 0.16f, 0.25f));
        for (int face = 1; face < 6; face++) Assert.False(Filled(paint, face % 3 / 3f + 0.16f, face / 3 * 0.5f + 0.25f), $"face {face} is left alone");
    }

    [Fact]
    public void Pieces_apart_on_the_model_stay_apart_though_their_pictures_touch()
    {
        var s = new Mesh()
            .Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), 0, 0, 0.5f, 1)
            .Quad(P(5, 0, 0), P(6, 0, 0), P(6, 1, 0), P(5, 1, 0), 0.5f, 0, 1, 1).Surface();
        var paint = new byte[Side * Side * 4];
        Fill(s, paint, Look((_, _) => (128, 128, 128)), 1);
        Assert.True(Filled(paint, 0.2f, 0.5f));
        Assert.False(Filled(paint, 0.8f, 0.5f));
    }

    [Fact]
    public void Tolerance_lets_nearby_shades_in()
    {
        var s = new Mesh().Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), 0, 0, 1, 1).Surface();
        var shades = Look((x, _) => ((byte)(100 + x), (byte)(100 + x), (byte)(100 + x)));
        var tight = new byte[Side * Side * 4];
        var loose = new byte[Side * Side * 4];
        int few = Fill(s, tight, shades, 1, match: 0);
        int many = Fill(s, loose, shades, 1, match: 1);
        Assert.InRange(few, 1, Side * 2);
        Assert.Equal(Side * Side, many);
        Assert.Equal(0, UvRegion.Fill(s, tight, shades, new RegionSeed(99, 0, 0), 1, 180, 1, 1, 1, 255));
    }
}
