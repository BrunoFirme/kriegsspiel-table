using KTable.Core.Model;
using KTable.Core.Protocol;

namespace KTable.Core.Tests;

public class SunPlaceTests
{
    [Fact]
    public void The_sun_s_place_is_set_kept_and_undone_with_the_light()
    {
        var g = new GameState();
        var lit = g.Lighting.Clone();
        (lit.SunFrom, lit.SunHeight) = (100, 30);
        Assert.Null(StateMachine.Apply(g, new SetLighting { Lighting = lit }, out var inverse));
        Assert.Equal((100.0, 30.0), (g.Lighting.SunFrom, g.Lighting.SunHeight));
        Assert.Null(StateMachine.Apply(g, inverse!, out _));
        Assert.Equal((Lighting.OverheadFrom, Lighting.Overhead), (g.Lighting.SunFrom, g.Lighting.SunHeight));
    }

    [Fact]
    public void A_bearing_past_a_full_turn_wraps_and_a_height_out_of_range_is_held_to_the_sky()
    {
        var g = new GameState();
        StateMachine.Apply(g, new SetLighting { Lighting = new Lighting { SunFrom = -90, SunHeight = 400 } }, out _);
        Assert.Equal((270.0, Lighting.HighestSun), (g.Lighting.SunFrom, g.Lighting.SunHeight));
        StateMachine.Apply(g, new SetLighting { Lighting = new Lighting { SunFrom = 725, SunHeight = -3 } }, out _);
        Assert.Equal((5.0, Lighting.LowestSun), (g.Lighting.SunFrom, g.Lighting.SunHeight));
    }

    [Fact]
    public void A_sun_place_that_is_not_a_number_is_refused_and_the_light_stays()
    {
        var g = new GameState();
        Assert.NotNull(StateMachine.Apply(g, new SetLighting { Lighting = new Lighting { SunFrom = double.NaN, SunEnergy = 0.1 } }, out _));
        Assert.NotNull(StateMachine.Apply(g, new SetLighting { Lighting = new Lighting { SunHeight = double.PositiveInfinity, SunEnergy = 0.1 } }, out _));
        Assert.NotEqual(0.1, g.Lighting.SunEnergy);
    }

    [Fact]
    public void A_light_saved_before_the_sun_had_a_place_reads_as_the_old_near_overhead_sun()
    {
        var old = Json.Deserialize<Lighting>("{\"sunEnergy\":0.5,\"ambient\":0.6,\"warmth\":0.2,\"shadows\":true,\"snowLine\":40,\"streams\":true}")!;
        Assert.Equal((Lighting.OverheadFrom, Lighting.Overhead), (old.SunFrom, old.SunHeight));
        var copy = old.Clone();
        Assert.Equal((40.0, true), (copy.SnowLine, copy.Streams));
    }
}
