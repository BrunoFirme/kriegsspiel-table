using KTable.Core.Model;
using KTable.Core.Protocol;
using C = KTable.Core.Model.CrowdCommands;

namespace KTable.Core.Tests;

public class CrowdOrderTests
{
    const double Upm = 0.1;

    static CrowdOrder Order(string command, int count = 10, string shape = CrowdShapes.Loose) => new()
    {
        Id = "o-1", Name = "The mob", Members = new() { ["w-1"] = Enumerable.Range(0, count).ToList() }, Do = command, To = [new Vec2(10, 0)], Shape = shape, Start = 1000,
    };

    static CrowdPath Line(Vec2 a, Vec2 b) => new([a, b]);

    [Fact]
    public void An_order_that_does_not_hold_together_says_why()
    {
        Assert.Null(Order(C.GoTo).Problem());
        Assert.NotNull(Order("dance").Problem());
        Assert.NotNull(Order(C.GoTo, 0).Problem());
        Assert.NotNull(new CrowdOrder { Members = Order(C.GoTo).Members, Do = C.GoTo, Shape = "blob" }.Problem());
        Assert.NotNull(new CrowdOrder { Members = Order(C.GoTo).Members, Do = C.GoTo, Act = "dance" }.Problem());
        Assert.NotNull(new CrowdOrder { Members = Order(C.GoTo).Members, Do = C.GoTo, Start = 10, Until = 5 }.Problem());
    }

    [Fact]
    public void A_line_stands_across_its_facing_a_ring_round_its_middle_and_the_way_lined_on_both_sides()
    {
        var line = Order(C.Form, 5, CrowdShapes.Line);
        var slots = Enumerable.Range(0, 5).Select(k => CrowdOrders.Slot(line, k, 5, Upm, new Vec2(0, 0), 0)).ToList();
        Assert.All(slots, s => Assert.Equal(0, s.At.Z, 6));
        Assert.Equal(4 * 0.9 * Upm, slots.Max(s => s.At.X) - slots.Min(s => s.At.X), 6);
        var ring = Order(C.Surround, 12, CrowdShapes.Ring);
        var round = Enumerable.Range(0, 12).Select(k => CrowdOrders.Slot(ring, k, 12, Upm, new Vec2(0, 0), 0).At.Length).ToList();
        Assert.All(round, r => Assert.Equal(round[0], r, 6));
        var way = Order(C.LineTheWay, 10, CrowdShapes.Sides);
        way.To = [new Vec2(0, 0), new Vec2(0, 10)];
        var sides = Enumerable.Range(0, 10).Select(k => CrowdOrders.Slot(way, k, 10, Upm, new Vec2(0, 5), 0).At.X).ToList();
        Assert.Equal(5, sides.Count(x => x > 0));
        Assert.Equal(5, sides.Count(x => x < 0));
    }

    [Fact]
    public void A_member_walks_to_their_place_at_the_pace_given_and_then_stands_doing_the_act()
    {
        var o = Order(C.Attack);
        o.Pace = CrowdPaces.Run;
        var start = new Vec2(0, 0);
        var track = CrowdOrders.Compile(o, 0, 1, start, 0, Upm, o.To[0], Line);
        Assert.Equal(2, track.Steps.Count);
        long arrive = track.Steps[0].End;
        double metres = track.Steps[0].Path!.Length / Upm;
        Assert.InRange(arrive - o.Start, metres / 3.5 - 1, metres / 3.5 + 1);
        Assert.True(track.At(o.Start + (arrive - o.Start) / 2, Upm, out var midway, out _) && midway.Moving);
        Assert.True(track.At(arrive + 60, Upm, out var there, out var act) && !there.Moving);
        Assert.Equal(RigMotion.Melee, act);
        Assert.False(track.Covers(o.Start - 1));
    }

    [Fact]
    public void Fire_at_from_a_line_of_two_ranks_kneels_the_front_rank_and_the_rear_rank_fires_standing()
    {
        const int count = 12;
        var o = Order(C.FireAt, count, CrowdShapes.Line);
        o.WidthM = CrowdOrders.TwoRanksM(count);
        var acts = Enumerable.Range(0, count).Select(rank =>
        {
            var track = CrowdOrders.Compile(o, rank, count, new Vec2(0, 0), 0, Upm, o.To[0], Line);
            Assert.True(track.At(track.Steps[0].End + 60, Upm, out _, out var act));
            return act;
        }).ToList();
        Assert.Equal(count / 2, acts.Count(a => a == "kneel+ranged"));
        Assert.Equal(count / 2, acts.Count(a => a == RigMotion.Ranged));
        Assert.All(acts.Take(count / 2), a => Assert.Equal("kneel+ranged", a));
        o.Shape = CrowdShapes.Loose;
        Assert.True(CrowdOrders.Compile(o, 0, count, new Vec2(0, 0), 0, Upm, o.To[0], Line).At(1_000_000, Upm, out _, out var loose));
        Assert.Equal(RigMotion.Ranged, loose);
        o.Act = RigMotion.Cheer;
        Assert.Null(o.Problem());
        o.Act = "kneel+ranged";
        Assert.Null(o.Problem());
    }

    [Fact]
    public void Halt_stands_where_they_are_go_home_ends_at_once_and_clearing_the_streets_keeps_them_indoors()
    {
        var start = new Vec2(3, 4);
        var halt = CrowdOrders.Compile(Order(C.Halt), 0, 1, start, 1, Upm, new Vec2(10, 0), Line);
        Assert.True(halt.At(5000, Upm, out var stood, out _));
        Assert.Equal(start, stood.At);
        var home = CrowdOrders.Compile(Order(C.Home), 0, 1, start, 0, Upm, new Vec2(10, 0), Line);
        Assert.False(home.Covers(1000));
        var curfew = CrowdOrders.Compile(Order(C.Clear), 0, 1, start, 0, Upm, new Vec2(0, 0), Line);
        long indoors = curfew.Steps[0].End + 10;
        Assert.True(curfew.Covers(indoors));
        Assert.False(curfew.At(indoors, Upm, out _, out _));
        var ended = Order(C.Gather);
        ended.Until = 1500;
        Assert.False(CrowdOrders.Compile(ended, 0, 1, start, 0, Upm, new Vec2(10, 0), Line).Covers(1500));
    }

    [Fact]
    public void An_order_is_given_changed_and_taken_back_in_one_step_each()
    {
        var g = new GameState();
        var unnamed = Order(C.GoTo);
        unnamed.Id = "";
        Assert.NotNull(StateMachine.Apply(g, new SetCrowdOrder { Order = unnamed }, out _));
        Assert.Null(StateMachine.Apply(g, new SetCrowdOrder { Order = Order(C.GoTo) }, out var take));
        Assert.Single(g.Crowds!.Orders!);
        var changed = Order(C.Gather);
        Assert.Null(StateMachine.Apply(g, new SetCrowdOrder { Order = changed }, out var back));
        Assert.Equal(C.Gather, g.Crowds!.Orders![0].Do);
        Assert.Null(StateMachine.Apply(g, back!, out _));
        Assert.Equal(C.GoTo, g.Crowds!.Orders![0].Do);
        Assert.Null(StateMachine.Apply(g, take!, out _));
        Assert.Null(g.Crowds);
        Assert.NotNull(StateMachine.Apply(g, new SetCrowdOrder { Order = Order("dance") }, out _));
    }
}
