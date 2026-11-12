using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SunLightTests
{
    static GameClock At(string time) => new() { GameTime = time };

    [Theory]
    [InlineData(6.0, 90, 5)]
    [InlineData(13.0, 180, 55)]
    [InlineData(20.0, 270, 5)]
    [InlineData(2.0, 90, 5)]
    [InlineData(23.0, 270, 5)]
    public void The_clock_s_sun_rises_in_the_east_stands_south_at_midday_and_sets_in_the_west(double hour, double from, double height)
    {
        var (f, h) = DayLight.SunAt(hour);
        Assert.Equal(from, f, 6);
        Assert.Equal(height, h, 6);
    }

    [Fact]
    public void An_old_table_s_sun_is_aimed_where_the_ceiling_light_always_stood()
    {
        Assert.Equal((-88.0, 35.0), DayLight.Aim(Lighting.OverheadFrom, Lighting.Overhead, 0));
    }

    [Theory]
    [InlineData(0, 0, 180)]
    [InlineData(90, 0, 90)]
    [InlineData(270, 0, 270)]
    [InlineData(90, 90, 0)]
    public void The_sun_is_turned_with_the_map_s_north(double from, double north, double yaw)
    {
        Assert.Equal(yaw, DayLight.Aim(from, 30, north).yaw, 6);
    }

    [Fact]
    public void The_sun_is_aimed_no_lower_than_the_lowest_sun_and_a_broken_place_falls_back_to_overhead()
    {
        Assert.Equal(-Lighting.LowestSun, DayLight.Aim(90, 0, 0).pitch);
        Assert.Equal((-88.0, 35.0), DayLight.Aim(double.NaN, double.NaN, 0));
    }

    [Fact]
    public void The_sun_climbs_through_the_morning_and_sinks_through_the_afternoon()
    {
        var morning = DayLight.SunAt(9.5);
        var afternoon = DayLight.SunAt(16.5);
        Assert.InRange(morning.from, 90, 180);
        Assert.InRange(afternoon.from, 180, 270);
        Assert.InRange(morning.height, 20, 55);
        Assert.Equal(morning.height, afternoon.height, 6);
    }

    [Fact]
    public void Following_the_clock_places_the_sun_and_keeps_the_rest_of_the_look()
    {
        var basis = new Lighting { SunFrom = 10, SunHeight = 70, Backdrop = "saxon-meadow", SnowLine = 300, Streams = true, Contours = true, TableShade = 0.5 };
        var l = DayLight.At(At("1866-07-03T09:30"), basis);
        Assert.True(l.FromClock);
        Assert.Equal(DayLight.SunAt(9.5), (l.SunFrom, l.SunHeight));
        Assert.Equal(("saxon-meadow", 300.0, true, true, 0.5), (l.Backdrop, l.SnowLine, l.Streams, l.Contours, l.TableShade));
        Assert.Equal((10.0, 70.0), (basis.SunFrom, basis.SunHeight));
    }

    [Fact]
    public void The_clock_line_names_the_sun_by_day_and_leaves_it_out_at_night()
    {
        Assert.Equal("sun ESE 28°", DayLight.SunWords(new Lighting { SunEnergy = 0.5, SunFrom = 112, SunHeight = 28 }));
        Assert.Equal("sun overhead", DayLight.SunWords(new Lighting { SunEnergy = 0.5 }));
        Assert.Equal("", DayLight.SunWords(DayLight.At(At("1866-07-03T02:00"), new Lighting())));
    }

    [Fact]
    public void Morning_and_afternoon_presets_put_the_sun_low_in_the_east_and_west()
    {
        var morning = LightPresets.Named(LightPresets.MorningSun)!;
        var afternoon = LightPresets.Named(LightPresets.AfternoonSun)!;
        Assert.Equal((90.0, 20.0), (morning.SunFrom, morning.SunHeight));
        Assert.Equal((270.0, 20.0), (afternoon.SunFrom, afternoon.SunHeight));
        Assert.False(LightPresets.Same(morning, afternoon));
        Assert.All(LightPresets.All.Where(p => p.Name is not (LightPresets.MorningSun or LightPresets.AfternoonSun)).Select(p => p.Light),
            l => Assert.Equal((Lighting.OverheadFrom, Lighting.Overhead), (l.SunFrom, l.SunHeight)));
    }

    [Fact]
    public void A_light_is_the_same_as_a_preset_only_with_the_sun_in_the_same_place()
    {
        var morning = LightPresets.Named(LightPresets.MorningSun)!;
        var moved = morning.Clone();
        moved.SunHeight = 40;
        Assert.False(LightPresets.Same(morning, moved));
        var north = new Lighting { SunFrom = 359.5 };
        Assert.True(LightPresets.Same(north, new Lighting { SunFrom = 0.2 }));
    }

    [Fact]
    public void A_preset_brings_its_sun_and_keeps_the_snow_line_streams_and_backdrop()
    {
        var now = new Lighting { SnowLine = 250, Streams = true, Backdrop = "orchard", Contours = true, MapStyle = MapStyles.Survey, FromClock = true };
        var l = LightPresets.Lit(now, LightPresets.Named(LightPresets.AfternoonSun)!);
        Assert.Equal((270.0, 20.0), (l.SunFrom, l.SunHeight));
        Assert.Equal((250.0, true, "orchard", true, false), (l.SnowLine, l.Streams, l.Backdrop, l.Contours, l.FromClock));
    }

    [Fact]
    public void A_sky_backdrop_sets_the_light_s_tone_and_leaves_the_sun_where_it_was()
    {
        var now = new Lighting { SunFrom = 90, SunHeight = 25, SnowLine = 250, Streams = true };
        var l = LightPresets.Toned(now, LightPresets.Named(LightPresets.MoonlitNight)!);
        Assert.Equal((90.0, 25.0, 250.0, true), (l.SunFrom, l.SunHeight, l.SnowLine, l.Streams));
        Assert.True(LightPresets.Same(LightPresets.Named(LightPresets.MoonlitNight)!, new Lighting { SunEnergy = l.SunEnergy, Ambient = l.Ambient, Warmth = l.Warmth, Shadows = l.Shadows }));
    }
}
