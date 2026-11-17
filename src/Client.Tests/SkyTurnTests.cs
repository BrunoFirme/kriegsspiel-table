using KTable.Core.Model;

namespace KTable.Client.Tests;

public class SkyTurnTests
{
    static GameClock At(string time) => new() { GameTime = time };

    static SkyTurn ByClock(string time, Lighting? basis = null)
    {
        var l = basis ?? new Lighting();
        l.FromClock = true;
        return DayLight.Sky(At(time), DayLight.At(At(time), l));
    }

    [Fact]
    public void By_the_clock_the_sky_is_day_at_noon_dark_at_midnight_and_glows_at_sunrise_and_sunset()
    {
        var noon = ByClock("1812-09-22T13:00");
        Assert.Equal((0.0, 0.0), (noon.Dark, noon.Glow));
        Assert.Equal((180.0, 55.0), (noon.SunFrom, noon.SunHeight));
        var midnight = ByClock("1812-09-22T01:00");
        Assert.Equal((1.0, 0.0), (midnight.Dark, midnight.Glow));
        Assert.True(midnight.SunHeight < -20 && midnight.MoonHeight > 20, $"{midnight}");
        Assert.Equal(0, midnight.SunFrom, 6);
        Assert.Equal(180, midnight.MoonFrom, 6);
        var sunrise = ByClock("1812-09-22T06:00");
        Assert.Equal(1, sunrise.Glow, 6);
        Assert.Equal((90.0, 0.0), (sunrise.SunFrom, sunrise.SunHeight));
        var sunset = ByClock("1812-09-22T20:00");
        Assert.Equal(1, sunset.Glow, 6);
        Assert.Equal(270, sunset.SunFrom, 6);
        Assert.InRange(sunset.Dark, 0, 0.1);
    }

    [Fact]
    public void The_sun_and_the_stars_never_jump_from_one_minute_to_the_next()
    {
        foreach (var basis in new[] { new Lighting(), new Lighting { Latitude = 55 }, new Lighting { Latitude = -40 }, new Lighting { Sunrise = 4, Sunset = 22 } })
        {
            SkyTurn? before = null;
            for (int minute = 0; minute < 24 * 60; minute += 2)
            {
                var now = ByClock($"1812-06-21T{minute / 60:00}:{minute % 60:00}", basis.Clone());
                if (before is { } was)
                {
                    Assert.InRange(Math.Abs(now.SunHeight - was.SunHeight), 0, 1.2);
                    Assert.InRange(Math.Abs(now.Dark - was.Dark), 0, 0.15);
                    Assert.InRange(Math.Abs(now.Glow - was.Glow), 0, 0.15);
                    Assert.InRange(180 - Math.Abs(Math.Abs(now.SunFrom - was.SunFrom) - 180), 0, 3);
                }
                before = now;
            }
        }
    }

    [Fact]
    public void Latitude_and_date_set_the_length_of_the_night_and_how_dark_it_gets()
    {
        var north = new Lighting { Latitude = 62 };
        var whiteNight = ByClock("1812-06-21T00:00", north.Clone());
        Assert.InRange(whiteNight.SunHeight, -6, -3);
        Assert.True(whiteNight.Dark < 0.5 && whiteNight.Glow > 0.5, $"{whiteNight}");
        var winterNight = ByClock("1812-12-21T00:00", north.Clone());
        Assert.Equal(1, winterNight.Dark);
        Assert.Equal(62, winterNight.Pole);
        var winterNoon = ByClock("1812-12-21T12:00", north.Clone());
        Assert.InRange(winterNoon.SunHeight, 3, 6);
        Assert.Equal(0, winterNoon.Dark);
        var south = ByClock("1812-12-21T12:00", new Lighting { Latitude = -40 });
        Assert.Equal(0, south.SunFrom, 6);
        Assert.Equal(DayLight.At(At("1812-12-21T12:00"), new Lighting { Latitude = -40, FromClock = true }).SunHeight, south.SunHeight, 6);
    }

    [Fact]
    public void Without_the_clock_the_sky_is_read_from_the_light()
    {
        var day = DayLight.Sky(null, new Lighting());
        Assert.Equal((0.0, 0.0, 0.0), (day.Dark, day.Glow, day.Turn));
        Assert.Equal(new Lighting().SunHeight, day.SunHeight);
        var sundown = DayLight.Sky(At("1812-09-22T13:00"), LightPresets.Named(LightPresets.Sundown)!);
        Assert.Equal(1, sundown.Glow, 6);
        Assert.InRange(sundown.SunHeight, 0, 5);
        var moonlit = DayLight.Sky(null, LightPresets.Named(LightPresets.MoonlitNight)!);
        Assert.Equal(1, moonlit.Dark);
        Assert.True(moonlit.MoonHeight > 0 && moonlit.SunHeight < 0);
        var moonless = DayLight.Sky(null, LightPresets.Named("Dark night")!);
        Assert.True(moonless.Dark == 1 && moonless.MoonHeight < 0);
        Assert.Equal(0, moonless.Moonlight);
        Assert.InRange(moonlit.Moonlight, 0.4, 0.6);
        Assert.Equal(0, day.Moonlight);
        Assert.True(ByClock("1812-09-22T01:00").Moonlight > 0 && ByClock("1812-09-22T13:00").Moonlight == 0);
        Assert.Equal(0, DayLight.Sky(null, LightPresets.Named("Warm lamp")!).Glow);
    }
}
