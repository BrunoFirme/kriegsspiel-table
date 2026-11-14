using KTable.Core.Model;

namespace KTable.Core.Tests;

public class ReliefFormsTests
{
    const int Side = 65;
    const double Height = 0.02;

    static float At(ReliefGrid g, int x, int y) => g.H[y * Side + x];

    static double Uv(int cell) => cell / (double)(Side - 1);

    [Theory]
    [InlineData(ReliefForms.Round)]
    [InlineData(ReliefForms.Sharp)]
    [InlineData(ReliefForms.Flat)]
    public void A_hill_stands_its_height_at_the_summit_and_ends_at_its_foot(string profile)
    {
        var g = new ReliefGrid(Side, Side);
        var box = ReliefForms.Hill(g, (0.5, 0.5), (Uv(32 + 12), 0.5), Height, profile);
        Assert.False(box.Empty);
        Assert.Equal(Height, At(g, 32, 32), 6);
        Assert.Equal(0f, At(g, 32 + 13, 32), 6);
        Assert.True(At(g, 32 + 6, 32) > 0 && At(g, 32 + 6, 32) < Height);
        Assert.Equal(At(g, 32 + 6, 32), At(g, 32, 32 - 6), 5);
    }

    [Fact]
    public void A_flat_hill_is_level_across_its_top_and_a_hollow_never_goes_under_the_table()
    {
        var g = new ReliefGrid(Side, Side);
        ReliefForms.Hill(g, (0.5, 0.5), (Uv(32 + 12), 0.5), Height, ReliefForms.Flat);
        Assert.Equal(Height, At(g, 32 + 5, 32), 6);
        ReliefForms.Hill(g, (0.5, 0.5), (Uv(32 + 12), 0.5), -1, ReliefForms.Round);
        Assert.Equal(0f, At(g, 32, 32), 6);
    }

    [Fact]
    public void A_ridge_keeps_its_height_along_a_bent_crest_and_a_valley_cuts_as_deep()
    {
        var g = new ReliefGrid(Side, Side);
        Array.Fill(g.H, 0.05f);
        (double, double)[] line = [(Uv(10), Uv(20)), (Uv(32), Uv(20)), (Uv(32), Uv(50))];
        ReliefForms.Ridge(g, line, 5, Height, ReliefForms.Round);
        Assert.Equal(0.05 + Height, At(g, 20, 20), 6);
        Assert.Equal(0.05 + Height, At(g, 32, 20), 6);
        Assert.Equal(0.05 + Height, At(g, 32, 40), 6);
        Assert.Equal(0.05f, At(g, 20, 26), 6);
        ReliefForms.Ridge(g, line, 5, -2 * Height, ReliefForms.Round);
        Assert.Equal(0.05 - Height, At(g, 32, 40), 6);
    }

    [Fact]
    public void A_slope_is_one_even_grade_between_its_ends()
    {
        var g = new ReliefGrid(Side, Side);
        for (int y = 0; y < Side; y++)
            for (int x = 40; x < Side; x++) g.H[y * Side + x] = 0.04f;
        ReliefForms.Slope(g, (Uv(48), 0.5), (Uv(16), 0.5), 6);
        Assert.Equal(0.02, At(g, 32, 32), 4);
        Assert.Equal(0.03, At(g, 40, 32), 4);
        Assert.Equal(0.01, At(g, 24, 32), 4);
        Assert.Equal(0f, At(g, 24, 32 + 7), 6);
    }

    [Fact]
    public void A_slope_with_a_rise_grades_flat_ground_from_its_top_down_to_its_foot()
    {
        var g = new ReliefGrid(Side, Side);
        ReliefForms.Slope(g, (Uv(16), 0.5), (Uv(48), 0.5), 6);
        Assert.True(g.H.All(h => h == 0), "as it lies, flat ground stays flat");
        var box = ReliefForms.Slope(g, (Uv(16), 0.5), (Uv(48), 0.5), 6, Height);
        Assert.False(box.Empty);
        Assert.Equal(Height, At(g, 16, 32), 4);
        Assert.Equal(0f, At(g, 48, 32), 4);
        Assert.Equal(Height / 2, At(g, 32, 32), 3);
        Assert.True(At(g, 24, 32) > At(g, 32, 32) && At(g, 32, 32) > At(g, 40, 32), "one even grade between them");
        Assert.Equal(0f, At(g, 32, 32 + 7), 6);
    }

    [Fact]
    public void A_slope_rises_its_height_above_its_foot_on_ground_that_already_stands()
    {
        var g = new ReliefGrid(Side, Side);
        Array.Fill(g.H, 0.01f);
        ReliefForms.Slope(g, (Uv(16), 0.5), (Uv(48), 0.5), 6, Height);
        Assert.Equal(0.01 + Height, At(g, 16, 32), 4);
        Assert.Equal(0.01, At(g, 48, 32), 4);
    }
}
