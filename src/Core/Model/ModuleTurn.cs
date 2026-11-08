namespace KTable.Core.Model;

public readonly record struct ModuleTurn(double M00, double M01, double M02, double M10, double M11, double M12, double M20, double M21, double M22)
{
    const double Degree = Math.PI / 180, Gimbal = 0.999999;

    public static readonly ModuleTurn None = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    public static ModuleTurn Of(double x, double y, double z) => About(0, 1, 0, y).Then(About(1, 0, 0, x)).Then(About(0, 0, 1, z));

    static ModuleTurn About(double ax, double ay, double az, double degrees)
    {
        double c = Math.Cos(degrees * Degree), s = Math.Sin(degrees * Degree);
        if (ax != 0) return new(1, 0, 0, 0, c, -s, 0, s, c);
        if (ay != 0) return new(c, 0, s, 0, 1, 0, -s, 0, c);
        return new(c, -s, 0, s, c, 0, 0, 0, 1);
    }

    public ModuleTurn Then(ModuleTurn b) => new(
        M00 * b.M00 + M01 * b.M10 + M02 * b.M20, M00 * b.M01 + M01 * b.M11 + M02 * b.M21, M00 * b.M02 + M01 * b.M12 + M02 * b.M22,
        M10 * b.M00 + M11 * b.M10 + M12 * b.M20, M10 * b.M01 + M11 * b.M11 + M12 * b.M21, M10 * b.M02 + M11 * b.M12 + M12 * b.M22,
        M20 * b.M00 + M21 * b.M10 + M22 * b.M20, M20 * b.M01 + M21 * b.M11 + M22 * b.M21, M20 * b.M02 + M21 * b.M12 + M22 * b.M22);

    public (double X, double Y, double Z) Apply(double x, double y, double z) =>
        (M00 * x + M01 * y + M02 * z, M10 * x + M11 * y + M12 * z, M20 * x + M21 * y + M22 * z);

    public (double X, double Y, double Z) Undo(double x, double y, double z) =>
        (M00 * x + M10 * y + M20 * z, M01 * x + M11 * y + M21 * z, M02 * x + M12 * y + M22 * z);

    public double UpY => M11;

    public (double X, double Y, double Z) Euler()
    {
        double x = Math.Asin(Math.Clamp(-M12, -1, 1));
        if (Math.Abs(M12) > Gimbal) return (x / Degree, Math.Atan2(-M20, M00) / Degree, 0);
        return (x / Degree, Math.Atan2(M02, M22) / Degree, Math.Atan2(M10, M11) / Degree);
    }
}
