using KTable.Converter.Bundles;

namespace KTable.Converter.Tests;

public sealed class ModelAtlasTests
{
    [Fact]
    public void A_triangle_past_its_texture_s_edge_keeps_its_corner_on_that_edge_rather_than_wrapping_to_the_other_side()
    {
        double[] verts = [0, 0, 0, 1, 0, 0, 0, 0, 1];
        double[] uvs = [1.5, 0, 2.0, 0, 1.5, 0.5];
        var model = new BundleModel("s", "Gun", [new ModelPart(verts, uvs, [0, 1, 2], "t")], [0, 0, 0], [1, 0, 1], 1, [], 0);
        var places = new Dictionary<string, AtlasPlace> { ["t"] = new AtlasPlace(0, 0, 10, 10) };

        var obj = ModelAtlas.Obj(model, places, 20, 10);

        var corners = obj.Split('\n').Where(l => l.StartsWith("vt ")).Select(l => l[3..]).ToList();
        Assert.Equal(["0.25 0", "0.5 0", "0.25 0.5"], corners);
    }

    const int AtlasWide = 40, AtlasHigh = 20;
    static readonly AtlasPlace Cell = new(10, 0, 10, 10);

    static List<(double X, double Z, double U, double V)[]> Faces(TextureTiling? tiling, double[] uvs, double[]? verts = null)
    {
        verts ??= [uvs[0], 0, uvs[1], uvs[2], 0, uvs[3], uvs[4], 0, uvs[5]];
        var model = new BundleModel("s", "Wall", [new ModelPart(verts, uvs, [0, 1, 2], "t", Tiling: tiling)], [0, 0, 0], [1, 0, 1], 1, [], 0);
        var lines = ModelAtlas.Obj(model, new Dictionary<string, AtlasPlace> { ["t"] = Cell }, AtlasWide, AtlasHigh).Split('\n');
        double[] Numbers(string line) => line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(n => double.Parse(n, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var points = lines.Where(l => l.StartsWith("v ")).Select(Numbers).ToList();
        var corners = lines.Where(l => l.StartsWith("vt ")).Select(Numbers).ToList();
        (double X, double Z, double U, double V) Corner(int[] c) =>
            (points[c[0] - 1][0], -points[c[0] - 1][2], (corners[c[1] - 1][0] * AtlasWide - Cell.X) / Cell.Width, (corners[c[1] - 1][1] * AtlasHigh - Cell.Y) / Cell.Height);
        return lines.Where(l => l.StartsWith("f ")).Select(l => l.Split(' ').Skip(1).Select(c => Corner(c.Split('/').Select(int.Parse).ToArray())).ToArray()).ToList();
    }

    static double Area((double X, double Z, double U, double V)[] f) =>
        Math.Abs((f[1].X - f[0].X) * (f[2].Z - f[0].Z) - (f[1].Z - f[0].Z) * (f[2].X - f[0].X)) / 2;

    [Fact]
    public void A_face_its_texture_repeats_across_is_cut_at_every_repeat_so_each_piece_shows_one_whole_copy_instead_of_one_smeared_copy()
    {
        var faces = Faces(null, [0, 0, 2.5, 0, 0, 1.5]);

        Assert.True(faces.Count >= 4, $"{faces.Count} pieces");
        Assert.Equal(2.5 * 1.5 / 2, faces.Sum(Area), 4);
        foreach (var face in faces)
        {
            double tileU = Math.Floor(face.Min(c => c.X) + 1e-6), tileV = Math.Floor(face.Min(c => c.Z) + 1e-6);
            foreach (var c in face)
            {
                Assert.Equal(c.X - tileU, c.U, 4);
                Assert.Equal(c.Z - tileV, c.V, 4);
            }
        }
    }

    [Fact]
    public void A_cut_corner_takes_its_place_and_its_light_from_between_the_corners_it_was_cut_from()
    {
        double[] verts = [0, 0, 0, 2, 4, 0, 0, 0, 2];
        double[] normals = [0, 1, 0, 1, 0, 0, 0, 1, 0];
        var model = new BundleModel("s", "Roof", [new ModelPart(verts, [0, 0, 2, 0, 0, 0.5], [0, 1, 2], "t", Normals: normals)], [0, 0, 0], [2, 4, 2], 1, [], 0);
        var lines = ModelAtlas.Obj(model, new Dictionary<string, AtlasPlace> { ["t"] = Cell }, AtlasWide, AtlasHigh).Split('\n');

        Assert.Contains("v 1 2 0 1 1 1", lines);
        var half = Math.Sqrt(0.5).ToString("G6", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains($"vn {half} {half} 0", lines);
        Assert.Equal(lines.Count(l => l.StartsWith("v ")), lines.Count(l => l.StartsWith("vn ")));
    }

    [Fact]
    public void A_mirrored_texture_runs_backwards_on_every_second_repeat()
    {
        var faces = Faces(TextureTiling.Plain with { WrapU = Wrap.Mirror }, [1.2, 0, 1.8, 0, 1.2, 0.5]);

        var corner = Assert.Single(faces).Select(c => (c.X, c.U)).OrderBy(c => c.X).ToArray();
        Assert.Equal(0.8, corner[0].U, 4);
        Assert.Equal(0.2, corner[^1].U, 4);
    }

    [Fact]
    public void A_clamped_texture_stretches_its_edge_past_its_end_rather_than_repeating()
    {
        var faces = Faces(TextureTiling.Plain with { WrapU = Wrap.Clamp }, [0, 0, 3, 0, 0, 0.5]);

        Assert.Equal(3 * 0.5 / 2, faces.Sum(Area), 4);
        foreach (var c in faces.SelectMany(f => f))
            Assert.Equal(Math.Min(c.X, 1), c.U, 4);
    }

    [Fact]
    public void The_material_s_tiling_and_offset_set_how_often_and_where_the_texture_lies_on_the_face()
    {
        var tiling = TextureTiling.Plain with { ScaleU = 2, OffsetU = 0.25, ScaleV = 0.5 };
        var faces = Faces(tiling, [0, 0, 1, 0, 0, 1], [0, 0, 0, 1, 0, 0, 0, 0, 1]);

        Assert.Equal(0.5, faces.Sum(Area), 4);
        foreach (var face in faces)
        {
            double tile = Math.Floor(face.Min(c => c.X * 2 + 0.25) + 1e-6);
            foreach (var c in face)
            {
                Assert.Equal(c.X * 2 + 0.25 - tile, c.U, 4);
                Assert.Equal(c.Z * 0.5, c.V, 4);
            }
        }
    }
}
