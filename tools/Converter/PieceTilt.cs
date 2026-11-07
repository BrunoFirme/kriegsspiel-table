namespace KTable.Converter;

public static class PieceTilt
{
    const double HalfTurn = 180, Turn = 360, FlipWindow = 30, Noise = 0.05, TextLiesAt = 90, Degree = Math.PI / 180;

    public sealed record Pose(double Heading, bool Down, double X, double Z)
    {
        public bool Tilted => X != 0 || Z != 0;
    }

    public static Pose Of(double rotX, double rotY, double rotZ, bool model, bool text)
    {
        if (text) return new Pose(rotY - rotZ, false, Clean(rotX - TextLiesAt), 0);
        if (model) return new Pose(rotY, false, Clean(rotX), Clean(rotZ));
        bool fx = Near180(rotX), fz = Near180(rotZ);
        return new Pose(fx ? rotY + HalfTurn : rotY, fx ^ fz, Clean(fx ? HalfTurn - rotX : rotX), Clean(fz ? rotZ - HalfTurn : rotZ));
    }

    public static bool Near180(double a) => Math.Abs(Signed(a - HalfTurn)) < FlipWindow;

    static double Signed(double a)
    {
        a %= Turn;
        if (a >= HalfTurn) a -= Turn;
        if (a < -HalfTurn) a += Turn;
        return a;
    }

    static double Clean(double a) => Math.Abs(Signed(a)) < Noise ? 0 : Math.Round(Signed(a), 3);

    public static (double Low, double High) Span(double across, double deep, double low, double high, double rotX, double rotZ)
    {
        double cx = Math.Cos(rotX * Degree), sx = Math.Sin(rotX * Degree), cz = Math.Cos(rotZ * Degree), sz = Math.Sin(rotZ * Degree);
        double min = double.MaxValue, max = double.MinValue;
        foreach (var x in new[] { -across / 2, across / 2 })
            foreach (var y in new[] { low, high })
                foreach (var z in new[] { -deep / 2, deep / 2 })
                {
                    double y1 = x * sz + y * cz;
                    double up = y1 * cx - z * sx;
                    min = Math.Min(min, up);
                    max = Math.Max(max, up);
                }
        return (min, max);
    }
}
