using KTable.Core;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Server.Tests;

public class TimedOrderServerTests
{
    const double Side = 60, MetresPerUnit = 50;
    const string Sheet = "s-000001", Inf = "p-000002", Marker = "p-000003", Player = "Ann";
    const long PlayerPeer = 40;

    static Table Laid()
    {
        var t = new Table();
        var state = t.Server.State;
        state.Calibration.UnitsPerMeter = 1 / MetresPerUnit;
        var sheet = state.Sheets.Single(s => s.Id == Sheet);
        sheet.Pos = new Vec2(Side / 2, Side / 2);
        sheet.Size = new Size2(Side, Side);
        var grid = ReliefGrid.Flat(Side, Side);
        Array.Fill(grid.Kind, GroundKinds.Grass);
        sheet.Relief = ReliefFiles.Save(t.Store.PackageDir(UploadAsset.ReliefPackage), grid).sha;
        state.Find(Layers.Umpire, Inf)!.Pos = new Vec2(10, 10);
        t.Ok(Table.Lead, new AllowOrders { On = true });
        return t;
    }

    static PieceInstance Piece(Table t, string id = Inf) => t.Server.State.Find(Layers.Umpire, id)!;

    static OrderPiece Go(params Vec2[] to) => new() { Layer = Layers.Umpire, Id = Inf, To = to.ToList() };

    static void Step(Table t, long seconds) => t.Ok(Table.Host, new StepClock { Seconds = seconds });

    static List<string> Lines(Table t) => t.Server.Record.Select(e => e.Text).ToList();

    [Fact]
    public void The_host_routes_a_foot_order_across_laid_ground_and_the_clock_brings_the_piece_there()
    {
        using var t = Laid();
        t.Ok(Table.Ump3, Go(new Vec2(40, 10)));
        var order = Piece(t).Timed!;
        Assert.Equal("foot", order.By);
        Assert.Equal(new Vec2(10, 10), order.Points[0]);
        Assert.Equal(new Vec2(40, 10), order.End);
        double metres = 30 * MetresPerUnit;
        Assert.InRange(order.Seconds, metres / 1.5, metres / 0.5);
        Assert.Contains(Lines(t), l => l.StartsWith("Infantry set off on an order", StringComparison.Ordinal));
        Step(t, (long)Math.Ceiling(order.Seconds / 2));
        t.Server.PlayOrders();
        Assert.InRange(Piece(t).Pos.X, 15, 35);
        Step(t, (long)Math.Ceiling(order.Seconds));
        t.Server.PlayOrders();
        Assert.Equal(new Vec2(40, 10), Piece(t).Pos);
        Assert.Contains("Infantry arrived", Lines(t));
    }

