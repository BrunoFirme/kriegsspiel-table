using System.Globalization;
using KTable.Converter.Bundles;
using KTable.Core.Model;

namespace KTable.Converter.Tests;

public sealed class FigureSurfaceTests
{
    static List<string[]> Lines(string obj) =>
        obj.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();

    static (double X, double Y, double Z) Normal(List<string[]> lines, string corner)
    {
        var normals = lines.Where(p => p[0] == "vn").ToList();
        var n = normals[int.Parse(corner.Split('/')[2], CultureInfo.InvariantCulture) - 1];
        return (double.Parse(n[1], CultureInfo.InvariantCulture), double.Parse(n[2], CultureInfo.InvariantCulture), double.Parse(n[3], CultureInfo.InvariantCulture));
    }

    static List<string[]> Faces(List<string[]> lines) => lines.Where(p => p[0] == "f").ToList();

    [Fact]
    public void A_module_model_keeps_one_normal_per_point_and_texture_point_the_last_one_its_faces_name_as_the_game_does()
    {
        var placed = Lines(ModuleMeshes.Placed(
        [
            "v 0 0 0", "v 1 0 0", "v 0 1 0", "v 1 1 0", "vt 0 0", "vn 0 0 1", "vn 1 0 0",
            "f 1/1/1 2/1/1 3/1/1", "f 2/1/2 4/1/2 3/1/2",
        ], 1));

        var faces = Faces(placed);
        Assert.Equal(Normal(placed, faces[0][2]), Normal(placed, faces[1][1]));
        Assert.Equal((-1, 0, 0), Normal(placed, faces[0][2]));
        Assert.Equal((0, 0, -1), Normal(placed, faces[0][1]));
    }

    [Fact]
    public void A_module_model_without_normals_is_smoothed_across_shared_texture_points_and_broken_at_a_texture_seam()
    {
        string[] Model(string far) =>
        [
            "v 0 0 0", "v 1 0 0", "v 0 1 0", "v 0 0 1", "vt 0 0", "vt 1 0",
            "f 1/1 2/1 3/1", $"f 1/1 4/{far} 2/1",
        ];
        var smooth = Lines(ModuleMeshes.Placed(Model("1"), 1));
        var seam = Lines(ModuleMeshes.Placed(["v 0 0 0", "v 1 0 0", "v 0 1 0", "v 0 0 1", "vt 0 0", "vt 1 0", "f 1/1 2/1 3/1", "f 1/2 4/2 2/2"], 1));

        var shared = Normal(smooth, Faces(smooth)[0][1]);
        Assert.Equal(shared, Normal(smooth, Faces(smooth)[1][1]));
        Assert.Equal(Math.Sqrt(0.5), shared.Y, 9);
        Assert.Equal(-Math.Sqrt(0.5), shared.Z, 9);
        Assert.NotEqual(Normal(seam, Faces(seam)[0][1]), Normal(seam, Faces(seam)[1][1]));
        Assert.Equal((0, 0, -1), Normal(seam, Faces(seam)[0][1]));
    }

    [Fact]
    public void A_module_model_is_drawn_one_sided_and_its_normals_lean_with_its_stretch()
    {
        var obj = ModuleMeshes.Placed(["v 0 0 0", "v 1 0 0", "v 0 1 0", "vn 1 1 0", "f 1//1 2//1 3//1"], 2);

        Assert.StartsWith("cull back\nalpha cut 0.5\n", obj);
        var n = Normal(Lines(obj), Faces(Lines(obj))[0][1]);
        Assert.Equal(-2 / Math.Sqrt(5), n.X, 9);
        Assert.Equal(1 / Math.Sqrt(5), n.Y, 9);
    }

    [Fact]
    public void A_module_model_face_named_by_counting_back_reads_the_same_points_as_counting_forward()
    {
        var back = Lines(ModuleMeshes.Placed(["v 0 0 0", "v 1 0 0", "v 0 1 0", "f -3 -2 -1"], 1));
        var forward = Lines(ModuleMeshes.Placed(["v 0 0 0", "v 1 0 0", "v 0 1 0", "f 1 2 3"], 1));

        Assert.Equal(Faces(forward)[0], Faces(back)[0]);
    }

    public static TheoryData<int, SurfaceAlpha> StandardModes => new()
    {
        { 0, SurfaceAlpha.Opaque }, { 1, SurfaceAlpha.Cut }, { 2, SurfaceAlpha.Blend }, { 3, SurfaceAlpha.Blend },
    };

