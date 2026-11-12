using KTable.Core.Model;
using Xunit;

namespace KTable.Core.Tests;

public class CalibrationTests
{
    static Calibration Cal(string units) => new() { UnitsPerMeter = 0.02, Units = units };

    static readonly GridDef Squares = new() { Kind = "square", Pitch = 10 };

    [Fact]
    public void Without_a_scale_a_length_stays_in_table_units()
    {
        Assert.Equal("7.50 u", new Calibration().Describe(7.5));
        Assert.Equal("7.50 u", new Calibration { UnitsPerMeter = 0, Units = "yd" }.Describe(7.5));
    }

    [Theory]
    [InlineData("m", 10, "500 m")]
    [InlineData("km", 10, "0.50 km")]
    [InlineData("yd", 10, "547 yd")]
    [InlineData("mi", 10, "0.31 mi")]
    [InlineData("pace", 10, "656 paces")]
    [InlineData("m", 20, "1,000 m")]
    [InlineData("km", 20, "1.00 km")]
    [InlineData("yd", 40, "2,187 yd")]
    [InlineData("mi", 40, "1.24 mi")]
    public void A_length_reads_in_the_one_measure_the_scenario_asks_for_short_or_long(string units, double tableUnits, string expected)
        => Assert.Equal(expected, Cal(units).Describe(tableUnits));

    [Fact]
    public void An_unknown_measure_falls_back_to_metres()
    {
        Assert.Equal("500 m", Cal("furlongs").Describe(10));
        Assert.Equal("Metres", Cal("furlongs").MeasureName);
        Assert.Equal("Kilometres", Cal("km").MeasureName);
    }

    [Fact]
    public void Grid_cells_count_the_grid_with_or_without_a_scale_and_fall_back_to_table_units_without_a_grid()
    {
        Assert.Equal("2.50 cells", Cal("grid").Describe(25, Squares));
        Assert.Equal("0.35 cells", new Calibration { Units = "grid" }.Describe(3.5, Squares));
        Assert.Equal("25.00 u", Cal("grid").Describe(25, new GridDef()));
        Assert.Equal("25.00 u", Cal("grid").Describe(25));
        Assert.Equal("6.00 × 4.00 cells", Cal("grid").DescribeSize(60, 40, Squares));
        Assert.Equal("500 m", Cal("m").Describe(10, Squares));
    }

    [Theory]
    [InlineData("m", 500, 10)]
    [InlineData("km", 500, 10)]
    [InlineData("grid", 500, 10)]
    [InlineData("yd", 547.0, 10.0)]
    [InlineData("mi", 547.0, 10.0)]
    [InlineData("pace", 656.0, 10.0)]
    public void A_distance_typed_in_the_measure_s_working_unit_becomes_table_units(string units, double typed, double tableUnits)
    {
        Assert.Equal(tableUnits, Cal(units).ToTableUnits(typed), 1);
        Assert.Equal(typed, Cal(units).FromTableUnits(tableUnits), 0);
    }

    [Fact]
    public void Without_a_scale_the_box_is_in_table_units_and_says_so()
    {
        var plain = new Calibration();
        Assert.Equal("u", plain.UnitName);
        Assert.Equal(7.5, plain.ToTableUnits(7.5));
        Assert.Equal(7.5, plain.FromTableUnits(7.5));
        Assert.Equal("m", Cal("m").UnitName);
        Assert.Equal("m", Cal("km").UnitName);
        Assert.Equal("yd", Cal("mi").UnitName);
        Assert.Equal("paces", Cal("pace").UnitName);
        Assert.Equal("m", plain.MeasuredIn);
    }

    [Theory]
    [InlineData("m", 1000, "a kilometre")]
    [InlineData("km", 1000, "a kilometre")]
    [InlineData("grid", 1000, "a kilometre")]
    [InlineData("yd", 1760, "a mile")]
    [InlineData("mi", 1760, "a mile")]
    [InlineData("pace", 1000, "a thousand paces")]
    [InlineData("furlongs", 1000, "a kilometre")]
    public void A_scale_bar_is_a_round_length_in_the_measure_s_working_unit(string units, double length, string words)
    {
        Assert.Equal((length, words), Cal(units).ScaleBar);
        Assert.Equal((length, words), new Calibration { Units = units }.ScaleBar);
    }

