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
    public void With_sunrise_and_sunset_typed_the_day_s_light_is_stretched_between_them()
    {
        var long_ = new Lighting { Sunrise = 4, Sunset = 22 };
        Assert.Equal((4.0, 22.0), DayLight.Daylight(At("1866-07-03T12:00"), long_));
        Assert.Equal(DayLight.At(At("1866-07-03T06:00"), new Lighting()).SunEnergy, DayLight.At(At("1866-07-03T04:00"), long_).SunEnergy, 9);
        Assert.Equal(DayLight.At(At("1866-07-03T20:00"), new Lighting()).Warmth, DayLight.At(At("1866-07-03T22:00"), long_).Warmth, 9);
        var noon = DayLight.At(At("1866-07-03T13:00"), long_);
        Assert.Equal((180.0, 55.0), (noon.SunFrom, noon.SunHeight));
        Assert.Equal(0, DayLight.At(At("1866-07-03T03:00"), long_).SunEnergy, 9);
        Assert.Equal("first light", DayLight.Describe(At("1866-07-03T04:10"), long_));
        Assert.Equal("night", DayLight.Describe(At("1866-07-03T04:10")));
        Assert.Equal((6.0, 20.0), DayLight.Daylight(At("1866-07-03T12:00"), new Lighting()));
        Assert.Equal((10.0, 11.0), DayLight.Daylight(At("1866-07-03T12:00"), new Lighting { Sunrise = 10, Sunset = 3 }));
    }

    [Theory]
    [InlineData(50, "1866-06-21T12:00", 7.9, 8.4)]
    [InlineData(50, "1866-12-21T12:00", 3.8, 4.2)]
    [InlineData(0, "1866-06-21T12:00", 5.99, 6.01)]
    [InlineData(-35, "1866-12-21T12:00", 7.0, 7.5)]
    [InlineData(66, "1866-06-21T12:00", 11.0, 11.5)]
    public void With_a_latitude_the_game_s_date_sets_how_long_the_day_is(double latitude, string time, double least, double most)
    {
        var (rise, set) = DayLight.Daylight(At(time), new Lighting { Latitude = latitude, Sunrise = 1, Sunset = 2 });
        Assert.InRange((set - rise) / 2, least, most);
        Assert.Equal(12, (rise + set) / 2, 9);
    }

    [Fact]
    public void With_a_latitude_the_noon_sun_stands_as_high_as_the_season_puts_it_and_north_of_a_southern_table()
    {
        var june = DayLight.At(At("1866-06-21T12:00"), new Lighting { Latitude = 50 });
        Assert.InRange(june.SunHeight, 62, 65);
        Assert.Equal(180, june.SunFrom, 6);
        var december = DayLight.At(At("1866-12-21T12:00"), new Lighting { Latitude = 50 });
        Assert.InRange(december.SunHeight, 15, 18);
        var south = DayLight.At(At("1866-12-21T12:00"), new Lighting { Latitude = -35 });
        Assert.Equal(0, south.SunFrom, 6);
        Assert.InRange(south.SunHeight, 76, 80);
    }

    [Theory]
    [InlineData("05:30", 5.5)]
    [InlineData("5", 5.0)]
    [InlineData("19.45", 19.75)]
    [InlineData("24:00", 24.0)]
    [InlineData("24:30", null)]
    [InlineData("7:75", null)]
    [InlineData("dawn", null)]
    [InlineData("", null)]
    public void An_hour_is_typed_as_hours_and_minutes(string typed, double? hour)
    {
        Assert.Equal(hour, DayLight.HourTyped(typed));
        if (hour is { } h) Assert.Equal(h, DayLight.HourTyped(DayLight.HourWords(h)));
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
    public void A_dim_cold_light_is_moonlight_and_named_as_the_moon_and_dim_daylight_is_still_the_sun()
    {
        Assert.True(DayLight.Moonlit(LightPresets.Named(LightPresets.MoonlitNight)!));
        foreach (var day in new[] { "Overcast", LightPresets.CoolDaylight, LightPresets.CeilingLight, LightPresets.MorningSun, LightPresets.Sundown, "Dark night" })
            Assert.False(DayLight.Moonlit(LightPresets.Named(day)!), day);
        Assert.Equal("moon W 30°", DayLight.SunWords(new Lighting { SunEnergy = 0.12, Warmth = -0.85, SunFrom = 270, SunHeight = 30 }));
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
