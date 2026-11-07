using KTable.Core;
using KTable.Core.Model;

namespace KTable.Core.Tests;

public class GlideTests
{
    [Fact]
    public void A_glide_pulls_the_piece_to_its_target_and_a_fast_one_gets_there_sooner()
    {
        var slow = new Glide();
        slow.MoveTo(new Vec2(0, 0), new Vec2(5, 0));
        var fast = new Glide { Fast = true };
        fast.MoveTo(new Vec2(0, 0), new Vec2(5, 0));
        while (!slow.Done) slow.Advance();
        while (!fast.Done) fast.Advance();
        Assert.True(fast.Steps < slow.Steps, $"{fast.Steps} against {slow.Steps}");
        Assert.Equal(new Vec2(5, 0), slow.Target);
        Assert.Null(slow.At);
    }

    [Fact]
    public void A_glide_turns_the_short_way_and_a_move_too_short_does_not_start()
    {
        var g = new Glide();
        g.TurnTo(350, 20);
        g.Advance();
        Assert.InRange(Glide.Turn(g.Heading!.Value), 350, 360);
        while (!g.Done) g.Advance();
        Assert.Equal(20, g.TurnedTo);
        var still = new Glide();
        still.MoveTo(new Vec2(1, 1), new Vec2(1.01, 1));
        still.TurnTo(30, 30.5);
        Assert.True(still.Done);
        Assert.Null(still.Target);
        Assert.Equal(10, Glide.Apart(355, 5));
    }
}
