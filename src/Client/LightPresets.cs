using KTable.Core.Model;

namespace KTable.Client;

public static class LightPresets
{
    public const string CeilingLight = "Ceiling light", CoolDaylight = "Cool daylight", Sundown = "Sundown", MoonlitNight = "Moonlit night";
    const double Close = 0.03;

    public static IReadOnlyList<(string Name, Lighting Light)> All => new (string, Lighting)[]
    {
        (CeilingLight, new Lighting()),
        ("Warm lamp", new Lighting { Warmth = 0.6, SunEnergy = 0.4, Ambient = 0.7 }),
        (CoolDaylight, new Lighting { Warmth = -0.6, SunEnergy = 0.4, Ambient = 0.72 }),
        (Sundown, new Lighting { Warmth = 0.85, SunEnergy = 0.3, Ambient = 0.5 }),
        ("Overcast", new Lighting { Warmth = -0.2, SunEnergy = 0.15, Ambient = 0.9, Shadows = false }),
        ("Flat (no shading)", new Lighting { SunEnergy = 0, Ambient = 1.0, Shadows = false }),
        (MoonlitNight, new Lighting { Warmth = -0.85, SunEnergy = 0.12, Ambient = 0.3 }),
        ("Dark night", new Lighting { Warmth = -0.5, SunEnergy = 0, Ambient = 0.16, Shadows = false }),
    };

    public static Lighting? Named(string name) => All.FirstOrDefault(p => p.Name == name).Light;

    public static bool Same(Lighting a, Lighting b) => Math.Abs(a.Warmth - b.Warmth) < Close
        && Math.Abs(a.SunEnergy - b.SunEnergy) < Close && Math.Abs(a.Ambient - b.Ambient) < Close && a.Shadows == b.Shadows;

    public static Lighting Lit(Lighting now, Lighting preset) => new()
    {
        SunEnergy = preset.SunEnergy, Ambient = preset.Ambient, Warmth = preset.Warmth, Shadows = preset.Shadows,
        FromClock = false, TableShade = now.TableShade, Backdrop = now.Backdrop, MapStyle = now.MapStyle,
    };
}
