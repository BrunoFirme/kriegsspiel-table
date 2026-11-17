using System.Globalization;
using KTable.Core.Model;
using Light = (double hour, double sun, double ambient, double warmth);

namespace KTable.Client;

public sealed record SkyTurn(double SunFrom, double SunHeight, double MoonFrom, double MoonHeight, double Dark, double Glow, double Turn, double Pole, double Moonlight);

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

    public const double RisesFrom = 90, SetsFrom = 270, HighestAtNoon = 55, UsualSunrise = 6, UsualSunset = 20;
    public const double HoursInDay = 24;
    const double Lit = 0.01, SolarNoon = 12, LeastDay = 1, Tilt = 23.44, DaysInYear = 365, DegreesAnHour = 15, DaysFromSolsticeToNewYear = 10, LongestDay = 172;

    public static double HourOf(GameClock clock) => clock.Cal.HourOf(clock.Now);

    static int? DayOfYear(GameClock clock) => (int)Math.Round(LongestDay + clock.Cal.YearTurn(clock.Now) * DaysInYear) % (int)DaysInYear;

    static double SunOverLatitude(int dayOfYear) => -Tilt * Math.Cos(2 * Math.PI * (dayOfYear + DaysFromSolsticeToNewYear) / DaysInYear);

    public static double HalfDay(double latitude, int dayOfYear)
    {
        double lat = Math.Clamp(latitude, -Lighting.MostLatitude, Lighting.MostLatitude) * Math.PI / 180, sun = SunOverLatitude(dayOfYear) * Math.PI / 180;
        double hours = Math.Acos(Math.Clamp(-Math.Tan(lat) * Math.Tan(sun), -1, 1)) * 180 / Math.PI / DegreesAnHour;
        return Math.Clamp(hours, LeastDay / 2, SolarNoon - LeastDay / 2);
    }

    public static (double Rise, double Set) Daylight(GameClock clock, Lighting l)
    {
        if (l.Latitude is { } latitude && DayOfYear(clock) is { } day)
        {
            double half = HalfDay(latitude, day);
            return (SolarNoon - half, SolarNoon + half);
        }
        double rise = Math.Clamp(l.Sunrise ?? UsualSunrise, 0, HoursInDay - LeastDay);
        return (rise, Math.Clamp(l.Sunset ?? UsualSunset, rise + LeastDay, HoursInDay));
    }

    static double OnTheUsualDay(double hour, (double Rise, double Set) day)
    {
        if (hour <= day.Rise) return day.Rise <= 0 ? UsualSunrise : UsualSunrise * hour / day.Rise;
        if (hour >= day.Set) return day.Set >= HoursInDay ? UsualSunset : UsualSunset + (HoursInDay - UsualSunset) * (hour - day.Set) / (HoursInDay - day.Set);
        return UsualSunrise + (UsualSunset - UsualSunrise) * (hour - day.Rise) / (day.Set - day.Rise);
    }

    static (double Highest, bool PassesNorth) NoonSun(GameClock clock, Lighting l)
    {
        if (l.Latitude is not { } latitude || DayOfYear(clock) is not { } day) return (HighestAtNoon, false);
        double sun = SunOverLatitude(day);
        return (Math.Clamp(Lighting.HighestSun - Math.Abs(latitude - sun), Lighting.LowestSun, Lighting.HighestSun), latitude < sun);
    }

    public static (double from, double height) SunAt(double hour, double highest = HighestAtNoon, bool passesNorth = false)
    {
        double k = Math.Clamp((hour - UsualSunrise) / (UsualSunset - UsualSunrise), 0, 1);
        double from = passesNorth ? Calibration.Turned(RisesFrom - (SetsFrom - RisesFrom) * k) : RisesFrom + (SetsFrom - RisesFrom) * k;
        return (from, Math.Max(Lighting.LowestSun, highest * Math.Sin(Math.PI * k)));
    }

    public static Lighting At(GameClock clock, Lighting basis)
    {
        double h = OnTheUsualDay(Math.Clamp(HourOf(clock), 0, HoursInDay), Daylight(clock, basis));
        var noon = NoonSun(clock, basis);
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
        (l.SunFrom, l.SunHeight) = SunAt(h, noon.Highest, noon.PassesNorth);
        return l;
    }

    public static string HourWords(double hour)
    {
        const int MinutesInHour = 60;
        int minutes = (int)Math.Round(Math.Clamp(hour, 0, HoursInDay) * MinutesInHour);
        return string.Create(CultureInfo.InvariantCulture, $"{minutes / MinutesInHour:00}:{minutes % MinutesInHour:00}");
    }

    public static double? HourTyped(string text)
    {
        var typed = text.Trim().Replace('.', ':').Replace('h', ':').Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (typed.Length is < 1 or > 2 || !int.TryParse(typed[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hours)) return null;
        int minutes = 0;
        if (typed.Length == 2 && !int.TryParse(typed[1], NumberStyles.None, CultureInfo.InvariantCulture, out minutes)) return null;
        return hours is >= 0 and <= 24 && minutes is >= 0 and < 60 && hours + minutes / 60.0 <= HoursInDay ? hours + minutes / 60.0 : null;
    }

    public static string Describe(GameClock clock, Lighting? look = null)
    {
        double h = look == null ? HourOf(clock) : OnTheUsualDay(HourOf(clock), Daylight(clock, look));
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

    const double StarsOut = -10, StarsGone = -2, GlowFrom = -14, GlowFull = -4, GlowFades = 6, GlowGone = 20, GlowFadesOfNoon = 0.3, GlowGoneOfNoon = 0.8;
    const double ClockMoon = 0.5, MoonFullyUp = 25, ShallowestNight = 10, ShallowestDip = 1, NightDepthPerHour = 5, UsualLatitude = 45, Hidden = -30, GlowSunHeight = 2;
    const double NightAmbient = 0.34, DuskAmbient = 0.5, WarmFrom = 0.3, WarmFull = 0.8, LampFrom = 0.6, LampFull = 0.8, FullTurn = 360;

    static double Ease(double from, double to, double v)
    {
        double t = Math.Clamp((v - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    static double Arc(double start, double k, bool passesNorth) => Calibration.Turned(start + (passesNorth ? -HalfTurn : HalfTurn) * k);

    public static SkyTurn Sky(GameClock? clock, Lighting l)
    {
        double pole = l.Latitude ?? UsualLatitude;
        if (!l.FromClock || clock == null)
        {
            double unlit = 1 - Ease(NightAmbient, DuskAmbient, l.Ambient);
            double warm = Ease(WarmFrom, WarmFull, l.Warmth) * (1 - Ease(LampFrom, LampFull, l.Ambient)) * (1 - unlit);
            bool night = unlit > 0.5;
            double sun = night ? Hidden : l.SunHeight + (GlowSunHeight - l.SunHeight) * warm;
            bool moon = night && l.SunEnergy >= Lit;
            return new SkyTurn(l.SunFrom, sun, l.SunFrom, moon ? l.SunHeight : Hidden, unlit, warm, 0, pole, moon ? Math.Clamp(l.SunEnergy / MoonAtMost, 0, 1) : 0);
        }
        var day = Daylight(clock, l);
        var noon = NoonSun(clock, l);
        double hour = Math.Clamp(HourOf(clock), 0, HoursInDay), from, height;
        if (hour >= day.Rise && hour <= day.Set)
        {
            double k = (hour - day.Rise) / (day.Set - day.Rise);
            (from, height) = (Arc(RisesFrom, k, noon.PassesNorth), noon.Highest * Math.Sin(Math.PI * k));
        }
        else
        {
            double dark = HoursInDay - (day.Set - day.Rise), k = (hour > day.Set ? hour - day.Set : hour + HoursInDay - day.Set) / dark;
            double depth = l.Latitude is { } latitude && DayOfYear(clock) is { } date
                ? Math.Clamp(Lighting.HighestSun - Math.Abs(latitude + SunOverLatitude(date)), ShallowestDip, Lighting.HighestSun)
                : Math.Min(Math.Max(Lighting.HighestSun - noon.Highest, ShallowestNight), dark * NightDepthPerHour);
            (from, height) = (Arc(SetsFrom, k, noon.PassesNorth), -depth * Math.Sin(Math.PI * k));
        }
        double fades = Math.Min(GlowFades, noon.Highest * GlowFadesOfNoon), gone = Math.Min(GlowGone, noon.Highest * GlowGoneOfNoon);
        return new SkyTurn(from, height, Calibration.Turned(from + HalfTurn), -height, 1 - Ease(StarsOut, StarsGone, height),
            Ease(GlowFrom, GlowFull, height) * (1 - Ease(fades, gone, height)), hour / HoursInDay * FullTurn, pole, ClockMoon * Ease(0, MoonFullyUp, -height));
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
