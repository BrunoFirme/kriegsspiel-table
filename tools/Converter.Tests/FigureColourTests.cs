using KTable.Converter.Bundles;

namespace KTable.Converter.Tests;

public sealed class FigureColourTests
{
    static ModelPart Triangle(double[]? colour) => new([0, 0, 0, 1, 0, 0, 0, 1, 0], [0, 0, 1, 0, 0, 1], [0, 1, 2], null, colour);

    [Fact]
    public void A_baked_figure_tints_only_its_first_part_and_every_other_part_keeps_its_own_material_colour()
    {
        var model = new BundleModel("sha", "Soldier", [Triangle([0.2, 0.3, 0.4]), Triangle([0.5, 0.6, 0.7])], [0, 0, 0], [1, 1, 0], 2, [], 0);
        var lines = ModelAtlas.Obj(model, new Dictionary<string, AtlasPlace>(), 1, 1).Split('\n');
        int own = Array.IndexOf(lines, "usemtl own");
        Assert.Equal("usemtl tinted", lines.First(l => l.StartsWith("usemtl", StringComparison.Ordinal)));
        Assert.All(lines.Take(own).Where(l => l.StartsWith("v ", StringComparison.Ordinal)), l => Assert.EndsWith(" 1 1 1", l));
        Assert.All(lines.Skip(own).Where(l => l.StartsWith("v ", StringComparison.Ordinal)), l => Assert.EndsWith(" 0.5 0.6 0.7", l));
    }
}
