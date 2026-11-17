using System.Globalization;
using System.Text.RegularExpressions;
using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Client.Tests;

public partial class TableToolsTests
{
    const double TravelSide = 60, TravelMetresPerUnit = 50;

    BuiltThing TravelRoad(string sheet, double z) =>
        BuiltRoads.Lay(new BuiltSite { Width = TravelSide, Height = TravelSide, UnitsPerMetre = 1 / TravelMetresPerUnit, HeightUnit = TravelSide }, sheet,
            [new Spot(8, z), new Spot(52, z)], "road", false);

    [Fact]
    public async Task Travel_answers_the_time_over_a_road_with_its_route_and_keeps_its_map_until_a_road_is_laid()
    {
        _s.Do(new SetCalibration { UnitsPerMeter = 1 / TravelMetresPerUnit });
        _s.Do(new AddSheet { Sheet = new SheetDef { Id = "s-west", Name = "West", Pos = new Vec2(TravelSide / 2, TravelSide / 2), Size = new Size2(TravelSide, TravelSide) } });
        _s.Do(new AddSheet { Sheet = new SheetDef { Id = "s-east", Name = "East", Pos = new Vec2(TravelSide * 1.5, TravelSide / 2), Size = new Size2(TravelSide, TravelSide) } });
        var sheet = _s.View.Sheets.Single(s => s.Name == "West");
        Assert.Contains("no built way", (await Call("travel", new { from = new[] { 10.0, 30 }, to = new[] { 50.0, 30 } })).Text);
        _s.Do(new LayBuilt { Thing = TravelRoad(sheet.Id, 30) });
        _s.Do(new SetPlace { Place = new PlacePin { Name = "The Mill", Pos = new Vec2(50, 30) } });
        _s.Do(new MovePiece { Layer = Layers.Umpire, Id = "p-1", Pos = new Vec2(10, 30), RotDeg = 0 });
        var told = await Call("travel", new { from = "p-1", to = "The Mill" });
        Assert.False(told.IsError, told.Text);
        Assert.StartsWith("By foot: ", told.Text);
        double seconds = double.Parse(Regex.Match(told.Text, @"\((\d+) s\)").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.InRange(seconds, 40 * TravelMetresPerUnit / 1.5 * 0.97, 40 * TravelMetresPerUnit / 1.5 * 1.03);
        Assert.DoesNotContain("Points", told.Text);
        var routed = await Call("travel", new { from = new[] { 10.0, 30 }, to = "mill", by = "Rider", route = true });
        Assert.StartsWith("By rider: ", routed.Text);
        Assert.Contains("Points: [[10,30,0],", routed.Text);
        Assert.Equal(2, _s.Travel.Built);
        Assert.Contains("No way there by cart", (await Call("travel", new { from = new[] { 10.0, 45 }, to = new[] { 50.0, 30 }, by = "cart" })).Text);
        Assert.Contains("not on one map sheet", (await Call("travel", new { from = "p-1", to = new[] { 70.0, 30 } })).Text);
        var camel = await Call("travel", new { from = "p-1", to = "The Mill", by = "camel" });
        Assert.True(camel.IsError);
        Assert.Contains("cart, coach, foot, rider", camel.Text);
        Assert.True((await Call("travel", new { from = "nowhere at all", to = "The Mill" })).IsError);
        Assert.Equal(2, _s.Travel.Built);
        _s.Do(new LayBuilt { Thing = TravelRoad(sheet.Id, 40) });
        Assert.False((await Call("travel", new { from = "p-1", to = "The Mill" })).IsError);
        Assert.Equal(3, _s.Travel.Built);
    }
}
