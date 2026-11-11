using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class NorthTests
{
    [Fact]
    public void North_is_set_turned_into_0_to_360_and_its_undo_puts_the_old_one_back()
    {
        var g = new GameState { Calibration = new Calibration { UnitsPerMeter = 0.5, Units = "yd", North = 10 } };
        Assert.Null(StateMachine.Apply(g, new SetCalibration { UnitsPerMeter = 0.5, North = -30 }, out var inverse));
        Assert.Equal(330, g.Calibration.North);
        Assert.Equal(0.5, g.Calibration.UnitsPerMeter);
        Assert.Equal("yd", g.Calibration.Units);
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal(10, g.Calibration.North);
    }

    [Fact]
    public void Setting_the_scale_alone_keeps_north_and_a_north_that_is_no_number_is_refused()
    {
        var g = new GameState { Calibration = new Calibration { North = 45 } };
        StateMachine.Apply(g, new SetCalibration { UnitsPerMeter = 2, Units = "m" }, out _);
        Assert.Equal(45, g.Calibration.North);
        Assert.NotNull(StateMachine.Apply(g, new SetCalibration { UnitsPerMeter = 2, North = double.NaN }, out _));
        Assert.Equal(45, g.Calibration.North);
        Assert.Equal(2, g.Calibration.UnitsPerMeter);
    }

    [Fact]
    public void Compass_and_map_bearings_turn_by_north()
    {
        var c = new Calibration { North = 30 };
        Assert.Equal(0, c.CompassBearing(30));
        Assert.Equal(330, c.CompassBearing(0));
        Assert.Equal(120, c.MapBearing(90));
        Assert.Equal(10, new Calibration().CompassBearing(370));
    }

    [Fact]
    public void North_and_the_distance_unit_go_from_the_scenario_to_the_game_to_every_seat_and_back()
    {
        var g = GameState.FromScenario(new Scenario { Id = "s", Name = "Haiti", Calibration = new Calibration { UnitsPerMeter = 0.1, Units = "pace", North = 15 } });
        Assert.Equal((0.1, "pace", 15.0), (g.Calibration.UnitsPerMeter, g.Calibration.Units, g.Calibration.North));
        var back = g.ToScenario("s", "Haiti").Calibration;
        Assert.Equal((0.1, "pace", 15.0), (back.UnitsPerMeter, back.Units, back.North));
        var seen = Visibility.Filter(g, new PeerInfo { PeerId = 5, Role = Role.Observer }).Calibration;
        Assert.Equal((0.1, "pace", 15.0), (seen.UnitsPerMeter, seen.Units, seen.North));
        Assert.NotSame(g.Calibration, seen);
    }

    [Fact]
    public void A_scenario_whose_north_is_the_top_writes_no_north()
    {
        Assert.DoesNotContain("north", Json.Serialize(new Scenario { Id = "s" }));
        var turned = Json.Deserialize<Scenario>(Json.Serialize(new Scenario { Id = "s", Calibration = new Calibration { North = 15 } }))!;
        Assert.Equal(15, turned.Calibration.North);
    }
}
