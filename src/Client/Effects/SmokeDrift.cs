using KTable.Core.Model;

namespace KTable.Client.Effects;

public readonly record struct SmokeAir(double WindStrength, double UnitsPerMetre)
{
    public static SmokeAir Of(Wind w, Calibration? c) => new(w.Strength, SmokeDrift.UnitsPerMetre(c));
}

public readonly record struct Plume(float Reach, float Spread);

public enum SmokeHeat { None, Cold, Hot }

public static class SmokeDrift
{
    public const double UncalibratedMetresPerUnit = 30;
    public const float StillDrift = 0.03f;
    public const int MostEmitters = 24, MostParticles = 2400;
    public const float MostSeconds = 120;
    public const double EffectTimeScale = 3;
    public const double DefaultWindStrength = 1;

    static readonly double[] BeaufortMiddles = { 0.3, 2.4, 6.75, 9.4, 12.35, 17.2 };

    const double MetresPerFile = 0.56, FiringRanks = 2, MusketChargeGrams = 6.5, MusketShotsPerMinute = 2.5;
    const double GunChargeGrams = 910, GunShotsPerMinute = 2, MetresPerGun = 15;
    const double SolidShare = 0.56, AirborneShare = 0.5;
    const double ExtinctionPerGram = 3, SeenOpticalDepth = 0.1;
    const double RoundsCarried = 60;
    const double BriggsNeutralA = 0.08, BriggsNeutralB = 0.0001;
    const double MostThinning = 0.3, BuoyantRiseWind = 5;
    static readonly double GaussianWidth = Math.Sqrt(2 * Math.PI);

    public static double WindMetresPerSecond(double strength)
    {
        double s = Math.Clamp(strength, 0, BeaufortMiddles.Length - 1);
        int lo = Math.Min((int)s, BeaufortMiddles.Length - 2);
        return BeaufortMiddles[lo] + (BeaufortMiddles[lo + 1] - BeaufortMiddles[lo]) * (s - lo);
    }

    public static double ThinShare(double metresPerSecond) => Math.Clamp(BeaufortMiddles[1] / Math.Max(metresPerSecond, 0.01), MostThinning, 1);

    public static double RiseShare(double metresPerSecond) => BuoyantRiseWind / (BuoyantRiseWind + Math.Max(metresPerSecond, 0));

    public static float ScreenWind(SmokeAir air) => (float)(WindMetresPerSecond(air.WindStrength) * air.UnitsPerMetre * EffectTimeScale);

    public static float Ride(SmokeAir air, float drift) => StillDrift + ScreenWind(air) * drift;

    public static double UnitsPerMetre(Calibration? c) => c?.UnitsPerMeter is { } u && u > 0 ? u : 1 / UncalibratedMetresPerUnit;

    public static double SmokePerSecondPerMetre(string kind) => kind == "cannon"
        ? GunChargeGrams * SolidShare * AirborneShare * GunShotsPerMinute / 60 / MetresPerGun
        : FiringRanks / MetresPerFile * MusketChargeGrams * SolidShare * AirborneShare * MusketShotsPerMinute / 60;

    public static double FiringSeconds => RoundsCarried / MusketShotsPerMinute * 60;

    public static double Spreading(double metres) => BriggsNeutralA * metres / Math.Sqrt(1 + BriggsNeutralB * metres);

    static double DistanceAtSpreading(double sigma)
    {
        double a2 = BriggsNeutralA * BriggsNeutralA, s2 = sigma * sigma;
        return (s2 * BriggsNeutralB + Math.Sqrt(s2 * s2 * BriggsNeutralB * BriggsNeutralB + 4 * a2 * s2)) / (2 * a2);
    }

    public static double ReachMetres(string kind, double frontMetres, double amount, double windStrength)
    {
        double u = WindMetresPerSecond(windStrength);
        double thickness = ExtinctionPerGram * SmokePerSecondPerMetre(kind) * Math.Max(amount, 0) / (u * SeenOpticalDepth);
        if (thickness <= 1 || frontMetres <= 0) return 0;
        double seenWhileSpread = frontMetres * (thickness - 1) / GaussianWidth;
        return Math.Min(DistanceAtSpreading(seenWhileSpread), u * FiringSeconds);
    }

    public static Plume Of(string kind, double frontUnits, double amount, SmokeAir air)
    {
        double front = frontUnits / air.UnitsPerMetre;
        double reach = ReachMetres(kind, front, amount, air.WindStrength);
        return Over((float)(reach * air.UnitsPerMetre), air);
    }

    public static Plume Over(float reachUnits, SmokeAir air) => new(reachUnits, (float)(GaussianWidth * Spreading(reachUnits / air.UnitsPerMetre) * air.UnitsPerMetre));
}