    [Theory]
    [InlineData("m", 0.02, "1,000 m")]
    [InlineData("km", 0.02, "1.00 km")]
    [InlineData("km", 0.0206, "1.00 km")]
    [InlineData("km", 0.3, "1.00 km")]
    [InlineData("km", 0.007, "1.00 km")]
    [InlineData("yd", 0.02, "1,760 yd")]
    [InlineData("mi", 0.02, "1.00 mi")]
    [InlineData("mi", 0.0206, "1.00 mi")]
    [InlineData("mi", 0.3, "1.00 mi")]
    [InlineData("mi", 0.007, "1.00 mi")]
    [InlineData("pace", 0.0206, "1,000 paces")]
    public void A_scale_bar_reads_as_itself(string units, double upm, string expected)
    {
        var cal = new Calibration { UnitsPerMeter = upm, Units = units };
        Assert.Equal(expected, cal.Describe(cal.ToTableUnits(cal.ScaleBar.Length)));
    }

    [Theory]
    [InlineData("m", "3,000 × 2,000 m")]
    [InlineData("km", "3.00 × 2.00 km")]
    [InlineData("yd", "3,281 × 2,187 yd")]
    [InlineData("mi", "1.86 × 1.24 mi")]
    [InlineData("pace", "3,937 × 2,625 paces")]
    public void A_sheets_size_reads_in_the_scenarios_measure(string units, string expected)
        => Assert.Equal(expected, Cal(units).DescribeSize(60, 40));

    [Fact]
    public void Without_a_scale_a_sheets_size_stays_in_table_units()
        => Assert.Equal("60 × 40.5 u", new Calibration { Units = "yd" }.DescribeSize(60, 40.5));

    [Fact]
    public void A_scale_bar_calibrated_end_to_end_counts_the_division_left_of_zero()
    {
        const double km = 15.6;
        double metresBar = 5 * km, milesBar = 4 * 1.609344 * km;
        string MilesBarReads(double metresSaid) => new Calibration { UnitsPerMeter = metresBar / metresSaid, Units = "mi" }.Describe(milesBar);
        Assert.Equal("3.20 mi", MilesBarReads(4000));
        Assert.Equal("4.00 mi", MilesBarReads(5000));
    }

    [Theory]
    [InlineData(0, 10, 1, 10, 0)]
    [InlineData(0, 1, 10, 0, 10)]
    [InlineData(0, 7, 6, 6.5, 6.5)]
    [InlineData(0, -7, -6, -6.5, -6.5)]
    [InlineData(0, 0, 0, 0, 0)]
    public void A_straightened_line_runs_to_the_nearest_of_the_eight_compass_points(double north, double x, double z, double wantX, double wantZ)
    {
        var to = new Calibration { North = north }.Straightened(new Vec2(3, 4), new Vec2(3 + x, 4 + z), 45);
        Assert.Equal(3 + wantX, to.X, 9);
        Assert.Equal(4 + wantZ, to.Z, 9);
    }

    [Theory]
    [InlineData(10, 10, 1)]
    [InlineData(-25, 3, -8)]
    [InlineData(200, -5, 0.5)]
    public void On_a_map_whose_north_is_turned_the_straight_line_follows_the_compass_not_the_sheet(double north, double x, double z)
    {
        var cal = new Calibration { North = north };
        var to = cal.Straightened(new Vec2(0, 0), new Vec2(x, z), 45);
        double bearing = cal.CompassBearing(Math.Atan2(to.X, to.Z) * 180 / Math.PI);
        Assert.Equal(0, Math.Abs(bearing / 45 - Math.Round(bearing / 45)), 6);
        Assert.True(to.Length <= new Vec2(x, z).Length + 1e-9);
        Assert.True(to.Length > 0);
    }

    [Fact]
    public void The_reading_helpers_are_not_written_into_a_save()
    {
        var json = Json.Serialize(Cal("yd"));
        Assert.DoesNotContain("scaleBar", json);
        Assert.DoesNotContain("measure", json, StringComparison.OrdinalIgnoreCase);
    }
}
