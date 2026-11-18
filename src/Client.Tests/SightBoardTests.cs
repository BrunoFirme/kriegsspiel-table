using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public class SightBoardTests
{
    static GameState Table() => new() { Boards = { new BoardDef { Id = "b-1", Name = "Layer 1" } } };

    static readonly List<Vec2> Across = new() { new(0, 0), new(4, 0) };

    [Fact]
    public void The_first_line_kept_makes_the_sight_layer_and_later_ones_go_on_it()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, SightBoard.Keep(g, new SightLine(SightLine.Blurs, Across, 0.05), "x-b1", 0.2), out _));
        var sight = Assert.Single(g.Boards, b => b.Sight);
        Assert.Equal(("x-b1", SightBoard.Named), (sight.Id, sight.Name));
        Assert.Null(StateMachine.Apply(g, SightBoard.Keep(g, new SightLine(SightLine.Block, Across), "x-b2", 0.2), out _));
        Assert.Single(g.Boards, b => b.Sight);
        var kept = SightBoard.Kept(g, new HashSet<string>()).ToList();
        Assert.Equal(new[] { SightLine.Blurs, SightLine.Block }, kept.Select(s => s.Sight));
        Assert.Equal((0.05, SightLine.InkOf(SightLine.Blurs)), (kept[0].Blur!.Value, kept[0].Color));
        Assert.Null(kept[1].Blur);
        Assert.Empty(SightBoard.Kept(g, new HashSet<string> { "x-b1" }));
    }

    [Fact]
    public void A_kept_line_changed_keeps_its_id_and_place_and_is_one_step_to_undo()
    {
        var g = Table();
        Assert.Null(StateMachine.Apply(g, SightBoard.Keep(g, new SightLine(SightLine.Block, Across), "x-b1", 0.2), out _));
        g.DrawLayer(Layers.Umpire).Add(new Stroke { Id = "d-after", Board = "b-1", Points = { new(0, 0), new(1, 1) } });
        var id = g.DrawLayer(Layers.Umpire)[0].Id;
        var changed = SightBoard.Changed(g, id, s =>
        {
            s.Points[1] = new Vec2(4, 3);
            SightBoard.Kind(s, SightLine.Shows, 0);
        })!;
        Assert.Null(StateMachine.Apply(g, changed, out var undo));
        var now = g.DrawLayer(Layers.Umpire)[0];
        Assert.Equal((id, SightLine.Shows, new Vec2(4, 3)), (now.Id, now.Sight, now.Points[1]));
        Assert.Null(now.Blur);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Equal((SightLine.Block, new Vec2(4, 0)), (g.DrawLayer(Layers.Umpire)[0].Sight, g.DrawLayer(Layers.Umpire)[0].Points[1]));
        Assert.Null(SightBoard.Changed(g, "nope", _ => { }));
    }

    [Fact]
    public void Kept_lines_count_in_a_pov_capture_as_its_own_lines_do()
    {
        var d = new SightingDraft { Radius = 10, Format = SightingFormat.Round };
        d.Kept.Add(new SightLine(SightLine.Block, new List<Vec2> { new(3, -9), new(3, 9) }));
        d.Place(new[] { ("eye", new Vec2(0, 0)) });
        d.Look();
        Assert.False(d.EyesSee(new Vec2(5, 0)));
        Assert.True(d.EyesSee(new Vec2(-5, 0)));
    }
}
