using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class ReliefBaseTests
{
    static readonly string A = new('a', 64), B = new('b', 64), C = new('c', 64);

    static GameState Shaped(string? relief)
    {
        var g = CoreSamples.State();
        g.Sheets.First(s => s.Id == "s-000001").Relief = relief;
        return g;
    }

    [Fact]
    public void A_stroke_based_on_the_standing_ground_is_kept()
    {
        var g = Shaped(A);
        Assert.Null(StateMachine.Apply(g, new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1, Base = A }, out _));
        Assert.Equal(B, g.Sheets.First(s => s.Id == "s-000001").Relief);
    }

    [Fact]
    public void A_stroke_based_on_ground_that_changed_since_is_refused_and_changes_nothing()
    {
        var g = Shaped(C);
        Assert.Equal(SetSheetRelief.GroundChanged, StateMachine.Apply(g, new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1, Base = A }, out _));
        Assert.Equal(C, g.Sheets.First(s => s.Id == "s-000001").Relief);
    }

    [Fact]
    public void A_first_stroke_on_flat_ground_is_based_on_no_relief()
    {
        Assert.Null(StateMachine.Apply(Shaped(null), new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1, Base = SetSheetRelief.NoRelief }, out _));
        Assert.Equal(SetSheetRelief.GroundChanged, StateMachine.Apply(Shaped(A), new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1, Base = SetSheetRelief.NoRelief }, out _));
    }

    [Fact]
    public void A_stroke_naming_no_base_is_kept_as_before()
    {
        Assert.Null(StateMachine.Apply(Shaped(C), new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1 }, out _));
    }

    [Fact]
    public void An_undo_is_refused_once_the_ground_moved_past_the_stroke()
    {
        var g = Shaped(A);
        Assert.Null(StateMachine.Apply(g, new SetSheetRelief { Id = "s-000001", Relief = B, Height = 1, Base = A }, out var undo));
        Assert.Null(StateMachine.Apply(g, new SetSheetRelief { Id = "s-000001", Relief = C, Height = 1, Base = B }, out _));
        Assert.Equal(SetSheetRelief.GroundChanged, StateMachine.Apply(g, Assert.IsType<SetSheetRelief>(undo), out _));
        Assert.Equal(C, g.Sheets.First(s => s.Id == "s-000001").Relief);
    }
}
