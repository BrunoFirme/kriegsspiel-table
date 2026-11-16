using KTable.Core.Model;

namespace KTable.Core.Tests;

public class LandformTests
{
    const double Side = 60, MetresToTheUnit = 25, Reach = 8;
    static readonly Spot Middle = new(30, 30);

    static double M(double metres) => metres / MetresToTheUnit;

    static BuiltSite Site(Func<double, double, double> metres, Func<double, double, byte>? kind = null)
    {
        var grid = ReliefGrid.Flat(Side, Side);
        for (int y = 0; y < grid.Rows; y++)
            for (int x = 0; x < grid.Cols; x++)
            {
                double u = x / (double)(grid.Cols - 1) * Side, v = y / (double)(grid.Rows - 1) * Side;
                grid.H[y * grid.Cols + x] = (float)M(metres(u, v));
                grid.Kind[y * grid.Cols + x] = kind?.Invoke(u, v) ?? GroundKinds.Grass;
            }
        return new BuiltSite { Width = Side, Height = Side, UnitsPerMetre = 1 / MetresToTheUnit, Grid = grid, HeightUnit = 1, Manner = Manners.Continental };
    }

    static double Bump(double u, double v, double cu, double cv, double tall, double wideU, double wideV) =>
        tall * Math.Exp(-((u - cu) * (u - cu) / (wideU * wideU) + (v - cv) * (v - cv) / (wideV * wideV)));

    static Landform At(BuiltSite site, double u, double v) => Landforms.Of(site, Middle, Reach).At(new Spot(u, v));

    [Fact]
    public void Level_ground_is_a_plain_and_an_even_hillside_a_slope_that_names_its_grade_and_facing()
    {
        Assert.Equal(Landform.Plain, At(Site((_, _) => 50), 30, 30));
        var hill = Site((_, v) => v * MetresToTheUnit * 0.2);
        Assert.Equal(Landform.Slope, At(hill, 30, 30));
        Assert.Equal("Slope · 20% · faces north", Landforms.Of(hill, Middle, Reach).Named(Middle));
    }

    [Fact]
    public void A_lone_hill_reads_as_a_summit_on_top_and_a_slope_on_its_flank()
    {
        var hill = Site((u, v) => Bump(u, v, 30, 30, 80, 10, 10));
        Assert.Equal(Landform.Summit, At(hill, 30, 30));
        Assert.Equal(Landform.Slope, At(hill, 38, 30));
    }

    [Fact]
    public void A_long_crest_reads_as_a_ridge_and_a_nose_running_down_a_hillside_as_a_spur()
    {
        Assert.Equal(Landform.Ridge, At(Site((u, v) => Bump(u, v, 30, 30, 70, 400, 6)), 30, 30));
        var spur = Site((u, v) => (60 - u) * MetresToTheUnit * 0.12 + Bump(u, v, 30, 30, 25, 30, 3.5));
        Assert.Equal(Landform.Spur, At(spur, 30, 30));
    }

    [Fact]
    public void The_col_between_two_hills_reads_as_a_saddle()
    {
        var two = Site((u, v) => Bump(u, v, 22, 30, 70, 6, 6) + Bump(u, v, 38, 30, 70, 6, 6));
        Assert.Equal(Landform.Saddle, At(two, 30, 30));
    }

    [Fact]
    public void A_broad_valley_reads_as_its_floor_and_a_narrow_deep_one_as_a_gorge()
    {
        Assert.Equal(Landform.Floor, At(Site((u, v) => 60 - Bump(u, v, 30, 30, 50, 400, 8)), 30, 30));
        Assert.Equal(Landform.Gorge, At(Site((u, v) => 200 - Bump(u, v, 30, 30, 150, 400, 1.6)), 30, 30));
    }

    [Fact]
    public void A_step_in_the_land_has_its_rim_above_and_its_foot_below()
    {
        var scarp = Site((u, _) => 40 + 60 / (1 + Math.Exp((u - 30) * 1.6)));
        Assert.Equal(Landform.Rim, At(scarp, 27.5, 30));
        Assert.Equal(Landform.Foot, At(scarp, 32.5, 30));
    }

    [Fact]
    public void Low_flat_ground_by_a_river_is_floodplain_and_the_bench_above_its_bank_a_river_terrace()
    {
        var river = Site((u, _) => u < 36 ? 20 + u * 0.02 : 32, (u, _) => u < 26 ? GroundKinds.Water : GroundKinds.Grass);
        Assert.Equal(Landform.Floodplain, At(river, 27.5, 30));
        var bench = Site((u, _) => u < 30 ? 20 : u < 31.5 ? 20 + (u - 30) * 6 : 29, (u, _) => u < 27 ? GroundKinds.Water : GroundKinds.Grass);
        Assert.Equal(Landform.Terrace, At(bench, 33, 30));
    }

    [Fact]
    public void Ground_steeper_than_forty_five_degrees_is_cliff()
    {
        Assert.Equal(Landform.Cliff, At(Site((u, _) => u * MetresToTheUnit * 1.2), 30, 30));
    }
}
