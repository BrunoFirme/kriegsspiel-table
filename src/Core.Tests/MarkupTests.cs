using KTable.Core.Model;

namespace KTable.Core.Tests;

public class MarkupTests
{
    [Fact]
    public void Text_without_symbols_is_one_plain_run()
    {
        var run = Assert.Single(Markup.Runs("Hold the ford [until dusk]"));
        Assert.Equal(("Hold the ford [until dusk]", true), (run.Text, run.Plain));
    }

    [Fact]
    public void Bold_italic_underline_and_strike_switch_on_and_off_in_either_case()
    {
        var runs = Markup.Runs("a[b]b[i]bi[/b]i[/I][U]u[/u][s]s[/S]z");
        Assert.Equal(new[] { "a", "b", "bi", "i", "u", "s", "z" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { false, true, true, false, false, false, false }, runs.Select(r => r.Bold));
        Assert.Equal(new[] { false, false, true, true, false, false, false }, runs.Select(r => r.Italic));
        Assert.Equal(new[] { false, false, false, false, true, false, false }, runs.Select(r => r.Underline));
        Assert.Equal(new[] { false, false, false, false, false, true, false }, runs.Select(r => r.Strike));
    }

    [Fact]
    public void A_colour_holds_until_a_dash_takes_it_off_and_the_one_before_comes_back()
    {
        var runs = Markup.Runs("plain[ff0000]red[00FF0080]green[-]red again[-]plain[-]still plain");
        Assert.Equal(new[] { "plain", "red", "green", "red again", "plainstill plain" }, runs.Select(r => r.Text));
        Assert.Equal(new[] { null, "#FF0000", "#00FF0080", "#FF0000", null }, runs.Select(r => r.Color));
    }

    [Fact]
    public void Raised_and_lowered_letters_end_with_either_closing_symbol()
    {
        var runs = Markup.Runs("x[sup]2[/sup] H[sub]2[/sup]O");
        Assert.Equal(new[] { MarkupLevel.Line, MarkupLevel.Raised, MarkupLevel.Line, MarkupLevel.Lowered, MarkupLevel.Line }, runs.Select(r => r.Level));
    }

    [Theory]
    [InlineData("[b]Orders[/b] for the [i]3rd[/i]", "Orders for the 3rd")]
    [InlineData("[c]as it is[/c] [80]faint", "as it is faint")]
    [InlineData("see [url=http://a/b]the map[/url]", "see the map")]
    [InlineData("cut [url=http://a", "cut ")]
    [InlineData("[x] [xy] [12345] [1234567] [ff00zz] [/q] [", "[x] [xy] [12345] [1234567] [ff00zz] [/q] [")]
    [InlineData("no bracket at all", "no bracket at all")]
    public void Only_the_game_s_own_symbols_leave_the_text(string written, string shown) => Assert.Equal(shown, Markup.Plain(written));
}