    [Fact]
    public void Every_peer_is_sent_the_routed_order_never_the_ask()
    {
        using var t = Laid();
        var sent = t.Ok(Table.Host, Go(new Vec2(40, 10))).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command).OfType<OrderPiece>().ToList();
        Assert.NotEmpty(sent);
        Assert.All(sent, c => Assert.False(c.Asks));
        Assert.All(sent, c => Assert.NotNull(c.Order));
    }

    [Fact]
    public void A_new_order_starts_from_the_point_the_clock_put_the_piece_on_the_old_route()
    {
        using var t = Laid();
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(50, 10)], Speed = 1 });
        Step(t, 500);
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(20, 50)], Speed = 1 });
        var order = Piece(t).Timed!;
        Assert.Equal(20, order.Points[0].X, 6);
        Assert.Equal(10, order.Points[0].Z, 6);
        Assert.Equal(t.Server.State.Clock.Moment, order.Start);
        Assert.Equal(new Vec2(20, 10), Piece(t).Pos);
    }

    [Fact]
    public void Stop_ends_the_order_where_the_piece_is_and_undo_puts_it_back_on_its_way()
    {
        using var t = Laid();
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(50, 10)], Speed = 1 });
        Step(t, 500);
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, Stop = true });
        Assert.Null(Piece(t).Timed);
        Assert.Equal(20, Piece(t).Pos.X, 6);
        Step(t, 500);
        t.Server.PlayOrders();
        Assert.Equal(20, Piece(t).Pos.X, 6);
        Assert.Contains("Infantry's order ended", Lines(t));
        var stop = t.Server.Log.OfType<OrderPiece>().Last();
        t.Ok(Table.Host, new Undo { TargetSeq = stop.Seq });
        Assert.NotNull(Piece(t).Timed);
        Assert.Equal(30, Piece(t).Pos.X, 6);
    }

    [Fact]
    public void A_hand_on_the_piece_ends_its_order_and_says_so()
    {
        using var t = Laid();
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(50, 10)], Speed = 1 });
        t.Ok(Table.Ump3, new MovePiece { Layer = Layers.Umpire, Id = Inf, Pos = new(5, 5) });
        Assert.Null(Piece(t).Timed);
        Assert.Contains("Infantry's order ended by hand", Lines(t));
        Assert.All(t.Server.Record.Where(e => e.Text.Contains("order", StringComparison.Ordinal)), e => Assert.Equal(RecordEntry.ToUmpires, e.Reach));
    }

    [Fact]
    public void A_player_is_sent_only_the_stretch_the_piece_is_on_and_the_next_as_it_passes_a_stop()
    {
        using var t = Laid();
        t.LetIn(PlayerPeer, Player, Role.Player);
        var given = t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(20, 10), new(20, 30), new(40, 30)], Speed = 1 });
        var seen = given.Where(o => o.PeerId == PlayerPeer).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command).OfType<OrderPiece>().Single().Order!;
        Assert.Equal(new[] { new Vec2(10, 10), new Vec2(20, 10) }, seen.Points);
        var umpire = given.Where(o => o.PeerId == Table.Ump3).Select(o => o.Message).OfType<CommandMessage>().Select(m => m.Command).OfType<OrderPiece>().Single().Order!;
        Assert.Equal(new Vec2(40, 30), umpire.End);
        var player = t.Server.Peers.First(p => p.PeerId == PlayerPeer);
        Assert.Equal(new Vec2(20, 10), Visibility.Filter(t.Server.State, player).Find(Layers.Umpire, Inf)!.Timed!.End);
        var onward = t.Server.Handle(Table.Host, new StepClock { Seconds = 600 });
        var next = onward.Where(o => o.PeerId == PlayerPeer).Select(o => o.Message).OfType<Stretches>().ToList();
        Assert.Empty(next);
        var later = t.Server.PlayOrders();
        var stretch = Assert.Single(later.Where(o => o.PeerId == PlayerPeer).Select(o => o.Message).OfType<Stretches>()).Pieces.Single().Order;
        Assert.Equal(new[] { new Vec2(20, 10), new Vec2(20, 30) }, stretch.Points);
        Assert.DoesNotContain(later, o => o.PeerId == Table.Ump3);
        Assert.Equal(Piece(t).Timed!.At(t.Server.State.Clock.Moment).Pos, stretch.At(t.Server.State.Clock.Moment).Pos);
    }

    [Fact]
    public void Only_those_who_may_move_a_piece_may_order_it()
    {
        using var t = Laid();
        t.LetIn(PlayerPeer, Player, Role.Player);
        Assert.Contains("not allowed", t.Error(PlayerPeer, Go(new Vec2(40, 10))));
        Assert.Contains("not allowed", t.Error(Table.Obs, Go(new Vec2(40, 10))));
        Assert.Null(Piece(t).Timed);
    }

    [Fact]
    public void Orders_are_refused_while_the_scenario_has_them_off_but_stopping_and_undo_still_work()
    {
        using var t = Laid();
        t.Ok(Table.Host, Go(new Vec2(40, 10)));
        Assert.Contains("not allowed", t.Error(Table.Ump3, new AllowOrders { On = false }));
        t.Ok(Table.Lead, new AllowOrders { On = false });
        Assert.Equal(GameServer.OrdersOff, t.Error(Table.Host, Go(new Vec2(20, 20))));
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, Stop = true });
        Assert.Null(Piece(t).Timed);
        t.Ok(Table.Host, new Undo { TargetSeq = t.Server.Log.OfType<OrderPiece>().Last().Seq });
        Assert.NotNull(Piece(t).Timed);
        Assert.True(Json.Deserialize<GameState>(Json.Serialize(t.Server.State)).OrdersOn == false);
    }

    [Fact]
    public void An_order_with_no_way_or_no_scale_is_refused_and_changes_nothing()
    {
        using var t = new Table();
        t.Ok(Table.Lead, new AllowOrders { On = true });
        Assert.Contains("no way there", t.Error(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(3, 3)] }));
        Assert.Contains("scale", t.Error(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(3, 3)], Speed = 1 }));
        Assert.Contains("traveller", t.Error(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(3, 3)], By = "dragon" }));
        Assert.Contains("pace", t.Error(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(3, 3)], Pace = 0 }));
        Assert.Contains("no such piece", t.Error(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = "p-999", To = [new(3, 3)] }));
        Assert.Null(Piece(t).Timed);
    }

    [Fact]
    public void A_late_joiner_and_a_reloaded_save_see_the_piece_where_the_clock_puts_it()
    {
        using var t = Laid();
        t.Ok(Table.Host, new OrderPiece { Layer = Layers.Umpire, Id = Inf, To = [new(50, 10)], Speed = 1 });
        Step(t, 250);
        t.Server.PlayOrders();
        var saved = Json.Deserialize<GameState>(Json.Serialize(t.Server.State));
        Assert.NotNull(saved.Find(Layers.Umpire, Inf)!.Timed);
        Assert.Equal(15, saved.Find(Layers.Umpire, Inf)!.Pos.X, 6);
        saved.Clock.Move(250);
        TimedOrders.Play(saved, saved.Clock.Moment);
        Assert.Equal(20, saved.Find(Layers.Umpire, Inf)!.Pos.X, 6);
        var marker = Piece(t, Marker);
        Assert.Null(marker.Timed);
    }
}