    [Theory]
    [MemberData(nameof(StandardModes))]
    public void A_standard_material_is_drawn_by_its_rendering_mode_one_sided_as_the_game_sets_it(int mode, SurfaceAlpha alpha)
    {
        var draw = MaterialDraw.Drawn(MaterialDraw.Builtin[46], new Dictionary<string, double> { ["_Mode"] = mode, ["_Cutoff"] = 0.3 });

        Assert.Equal(new SurfaceDraw(true, alpha, 0.3), draw);
    }

    [Fact]
    public void A_material_s_built_in_shader_decides_its_sides_and_its_see_through_whatever_rendering_mode_it_still_carries()
    {
        var stale = new Dictionary<string, double> { ["_Mode"] = 1 };

        Assert.Equal(new SurfaceDraw(true, SurfaceAlpha.Opaque, SurfaceDraw.HalfCut), MaterialDraw.Drawn(MaterialDraw.Builtin[7], stale));
        Assert.Equal(new SurfaceDraw(false, SurfaceAlpha.Blend, SurfaceDraw.HalfCut), MaterialDraw.Drawn(MaterialDraw.Builtin[203], stale));
        Assert.Equal(new SurfaceDraw(false, SurfaceAlpha.Blend, SurfaceDraw.HalfCut), MaterialDraw.Drawn(MaterialDraw.Builtin[10760], stale));
        Assert.Equal(new SurfaceDraw(true, SurfaceAlpha.Cut, SurfaceDraw.HalfCut), MaterialDraw.Drawn(MaterialDraw.Builtin[10751], stale));
    }

    [Fact]
    public void A_shader_carried_in_the_bundle_is_read_for_its_culling_its_blending_and_its_cutout_tags()
    {
        Assert.Equal(new ShaderFacts(true, SurfaceAlpha.Blend), MaterialDraw.FromState(0, 5, 10, "Transparent", "Transparent", hasMode: false));
        Assert.Equal(new ShaderFacts(false, SurfaceAlpha.Cut), MaterialDraw.FromState(2, 1, 0, "TransparentCutout", "", hasMode: false));
        Assert.Equal(new ShaderFacts(false, SurfaceAlpha.Opaque), MaterialDraw.FromState(2, 1, 0, "Opaque", "", hasMode: false));
        Assert.Equal(MaterialDraw.Standard, MaterialDraw.FromState(2, 1, 0, "Opaque", "", hasMode: true));
    }

    [Fact]
    public void A_bundle_figure_s_file_carries_its_own_normals_mirrored_like_its_points_and_each_part_s_draw()
    {
        double[] verts = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        double[] normals = [0, 0, 1, 0, 0, 1, 0, 0, 1];
        var flag = new SurfaceDraw(false, SurfaceAlpha.Blend, SurfaceDraw.HalfCut);
        var model = new BundleModel("s", "Guidon", [
            new ModelPart(verts, [0, 0, 1, 0, 0, 1], [0, 1, 2], null, Normals: normals),
            new ModelPart(verts, [0, 0, 1, 0, 0, 1], [0, 1, 2], null, Normals: normals, Draw: flag),
        ], [0, 0, 0], [1, 1, 0], 2, [], 0);

        var obj = ModelAtlas.Obj(model, new Dictionary<string, AtlasPlace>(), 4, 4);
        var lines = Lines(obj);

        Assert.Contains("cull back\nalpha opaque\nusemtl tinted\n", obj);
        Assert.Contains("cull none\nalpha blend\nusemtl own\n", obj);
        Assert.All(Faces(lines).SelectMany(f => f.Skip(1)), c => Assert.Equal((0, 0, -1), Normal(lines, c)));
        Assert.Equal(["f", "6/6/6", "5/5/5", "4/4/4"], Faces(lines)[1]);
        Assert.All(Faces(lines), f => Assert.True(Dot(Winding(lines, f), Normal(lines, f[1])) > 0, $"face {string.Join(' ', f)} winds round its own normal"));
    }

    static (double X, double Y, double Z) Point(List<string[]> lines, string corner)
    {
        var v = lines.Where(p => p[0] == "v").ToList()[int.Parse(corner.Split('/')[0], CultureInfo.InvariantCulture) - 1];
        return (double.Parse(v[1], CultureInfo.InvariantCulture), double.Parse(v[2], CultureInfo.InvariantCulture), double.Parse(v[3], CultureInfo.InvariantCulture));
    }

    static (double X, double Y, double Z) Winding(List<string[]> lines, string[] face)
    {
        var (a, b, c) = (Point(lines, face[1]), Point(lines, face[2]), Point(lines, face[3]));
        (double X, double Y, double Z) u = (b.X - a.X, b.Y - a.Y, b.Z - a.Z), w = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
        return (u.Y * w.Z - u.Z * w.Y, u.Z * w.X - u.X * w.Z, u.X * w.Y - u.Y * w.X);
    }

    static double Dot((double X, double Y, double Z) a, (double X, double Y, double Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
}
