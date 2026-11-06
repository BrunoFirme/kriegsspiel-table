using KTable.Core.Model;
using Xunit;

namespace KTable.Core.Tests;

public class CalibrationTests
{
    static Calibration Cal(string units) => new() { UnitsPerMeter = 0.02, Units = units };

    [Fact]
    public void Without_a_scale_a_length_stays_in_table_units()
    {
        Assert.Equal("7.50 u", new Calibration().Describe(7.5));
        Assert.Equal("7.50 u", new Calibration { UnitsPerMeter = 0, Units = "yd" }.Describe(7.5));
    }

    [Theory]
    [InlineData("m", 10, "500 m (656 paces)")]
    [InlineData("yd", 10, "547 yd (656 paces)")]
    [InlineData("pace", 10, "656 paces")]
    [InlineData("m", 20, "1.00 km (1,312 paces)")]
    [InlineData("yd", 20, "1,094 yd (1,312 paces)")]
    [InlineData("yd", 40, "1.24 mi (2,625 paces)")]
    public void A_length_reads_in_the_measure_the_scenario_asks_for(string units, double tableUnits, string expected)
        => Assert.Equal(expected, Cal(units).Describe(tableUnits));

    [Fact]
    public void An_unknown_measure_falls_back_to_metres()
        => Assert.Equal("500 m (656 paces)", Cal("furlongs").Describe(10));

    [Theory]
    [InlineData("m", 500, 10)]
    [InlineData("yd", 547.0, 10.0)]
    [InlineData("pace", 656.0, 10.0)]
    public void A_distance_typed_in_the_scenarios_measure_becomes_table_units(string units, double typed, double tableUnits)
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
        Assert.Equal("paces", Cal("pace").UnitName);
    }

    [Theory]
    [InlineData("m", 1000, "a kilometre")]
    [InlineData("yd", 1760, "a mile")]
    [InlineData("pace", 1000, "a thousand paces")]
    [InlineData("furlongs", 1000, "a kilometre")]
    public void A_scale_bar_is_a_round_length_in_the_scenarios_measure(string units, double length, string words)
    {
        Assert.Equal((length, words), Cal(units).ScaleBar);
        Assert.Equal((length, words), new Calibration { Units = units }.ScaleBar);
    }

    [Theory]
    [InlineData("m", 0.02, "1.00 km (1,312 paces)")]
    [InlineData("m", 0.0206, "1.00 km (1,312 paces)")]
    [InlineData("m", 0.3, "1.00 km (1,312 paces)")]
    [InlineData("m", 0.007, "1.00 km (1,312 paces)")]
    [InlineData("yd", 0.02, "1.00 mi (2,112 paces)")]
    [InlineData("yd", 0.0206, "1.00 mi (2,112 paces)")]
    [InlineData("yd", 0.3, "1.00 mi (2,112 paces)")]
    [InlineData("yd", 0.007, "1.00 mi (2,112 paces)")]
    [InlineData("pace", 0.0206, "1,000 paces")]
    public void A_scale_bar_reads_as_itself(string units, double upm, string expected)
    {
        var cal = new Calibration { UnitsPerMeter = upm, Units = units };
        Assert.Equal(expected, cal.Describe(cal.ToTableUnits(cal.ScaleBar.Length)));
    }

    [Theory]
    [InlineData("m", "3.0 × 2.0 km")]
    [InlineData("yd", "1.9 × 1.2 mi")]
    [InlineData("pace", "3,937 × 2,625 paces")]
    public void A_sheets_size_reads_in_the_long_unit_of_the_measure(string units, string expected)
        => Assert.Equal(expected, Cal(units).DescribeSize(60, 40));

    [Fact]
    public void Without_a_scale_a_sheets_size_stays_in_table_units()
        => Assert.Equal("60 × 40.5 u", new Calibration { Units = "yd" }.DescribeSize(60, 40.5));

    [Fact]
    public void A_scale_bar_calibrated_end_to_end_counts_the_division_left_of_zero()
    {
        const double km = 15.6;
        double metresBar = 5 * km, milesBar = 4 * 1.609344 * km;
        string MilesBarReads(double metresSaid) => new Calibration { UnitsPerMeter = metresBar / metresSaid, Units = "yd" }.Describe(milesBar);
        Assert.StartsWith("3.20 mi", MilesBarReads(4000));
        Assert.StartsWith("4.00 mi", MilesBarReads(5000));
    }

    [Fact]
    public void The_reading_helpers_are_not_written_into_a_save()
    {
        var json = Json.Serialize(Cal("yd"));
        Assert.DoesNotContain("scaleBar", json);
    }
}
