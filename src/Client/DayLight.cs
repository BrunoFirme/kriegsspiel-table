using System.Globalization;
using KTable.Core.Model;
using Light = (double hour, double sun, double ambient, double warmth);

namespace KTable.Client;

public static class DayLight
{
    static readonly Light SmallHours = (0.0, 0.00, 0.30, -0.55);
    static readonly Light Sunrise = (6.0, 0.18, 0.52, 0.85);
    static readonly Light Noon = (13.0, 0.55, 0.80, 0.00);
    static readonly Light Sunset = (20.0, 0.12, 0.46, 0.85);
    static readonly Light MidnightAgain = (24.0, SmallHours.sun, SmallHours.ambient, SmallHours.warmth);

    static readonly Light[] Day =
    {
        SmallHours,
        (4.5,  0.00, 0.32, -0.50),
        Sunrise,
        (7.5,  0.40, 0.70,  0.45),
        (10.0, 0.50, 0.78,  0.10),
        Noon,
        (16.0, 0.50, 0.78,  0.12),
        (18.5, 0.34, 0.66,  0.55),
        Sunset,
        (21.5, 0.00, 0.32, -0.45),
        MidnightAgain,
    };

    public const double RisesFrom = 90, SetsFrom = 270, HighestAtNoon = 55;
    const double Lit = 0.01;

    public static double HourOf(GameClock clock) =>
        DateTime.TryParse(clock.GameTime, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)
            ? t.Hour + t.Minute / 60.0
            : 12.0;

    public static (double from, double height) SunAt(double hour)
    {
        double k = Math.Clamp((hour - Sunrise.hour) / (Sunset.hour - Sunrise.hour), 0, 1);
        return (RisesFrom + (SetsFrom - RisesFrom) * k, Math.Max(Lighting.LowestSun, HighestAtNoon * Math.Sin(Math.PI * k)));
    }

    public static Lighting At(GameClock clock, Lighting basis)
    {
        double h = Math.Clamp(HourOf(clock), 0, 24);
        int i = 0;
        while (i < Day.Length - 2 && Day[i + 1].hour < h) i++;
        var (h0, s0, a0, w0) = Day[i];
        var (h1, s1, a1, w1) = Day[i + 1];
        double k = h1 - h0 <= 0 ? 0 : Math.Clamp((h - h0) / (h1 - h0), 0, 1);
        var l = basis.Clone();
        l.SunEnergy = s0 + (s1 - s0) * k;
        l.Ambient = a0 + (a1 - a0) * k;
        l.Warmth = w0 + (w1 - w0) * k;
        l.FromClock = true;
        (l.SunFrom, l.SunHeight) = SunAt(h);
        return l;
    }

    public static string Describe(GameClock clock)
    {
        double h = HourOf(clock);
        return h switch
        {
            < 5 => "night",
            < 7 => "first light",
            < 11 => "morning",
            < 14 => "midday",
            < 17 => "afternoon",
            < 19.5 => "evening",
            < 21.5 => "dusk",
            _ => "night",
        };
    }

    const double HalfTurn = 180;

    public static (double pitch, double yaw) Aim(double from, double height, double north)
    {
        double h = Math.Clamp(double.IsFinite(height) ? height : Lighting.Overhead, Lighting.LowestSun, Lighting.HighestSun);
        double f = double.IsFinite(from) ? from : Lighting.OverheadFrom;
        return (-h, Calibration.Turned(HalfTurn - f - north));
    }

    const double MoonAtMost = 0.25, MoonCold = -0.55;

    public static bool Moonlit(Lighting l) => l.SunEnergy is >= Lit and <= MoonAtMost && l.Warmth <= MoonCold;

    public static string SunWords(Lighting l)
    {
        if (l.SunEnergy < Lit) return "";
        var body = Moonlit(l) ? "moon" : "sun";
        return l.SunHeight >= Lighting.Overhead ? $"{body} overhead"
            : string.Create(CultureInfo.InvariantCulture, $"{body} {Wind.Point(l.SunFrom)} {l.SunHeight:0}°");
    }
}
