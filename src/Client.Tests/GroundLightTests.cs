using KTable.Core.Model;

namespace KTable.Client.Tests;

public class GroundLightTests
{
    [Fact]
    public void Under_the_ceiling_light_flat_ground_shows_its_own_colour()
    {
        var l = new Lighting();
        Assert.Equal(1, GroundLight.Level(l), 9);
        Assert.Equal(1 / GroundLight.OnFlat(l), GroundLight.Share(l), 9);
    }

    [Fact]
    public void Every_daylight_preset_shows_the_ground_at_full_and_the_nights_dim_it()
    {
        foreach (var (name, light) in LightPresets.All)
        {
            double level = GroundLight.Level(light);
            if (name is LightPresets.Sundown or LightPresets.MoonlitNight or "Dark night") Assert.InRange(level, 0.1, 0.85);
            else Assert.Equal(1, level, 9);
        }
    }

    [Fact]
    public void A_low_sun_is_lifted_so_flat_ground_keeps_its_light()
    {
        var noon = new Lighting { SunEnergy = 0.6, Ambient = 0.2 };
        var low = new Lighting { SunEnergy = 0.6, Ambient = 0.2, SunHeight = 25 };
        Assert.Equal(GroundLight.OnFlat(noon), GroundLight.OnFlat(low), 6);
        Assert.Equal(0.2, GroundLight.OnFlat(new Lighting { SunEnergy = 0, Ambient = 0.2 }), 9);
    }
}
