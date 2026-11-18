using KTable.Core.Model;

namespace KTable.Client;

public sealed class Viewshed
{
    public const int Bearings = 1024, Rings = 320;
    public const byte SeenValue = byte.MaxValue;
    const double Grazing = 0.002;

    readonly float[] _heights = new float[Bearings * Rings];
    readonly byte[] _seen = new byte[Bearings * Rings];

    public Vec2 Center { get; }
    public double Reach { get; }
    public double Eye { get; private set; } = double.NaN;
    public int Looks { get; private set; }

    public byte[] Seen => _seen;

    public Viewshed(Vec2 center, double reach, Func<Vec2, double> ground)
    {
        Center = center;
        Reach = reach;
        Parallel.For(0, Bearings, b =>
        {
            double angle = AngleOf(b), across = Math.Cos(angle), up = Math.Sin(angle);
            for (int r = 0; r < Rings; r++)
            {
                double away = AwayOf(r, reach);
                _heights[b * Rings + r] = (float)ground(new Vec2(center.X + across * away, center.Z + up * away));
            }
        });
    }

    public static double AngleOf(int bearing) => (bearing + 0.5) / Bearings * Math.Tau;

    public static double AwayOf(int ring, double reach) => (ring + 0.5) / Rings * reach;

    public bool Fits(Vec2 center, double reach) => Center == center && Reach == reach;

    public void LookFrom(double eye)
    {
        if (eye == Eye) return;
        Eye = eye;
        Looks++;
        Parallel.For(0, Bearings, b =>
        {
            double horizon = double.NegativeInfinity;
            for (int r = 0; r < Rings; r++)
            {
                double slope = (_heights[b * Rings + r] - eye) / AwayOf(r, Reach);
                _seen[r * Bearings + b] = slope >= horizon - Grazing ? SeenValue : (byte)0;
                horizon = Math.Max(horizon, slope);
            }
        });
    }

    public bool Sees(Vec2 p) => Looked(_seen, Center, Reach, p);

    public static bool Looked(byte[] seen, Vec2 center, double reach, Vec2 p) => CellOf(center, reach, p) is int at && seen[at] != 0;

    public static int? CellOf(Vec2 center, double reach, Vec2 p)
    {
        double dx = p.X - center.X, dz = p.Z - center.Z, away = Math.Sqrt(dx * dx + dz * dz);
        if (away >= reach) return null;
        double turn = Math.Atan2(dz, dx) / Math.Tau;
        int bearing = Math.Clamp((int)((turn - Math.Floor(turn)) * Bearings), 0, Bearings - 1);
        int ring = Math.Clamp((int)(away / reach * Rings), 0, Rings - 1);
        return ring * Bearings + bearing;
    }
}
