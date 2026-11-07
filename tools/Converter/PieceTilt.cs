namespace KTable.Converter;

public static class PieceTilt
{
    const double HalfTurn = 180, Turn = 360, FlipWindow = 30, Noise = 0.05, TextLiesAt = 90, Degree = Math.PI / 180, Level = 1e-9, Straight = 0.9999;

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

    public static double[]? ValueUp(ModuleObj o)
    {
        if (o.RotationValues.Count == 0) return null;
        var up = DefFactory.UpOf(new ModuleObj.Face("", o.RotX, o.RotY, o.RotZ));
        return DefFactory.UpOf(o.RotationValues.MaxBy(f => DefFactory.UpOf(f).Zip(up, (a, b) => a * b).Sum())!);
    }

    public static Pose OfDie(double heading, bool down, double rotX, double rotZ, double[]? faceUp)
    {
        if (faceUp is not { Length: 3 }) return new Pose(heading, down, 0, 0);
        var (x, _, z) = KTable.Core.Model.DieTurns.RotationOf(ToLevel(faceUp, KTable.Core.Model.DieTurns.UpOf(rotX, rotZ)));
        return new Pose(heading, down, Clean(x), Clean(z));
    }

    static double[] ToLevel(double[] face, double[] v)
    {
        double len = Math.Sqrt(face[0] * face[0] + face[1] * face[1] + face[2] * face[2]);
        if (len < Level) return v;
        double ax = face[0] / len, ay = face[1] / len, az = face[2] / len;
        if (ay > Straight) return v;
        if (ay < -Straight) return [v[0], -v[1], -v[2]];
        double angle = Math.Acos(ay), kl = Math.Sqrt(az * az + ax * ax), kx = -az / kl, kz = ax / kl;
        double c = Math.Cos(angle), s = Math.Sin(angle), dot = kx * v[0] + kz * v[2];
        double[] cross = [-kz * v[1], kz * v[0] - kx * v[2], kx * v[1]];
        return [v[0] * c + cross[0] * s + kx * dot * (1 - c), v[1] * c + cross[1] * s, v[2] * c + cross[2] * s + kz * dot * (1 - c)];
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
