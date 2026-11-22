using KTable.Core.Model;

namespace KTable.Client.Tests;

public class InkOrderTests
{
    static readonly List<BoardDef> Boards = [new() { Id = "top", Name = "Top" }, new() { Id = "mid", Name = "Middle" }, new() { Id = "low", Name = "Low" }];

    static Stroke At(string id, string board, string color, double x, bool fill = false) =>
        new() { Id = id, Board = board, Color = color, Fill = fill, Width = 0.5, Points = { new(x, 0), new(x + 1, 1) } };

    static List<InkOrder.Run<string>> Runs(IEnumerable<Stroke> strokes, IReadOnlyList<BoardDef>? boards = null, int steps = 100) =>
        InkOrder.Runs(boards ?? Boards, strokes, s => s.Color, steps);

    static int StepOf(List<InkOrder.Run<string>> runs, string id, bool wash = false) => runs.Single(r => r.Wash == wash && r.Strokes.Any(s => s.Id == id)).Step;

    [Fact]
    public void An_upper_layer_draws_over_a_lower_whatever_was_drawn_last()
    {
        var runs = Runs([At("a", "top", "#000000", 0), At("b", "low", "#000000", 0), At("c", "mid", "#ff0000", 0), At("d", "low", "#00ff00", 0)]);
        Assert.True(StepOf(runs, "a") > StepOf(runs, "c"));
        Assert.True(StepOf(runs, "c") > StepOf(runs, "d"));
        Assert.True(StepOf(runs, "d") > StepOf(runs, "b"));
    }

    [Fact]
    public void Moving_a_layer_up_lifts_its_drawings_over_the_rest()
    {
        var strokes = new[] { At("a", "top", "#000000", 0), At("b", "low", "#ff0000", 0) };
        var before = Runs(strokes);
        Assert.True(StepOf(before, "a") > StepOf(before, "b"));
        var after = Runs(strokes, [Boards[2], Boards[0], Boards[1]]);
        Assert.True(StepOf(after, "b") > StepOf(after, "a"));
    }

    [Fact]
    public void On_one_layer_a_later_stroke_lies_over_an_earlier_one_it_crosses()
    {
        var runs = Runs([At("red1", "mid", "#ff0000", 0), At("blue", "mid", "#0000ff", 0), At("red2", "mid", "#ff0000", 0)]);
        Assert.True(StepOf(runs, "red2") > StepOf(runs, "blue"));
        Assert.True(StepOf(runs, "blue") > StepOf(runs, "red1"));
    }

    [Fact]
    public void Strokes_of_one_colour_that_nothing_comes_between_share_one_run()
    {
        var runs = Runs([At("red1", "mid", "#ff0000", 0), At("blue", "mid", "#0000ff", 50), At("red2", "mid", "#ff0000", 0)]);
        Assert.Equal(2, runs.Count);
        Assert.Equal(["red1", "red2"], runs.Single(r => r.Key == "#ff0000").Strokes.Select(s => s.Id));
    }

    [Fact]
    public void A_layers_fills_lie_under_its_lines_and_over_the_layer_below()
    {
        var runs = Runs([At("line", "mid", "#000000", 0), At("area", "mid", "#00ff00", 0, fill: true), At("under", "low", "#ff0000", 0)]);
        Assert.True(StepOf(runs, "area", wash: true) < StepOf(runs, "line"));
        Assert.True(StepOf(runs, "area", wash: true) > StepOf(runs, "under"));
    }

    [Fact]
    public void A_stroke_on_no_known_layer_lies_under_them_all()
    {
        var runs = Runs([At("lost", "gone", "#000000", 0), At("low", "low", "#ff0000", 0)]);
        Assert.True(StepOf(runs, "lost") < StepOf(runs, "low"));
    }

    [Fact]
    public void Past_the_steps_there_are_layers_still_never_share_one()
    {
        var at = InkOrder.Steps([40, 1, 300, 5], 20);
        Assert.Equal(346, at.Length);
        Assert.All(at, s => Assert.InRange(s, 0, 19));
        Assert.True(at.Zip(at.Skip(1)).All(p => p.First <= p.Second));
        int[] ends = [40, 41, 341, 346];
        for (int b = 1; b < ends.Length; b++) Assert.True(at[ends[b - 1]] > at[ends[b - 1] - 1]);
    }

    [Fact]
    public void Within_the_steps_every_run_has_its_own()
    {
        Assert.Equal([0, 1, 2, 3, 4], InkOrder.Steps([2, 0, 3], 10));
    }
}
