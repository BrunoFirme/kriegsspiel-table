namespace KTable.Core.Tests;

public class CalcTests
{
    [Theory]
    [InlineData("12*1.5", 18)]
    [InlineData("(3+4)/2", 3.5)]
    [InlineData("2*-3", -6)]
    [InlineData("1,5*2", 3)]
    [InlineData("5,200", 5.2)]
    [InlineData("0,750", 0.75)]
    [InlineData("2,125*2", 4.25)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("40.0 u", 40)]
    [InlineData("60°", 60)]
    [InlineData("3 × 4 ÷ 2 − 1", 5)]
    public void A_sum_typed_in_full_comes_out_as_its_value(string typed, double value) => Assert.Equal(value, Calc.Evaluate(typed, 99, negatives: true)!.Value, 9);

    [Theory]
    [InlineData("*1.5", 15)]
    [InlineData("/2", 5)]
    [InlineData("+1.5", 11.5)]
    [InlineData("x-1.5", 8.5)]
    [InlineData("x*1.5+2", 17)]
    [InlineData("(x+2)*2", 24)]
    public void A_sum_that_starts_with_an_operator_or_names_x_works_on_the_value_already_there(string typed, double value) =>
        Assert.Equal(value, Calc.Evaluate(typed, 10, negatives: true)!.Value, 9);

    [Fact]
    public void A_leading_minus_subtracts_where_no_value_is_below_zero_and_is_a_negative_number_where_one_can_be()
    {
        Assert.Equal(8.5, Calc.Evaluate("-1.5", 10, negatives: false)!.Value, 9);
        Assert.Equal(-1.5, Calc.Evaluate("-1.5", 10, negatives: true)!.Value, 9);
    }

    [Theory]
    [InlineData("2.4k", 2400)]
    [InlineData("1.2M", 1_200_000)]
    [InlineData("*2", 2400)]
    public void The_zoom_box_s_shorthand_counts_thousands_and_millions(string typed, double value) =>
        Assert.Equal(value, Calc.Evaluate(typed, 1200, negatives: false, shorthand: true)!.Value, 9);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    [InlineData("3/0")]
    [InlineData("x/0")]
    [InlineData("2x")]
    [InlineData("1.2.3")]
    [InlineData("10%")]
    [InlineData("(1+2")]
    [InlineData("1+2)")]
    public void A_sum_that_cannot_be_read_gives_nothing(string typed) => Assert.Null(Calc.Evaluate(typed, 10, negatives: true));
}
