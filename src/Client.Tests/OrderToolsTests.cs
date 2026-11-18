using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public partial class TableToolsTests
{
    [Fact]
    public async Task Order_piece_sends_a_piece_down_the_road_and_wait_for_arrival_says_when_it_gets_there()
    {
        _s.Do(new SetCalibration { UnitsPerMeter = 1 / TravelMetresPerUnit });
        Assert.Contains("orders are off", (await Call("order_piece", new { id = "p-1", to = new[] { 5.0, 5 } })).Text);
        _s.Do(new AllowOrders { On = true });
        _s.Do(new AddSheet { Sheet = new SheetDef { Id = "s-road", Name = "Road", Pos = new Vec2(TravelSide / 2, TravelSide / 2), Size = new Size2(TravelSide, TravelSide) } });
        _s.Do(new LayBuilt { Thing = TravelRoad(_s.View.Sheets.Single(s => s.Name == "Road").Id, 30) });
        _s.Do(new SetPlace { Place = new PlacePin { Name = "The Mill", Pos = new Vec2(50, 30) } });
        _s.Do(new MovePiece { Layer = Layers.Umpire, Id = "p-1", Pos = new Vec2(10, 30), RotDeg = 0 });
        var refused = await Call("order_piece", new { id = "p-1", to = new[] { 200.0, 30 } });
        Assert.True(refused.IsError);
        Assert.Contains("Not ordered", refused.Text);
        var told = await Call("order_piece", new { id = "p-1", to = "The Mill", by = "rider" });
        Assert.False(told.IsError, told.Text);
        Assert.Contains("goes by rider to 50, 30", told.Text);
        Assert.Contains("clock is paused", told.Text);
        var order = _s.View.Find(Layers.Umpire, "p-1")!.Timed!;
        Assert.Equal("rider", order.By);
        Assert.Contains("Nothing happened", (await Call("wait_for", new { what = "arrival", id = "p-1", seconds = 1 })).Text);
        long step = (long)Math.Ceiling(order.Seconds / 4) + 1;
        var ticking = new KTable.Client.Agent.TableTools(() => _s, () => false, _ => { _s.Do(new StepClock { Seconds = step }); return Task.CompletedTask; }, _log.Add)
            .Tools().First(t => t.Name == "wait_for");
        var arrived = await ticking.Run(System.Text.Json.JsonSerializer.SerializeToElement(new { what = "arrival", seconds = 5 }));
        Assert.Contains("arrived", arrived.Text);
        Assert.Contains("p-1", arrived.Text);
        Assert.Contains("has no order", (await Call("order_piece", new { id = "p-2", stop = true })).Text);
        Assert.Contains("has arrived", KTable.Client.Agent.OrderTools.Told(_s, _s.View.Find(Layers.Umpire, "p-1")!));
        Assert.False((await Call("order_piece", new { id = "p-1", stop = true })).IsError);
        Assert.Null(_s.View.Find(Layers.Umpire, "p-1")!.Timed);
    }
}
