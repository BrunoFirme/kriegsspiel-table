namespace KTable.Client.Tests;

public class PickingTests
{
    static Picking<string> Five()
    {
        var p = new Picking<string>();
        p.Show(new[] { "a", "b", "c", "d", "e" });
        return p;
    }

    [Fact]
    public void A_click_picks_one_a_toggle_adds_or_drops_and_a_range_runs_from_the_last_click()
    {
        var p = Five();
        p.Click("b", toggle: false, range: false);
        Assert.Equal(new[] { "b" }, p.Picked);
        Assert.Equal("b", p.Single);
        p.Click("d", toggle: true, range: false);
        Assert.Equal(new[] { "b", "d" }, p.Picked);
        Assert.Null(p.Single);
        p.Click("b", toggle: true, range: false);
        Assert.Equal(new[] { "d" }, p.Picked);
        p.Click("a", toggle: false, range: true);
        Assert.Equal(new[] { "a", "b" }, p.Picked);
        p.Click("e", toggle: false, range: false);
        p.Click("c", toggle: true, range: true);
        Assert.Equal(new[] { "c", "d", "e" }, p.Picked);
    }

    [Fact]
    public void A_range_without_an_earlier_click_picks_the_one_clicked()
    {
        var p = Five();
        p.Click("c", toggle: false, range: true);
        Assert.Equal(new[] { "c" }, p.Picked);
    }

    [Fact]
    public void A_right_click_on_something_picked_keeps_the_rest_and_on_anything_else_picks_only_it()
    {
        var p = Five();
        p.Click("a", false, false);
        p.Click("c", true, false);
        p.PickForMenu("c");
        Assert.Equal(new[] { "a", "c" }, p.Picked);
        p.PickForMenu("e");
        Assert.Equal(new[] { "e" }, p.Picked);
    }

    [Fact]
    public void What_is_gone_after_a_refresh_is_dropped_and_all_picks_everything_shown()
    {
        var p = Five();
        p.Click("b", false, false);
        p.Click("d", true, false);
        p.Show(new[] { "a", "b", "c" });
        Assert.Equal(new[] { "b" }, p.Picked);
        p.Click("a", false, true);
        Assert.Equal(new[] { "a" }, p.Picked);
        p.All();
        Assert.Equal(new[] { "a", "b", "c" }, p.Picked);
        p.Clear();
        Assert.Equal(0, p.Count);
    }
}
