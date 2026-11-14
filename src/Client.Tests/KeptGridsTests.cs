using KTable.Client.Relief;
using KTable.Core.Model;

namespace KTable.Client.Tests;

public class KeptGridsTests
{
    static ReliefGrid Grid() => new(10, 10);

    [Fact]
    public void The_newest_grids_stay_and_the_oldest_past_the_spare_room_go()
    {
        var kept = new KeptGrids(() => [], spareCells: 250);
        foreach (var sha in new[] { "a", "b", "c", "d", "e" }) kept.Put(sha, Grid());
        Assert.Null(kept.Get("a"));
        Assert.Null(kept.Get("b"));
        Assert.NotNull(kept.Get("c"));
        Assert.NotNull(kept.Get("d"));
        Assert.NotNull(kept.Get("e"));
    }

    [Fact]
    public void A_grid_a_sheet_shows_is_never_let_go_and_takes_no_spare_room()
    {
        var kept = new KeptGrids(() => ["a"], spareCells: 100);
        foreach (var sha in new[] { "a", "b", "c", "d" }) kept.Put(sha, Grid());
        Assert.NotNull(kept.Get("a"));
        Assert.Null(kept.Get("b"));
        Assert.NotNull(kept.Get("c"));
        Assert.NotNull(kept.Get("d"));
    }

    [Fact]
    public void The_grid_just_put_stays_even_with_no_spare_room()
    {
        var kept = new KeptGrids(() => [], spareCells: 0);
        kept.Put("a", Grid());
        kept.Put("b", Grid());
        Assert.Null(kept.Get("a"));
        Assert.NotNull(kept.Get("b"));
        Assert.Equal(1, kept.Count);
    }

    [Fact]
    public void A_grid_put_again_counts_as_the_newest()
    {
        var kept = new KeptGrids(() => [], spareCells: 100);
        kept.Put("a", Grid());
        kept.Put("b", Grid());
        kept.Put("a", Grid());
        kept.Put("c", Grid());
        Assert.NotNull(kept.Get("a"));
        Assert.Null(kept.Get("b"));
        Assert.NotNull(kept.Get("c"));
    }

    [Fact]
    public void Many_strokes_on_one_sheet_hold_no_more_than_the_spare_room()
    {
        string shown = "0";
        var kept = new KeptGrids(() => [shown], spareCells: 1000);
        for (int i = 0; i < 500; i++)
        {
            kept.Put(i.ToString(), Grid());
            shown = i.ToString();
        }
        Assert.InRange(kept.Count, 1, 12);
        Assert.NotNull(kept.Get("499"));
    }
}
