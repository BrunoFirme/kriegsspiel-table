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
}
