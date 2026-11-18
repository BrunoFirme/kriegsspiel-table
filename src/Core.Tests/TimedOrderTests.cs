using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class TimedOrderTests
{
    const string Layer = Layers.Umpire;

    static TimedOrder East(double start = 100) => TimedOrder.Along([new(0, 0), new(10, 0), new(10, 10)], [10, 20], start, 1, 45);

    static GameState Table(params PieceInstance[] pieces)
    {
        var g = new GameState();
        g.Layers[Layer] = pieces.ToList();
        g.Clock.Put(1000);
        return g;
    }

    static void Near(Vec2 want, Vec2 got)
    {
        Assert.Equal(want.X, got.X, 9);
        Assert.Equal(want.Z, got.Z, 9);
    }

    [Fact]
    public void A_piece_stands_at_the_start_before_its_time_walks_each_leg_at_its_pace_and_stays_at_the_end()
    {
        var order = East();
        Assert.Equal(new OrderPoint(new(0, 0), 270, false), order.At(50));
        Near(new(5, 0), order.At(105).Pos);
        Near(new(10, 5), order.At(120).Pos);
        Assert.Equal(180, order.At(120).RotDeg, 9);
        Assert.False(order.At(129.9).Arrived);
        Assert.Equal(new OrderPoint(new(10, 10), 180, true), order.At(130));
        Assert.Equal(new OrderPoint(new(10, 10), 180, true), order.At(1e6));
        Assert.Equal(130, order.Arrives);
    }

    [Fact]
    public void Pace_scales_the_time_and_the_facing_follows_the_table_axes()
    {
        var order = East();
        order.Pace = 2;
        Near(new(10, 0), order.At(105).Pos);
        Assert.Equal(115, order.Arrives);
        Assert.Equal(0, TimedOrder.Facing(new(0, 0), new(0, -1)), 9);
        Assert.Equal(90, TimedOrder.Facing(new(0, 0), new(-1, 0)), 9);
        Assert.Equal(180, TimedOrder.Facing(new(0, 0), new(0, 1)), 9);
        Assert.Equal(270, TimedOrder.Facing(new(0, 0), new(1, 0)), 9);
    }

    [Fact]
    public void A_straight_order_times_each_leg_by_its_length_and_joined_parts_keep_their_times()
    {
        var straight = TimedOrder.Straight([new(0, 0), new(3, 4), new(3, 10)], 0.5, 0, 0);
        Assert.Equal(new[] { 10d, 12d }, straight.Legs);
        var joined = TimedOrder.Joined([TimedOrder.Along([new(0, 0), new(1, 0)], [4], 0, 1, 0), TimedOrder.Along([new(1, 0), new(1, 2)], [6], 0, 1, 0)], 7, 1, 0);
        Assert.Equal(new Vec2[] { new(0, 0), new(1, 0), new(1, 2) }, joined.Points);
        Assert.Equal(17, joined.Arrives);
    }

    [Fact]
    public void A_stretch_holds_only_the_leg_in_hand_and_puts_the_piece_where_the_whole_route_does()
    {
        var order = East();
        order.Pace = 2;
        foreach (double t in new[] { 90, 101, 104.9, 106, 114, 200 })
        {
            var part = order.Stretch(t);
            Assert.True(part.Points.Count <= 2);
            Assert.Equal(order.At(t).Pos, part.At(t).Pos);
            Assert.Equal(order.At(t).RotDeg, part.At(t).RotDeg, 9);
        }
        Assert.Equal(new[] { 0, 1, 2 }, new[] { order.LegAt(101), order.LegAt(106), order.LegAt(200) });
    }

    [Fact]
    public void A_broken_order_is_refused()
    {
        var bad = East();
        bad.Legs.RemoveAt(0);
        Assert.NotNull(bad.Fault());
        var negative = East();
        negative.Legs[0] = -1;
        Assert.NotNull(negative.Fault());
        var slow = East();
        slow.Pace = 0;
        Assert.NotNull(slow.Fault());
        Assert.Null(East().Fault());
    }

    [Fact]
    public void Giving_an_order_puts_the_piece_where_the_clock_says_and_undo_takes_it_back()
    {
        var p = new PieceInstance { Id = "a", Def = "inf", Pos = new(0, 0), RotDeg = 30 };
        var g = Table(p);
        Assert.Null(StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a", Order = East(995) }, out var undo));
        Near(new(5, 0), p.Pos);
        Assert.Equal(270, p.RotDeg, 9);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.Null(p.Timed);
        Assert.Equal((new Vec2(0, 0), 30d), (p.Pos, p.RotDeg));
    }

    [Fact]
    public void An_order_still_to_be_routed_never_applies()
    {
        var g = Table(new PieceInstance { Id = "a", Def = "inf" });
        Assert.NotNull(StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a", To = [new(1, 1)] }, out _));
        Assert.NotNull(StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a" }, out _));
    }

    [Fact]
    public void The_clock_moves_the_piece_and_what_rides_on_it_and_a_new_order_starts_where_the_old_one_put_it()
    {
        var p = new PieceInstance { Id = "a", Def = "inf", Pos = new(0, 0), Attached = ["b"] };
        var rider = new PieceInstance { Id = "b", Def = "inf", Pos = new(0, 1) };
        var g = Table(p, rider);
        StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a", Order = East(1000) }, out _);
        g.Clock.Move(5);
        Assert.Equal(1, TimedOrders.Play(g, g.Clock.Moment));
        Near(new(5, 0), p.Pos);
        Near(new Vec2(5, 0) + Turned(new(0, 1), 270), rider.Pos);
        Assert.Equal(0, TimedOrders.Play(g, g.Clock.Moment));
        var here = p.Pos;
        var turned = TimedOrder.Along([here, new(5, -10)], [10], g.Clock.Moment, 1, p.RotDeg);
        StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a", Order = turned }, out _);
        Near(here, p.Pos);
        g.Clock.Move(10);
        TimedOrders.Play(g, g.Clock.Moment);
        Near(new(5, -10), p.Pos);
        Assert.Equal(0, p.RotDeg, 9);
    }

    static Vec2 Turned(Vec2 offset, double degrees)
    {
        double rad = -degrees * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
        return new Vec2(offset.X * cos - offset.Z * sin, offset.X * sin + offset.Z * cos);
    }

    [Fact]
    public void A_hand_ends_the_order_and_its_undo_brings_the_order_back()
    {
        var p = new PieceInstance { Id = "a", Def = "inf", Pos = new(0, 0) };
        var g = Table(p);
        StateMachine.Apply(g, new OrderPiece { Layer = Layer, Id = "a", Order = East(1000) }, out _);
        Assert.Null(StateMachine.Apply(g, new MovePiece { Layer = Layer, Id = "a", Pos = new(40, 40) }, out var undo));
        Assert.Null(p.Timed);
        g.Clock.Move(5);
        Assert.Equal(0, TimedOrders.Play(g, g.Clock.Moment));
        Assert.Equal(new Vec2(40, 40), p.Pos);
        Assert.Null(StateMachine.Apply(g, undo!, out _));
        Assert.NotNull(p.Timed);
        Near(new(5, 0), p.Pos);
    }

    [Fact]
    public void A_timed_order_rides_in_the_save_and_a_clone()
    {
        var p = new PieceInstance { Id = "a", Def = "inf", Timed = East() };
        p.Timed.Pace = 1.5;
        p.Timed.By = "rider";
        var back = Json.Deserialize<PieceInstance>(Json.Serialize(p));
        Assert.Equal(Json.Serialize(p), Json.Serialize(back));
        Assert.Equal(p.Timed.Arrives, back.Timed!.Arrives);
        var copy = p.Clone();
        copy.Timed!.Points.Clear();
        Assert.Equal(3, p.Timed.Points.Count);
    }
}
