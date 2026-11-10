using System.Numerics;
using KTable.Client.Studio;

namespace KTable.Client.Tests;

public class ModelThumbTests
{
    static ModelMesh Cube()
    {
        var m = new ModelMesh { Materials = { new ModelMaterial(Rgba.White, null) } };
        var corners = new[]
        {
            new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 },
        };
        Vector3 C(int i) => new((i & 1) == 0 ? -0.5f : 0.5f, (i & 2) == 0 ? -0.5f : 0.5f, (i & 4) == 0 ? -0.5f : 0.5f);
        foreach (var q in corners)
            foreach (var k in new[] { 0, 1, 2, 0, 2, 3 })
            {
                m.Add(C(q[k]), Vector3.UnitY, new Vector2(0.25f, 0.25f), Rgba.White);
                if (m.V.Count % 3 == 0) m.Mat.Add(0);
            }
        return m;
    }

    static ModelPicture Plain(byte r, byte g, byte b, byte a) =>
        new(Enumerable.Range(0, 4).SelectMany(_ => new[] { r, g, b, a }).ToArray(), 2, 2);

    static (byte r, byte g, byte b, byte a) At(byte[] rgba, int x, int y)
    {
        int o = (y * ModelThumb.Out + x) * 4;
        return (rgba[o], rgba[o + 1], rgba[o + 2], rgba[o + 3]);
    }

    [Fact]
    public void A_model_s_picture_shows_its_painted_colour_in_the_middle_and_nothing_at_the_corners()
    {
        var rgba = ModelThumb.Render(Cube(), Plain(200, 40, 40, 255))!;
        Assert.Equal(ModelThumb.Out * ModelThumb.Out * 4, rgba.Length);
        var mid = At(rgba, ModelThumb.Out / 2, ModelThumb.Out / 2);
        Assert.Equal(255, mid.a);
        Assert.True(mid.r > 2 * mid.g && mid.r > 2 * mid.b);
        Assert.Equal(0, At(rgba, 0, 0).a);
        Assert.Equal(0, At(rgba, ModelThumb.Out - 1, ModelThumb.Out - 1).a);
    }

    [Fact]
    public void A_model_without_paint_is_drawn_in_its_material_colour()
    {
        var mesh = Cube();
        mesh.Materials[0] = new ModelMaterial(new Rgba(0.1f, 0.2f, 0.9f), null);
        var mid = At(ModelThumb.Render(mesh, null)!, ModelThumb.Out / 2, ModelThumb.Out / 2);
        Assert.Equal(255, mid.a);
        Assert.True(mid.b > 2 * mid.r);
    }

    [Fact]
    public void See_through_paint_and_empty_models_give_no_picture()
    {
        var clear = ModelThumb.Render(Cube(), Plain(9, 9, 9, 0))!;
        Assert.All(Enumerable.Range(0, ModelThumb.Out * ModelThumb.Out), i => Assert.Equal(0, clear[i * 4 + 3]));
        Assert.Null(ModelThumb.Render(new ModelMesh(), null));
    }
}
