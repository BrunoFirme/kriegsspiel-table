using System;
using KTable.Core.Model;

namespace KTable.Client;

public static class GroundLight
{
    public const double FullAt = 1, MostSun = 3, MostLift = 3, Lit = 0.01;

    public static double FlatLift(double height) =>
        Math.Min(MostLift, Math.Sin(Radians(Lighting.Overhead)) / Math.Sin(Radians(Height(height))));

    public static double SunEnergy(Lighting l) => l.SunEnergy > Lit ? Math.Clamp(l.SunEnergy, 0, MostSun) * FlatLift(l.SunHeight) : 0;

    public static double OnFlat(Lighting l) => Math.Clamp(l.Ambient, 0, MostSun) + SunEnergy(l) * Math.Sin(Radians(Height(l.SunHeight)));

    public static double Share(Lighting l) => 1 / Math.Max(FullAt, OnFlat(l));

    public static double Level(Lighting l) => OnFlat(l) * Share(l);

    static double Height(double height) => Math.Clamp(double.IsFinite(height) ? height : Lighting.Overhead, Lighting.LowestSun, Lighting.HighestSun);

    static double Radians(double degrees) => degrees * Math.PI / 180;
}
