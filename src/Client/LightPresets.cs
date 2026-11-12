using KTable.Core.Model;

namespace KTable.Client;

public static class LightPresets
{
    public const string CeilingLight = "Ceiling light", CoolDaylight = "Cool daylight", Sundown = "Sundown", MoonlitNight = "Moonlit night";
    public const string MorningSun = "Morning sun", AfternoonSun = "Afternoon sun";
    const double Close = 0.03, CloseDegrees = 1, HalfTurn = 180;
    const double East = 90, West = 270, LowSun = 20;

    public static IReadOnlyList<(string Name, Lighting Light)> All => new (string, Lighting)[]
    {
        (CeilingLight, new Lighting()),
        ("Warm lamp", new Lighting { Warmth = 0.7, SunEnergy = 0.6, Ambient = 0.85 }),
        (CoolDaylight, new Lighting { Warmth = -0.6, SunEnergy = 0.6, Ambient = 0.9 }),
        (MorningSun, new Lighting { Warmth = 0.45, SunEnergy = 0.6, Ambient = 0.6, SunFrom = East, SunHeight = LowSun }),
        (AfternoonSun, new Lighting { Warmth = 0.55, SunEnergy = 0.6, Ambient = 0.6, SunFrom = West, SunHeight = LowSun }),
        (Sundown, new Lighting { Warmth = 0.85, SunEnergy = 0.3, Ambient = 0.5 }),
        ("Overcast", new Lighting { Warmth = -0.2, SunEnergy = 0.2, Ambient = 1.1, Shadows = false }),
        ("Flat (no shading)", new Lighting { SunEnergy = 0, Ambient = 1.0, Shadows = false }),
        (MoonlitNight, new Lighting { Warmth = -0.85, SunEnergy = 0.12, Ambient = 0.3 }),
        ("Dark night", new Lighting { Warmth = -0.5, SunEnergy = 0, Ambient = 0.16, Shadows = false }),
    };

    public static Lighting? Named(string name) => All.FirstOrDefault(p => p.Name == name).Light;

    public static bool Same(Lighting a, Lighting b) => Math.Abs(a.Warmth - b.Warmth) < Close
        && Math.Abs(a.SunEnergy - b.SunEnergy) < Close && Math.Abs(a.Ambient - b.Ambient) < Close && a.Shadows == b.Shadows
        && Math.Abs(a.SunHeight - b.SunHeight) < CloseDegrees && Apart(a.SunFrom, b.SunFrom) < CloseDegrees;

    static double Apart(double a, double b) => HalfTurn - Math.Abs(Calibration.Turned(a - b) - HalfTurn);

    public static Lighting Lit(Lighting now, Lighting preset)
    {
        var l = Toned(now, preset);
        l.SunFrom = preset.SunFrom;
        l.SunHeight = preset.SunHeight;
        return l;
    }

    public static Lighting Toned(Lighting now, Lighting preset)
    {
        var l = now.Clone();
        l.SunEnergy = preset.SunEnergy;
        l.Ambient = preset.Ambient;
        l.Warmth = preset.Warmth;
        l.Shadows = preset.Shadows;
        l.FromClock = false;
        return l;
    }
}
