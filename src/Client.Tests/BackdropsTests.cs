using KTable.Core.Model;

namespace KTable.Client.Tests;

public class BackdropsTests
{
    [Fact]
    public void A_new_scenario_takes_the_chosen_backdrop_and_surface()
    {
        var look = Backdrops.NewLook("saxon-meadow", 0.5);
        Assert.Equal("saxon-meadow", look.Backdrop);
        Assert.Equal(0.5, look.TableShade);
        Assert.Equal(new Lighting().SunEnergy, look.SunEnergy);
    }

    [Fact]
    public void A_sky_brings_its_own_light_and_a_colour_is_kept_as_it_is()
    {
        var night = Backdrops.NewLook("starry-night", 1);
        Assert.Equal(LightPresets.Named(LightPresets.MoonlitNight)!.SunEnergy, night.SunEnergy);
        Assert.Equal("starry-night", night.Backdrop);
        Assert.Equal(1, night.TableShade);
        Assert.Equal("#203040", Backdrops.NewLook("#203040", 0).Backdrop);
    }

    [Fact]
    public void An_unknown_backdrop_or_a_surface_out_of_range_falls_back_to_what_the_app_allows()
    {
        Assert.Equal(Backdrops.None, Backdrops.NewLook("a-lost-backdrop", 0).Backdrop);
        Assert.Equal(Lighting.SeeThrough, Backdrops.NewLook("", 9).TableShade);
        Assert.Equal(0, Backdrops.NewLook("", -1).TableShade);
    }

    [Fact]
    public void A_painted_sky_needs_no_picture_and_a_night_one_brings_the_night()
    {
        var painted = Backdrops.All.Where(b => b.Painted > 0).ToList();
        Assert.NotEmpty(painted);
        Assert.All(painted, b => Assert.Equal(Backdrops.NoFile, b.File));
        Assert.Equal(painted.Count, painted.Select(b => b.Painted).Distinct().Count());
        Assert.All(Backdrops.All.Where(b => b.Painted == 0), b => Assert.NotEqual(Backdrops.NoFile, b.File));
        var night = Backdrops.NewLook("blocky-stars", 0);
        Assert.Equal("blocky-stars", night.Backdrop);
        Assert.Equal(LightPresets.Named(LightPresets.MoonlitNight)!.Ambient, night.Ambient);
        Assert.Equal(new Lighting().Ambient, Backdrops.NewLook("painted-sky", 0).Ambient);
    }

    [Fact]
    public void Choosing_a_painted_sky_leaves_a_light_that_follows_the_clock_alone()
    {
        var byClock = new Lighting { FromClock = true, Latitude = 48 };
        var chosen = Backdrops.Chosen(byClock, Backdrops.Find("northern-lights")!);
        Assert.True(chosen.FromClock);
        Assert.Equal(byClock.Ambient, chosen.Ambient);
        Assert.Equal("northern-lights", chosen.Backdrop);
        var pictured = Backdrops.Chosen(byClock, Backdrops.Find("starry-night")!);
        Assert.False(pictured.FromClock);
        var still = Backdrops.Chosen(new Lighting(), Backdrops.Find("northern-lights")!);
        Assert.Equal(LightPresets.Named(LightPresets.MoonlitNight)!.Ambient, still.Ambient);
    }
}
