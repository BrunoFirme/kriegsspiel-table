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
}
