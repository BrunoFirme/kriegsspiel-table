namespace KTable.Core.Model;

public static class CrowdSpots
{
    public const double RoundLeastM = 7.5, JitterM = 0.2;
    public const double ApartM = 1.2, EdgeM = 4, BayM = 4.5, BayInsetM = 2.5, LeastLaneM = 0.8, LaneSpreadM = 1.0, LaneEaseM = 6;
    const long Prime = 7919;
    const int MostSpots = 20000;

    public static Vec2[] Of(CrowdPlace place, double upm)
    {
        double step = ApartM * upm;
        var spots = new List<Vec2>();
        if (place.Shape is { Count: >= 3 } shape)
        {
            var (low, high) = Box(shape);
            double edge = Math.Min(high.X - low.X, high.Z - low.Z) > 4 * EdgeM * upm ? EdgeM * upm : 0;
            int row = 0;
            for (double z = low.Z + step / 2; z < high.Z && spots.Count < MostSpots; z += step * Rows, row++)
                for (double x = low.X + step / 2 + (row % 2) * step / 2; x < high.X && spots.Count < MostSpots; x += step)
                {
                    var p = Jittered(x, z, upm);
                    if (Inside(shape, p) && (edge == 0 || Edge(shape, p) >= edge)) spots.Add(p);
                }
        }
        if (spots.Count > 0) return spots.ToArray();
        double reach = Math.Max(Math.Max(place.ReachM, RoundLeastM) * upm, step);
        int ring = 0;
        for (double z = -reach; z <= reach; z += step * Rows, ring++)
            for (double x = -reach + (ring % 2) * step / 2; x <= reach; x += step)
                if (x * x + z * z <= reach * reach) spots.Add(Jittered(place.At.X + x, place.At.Z + z, upm));
        return spots.Count > 0 ? spots.ToArray() : [place.At];
    }

    public static Vec2 For(Vec2[] spots, int id)
    {
        long n = spots.Length, step = n % Prime == 0 ? Prime + 1 : Prime;
        return spots[(int)((Math.Abs((long)id) * step) % n)];
    }

    public static (Vec2 At, Vec2 Along)? Bay(CrowdPlace place, int n, double upm)
    {
        if (place.Shape is not { Count: >= 3 } shape) return null;
        var bays = new List<(Vec2, Vec2)>();
        double gap = BayM * upm, inset = BayInsetM * upm;
        for (int k = 0; k < shape.Count; k++)
        {
            Vec2 a = shape[k], b = shape[(k + 1) % shape.Count], d = b - a;
            double length = d.Length;
            if (length < gap) continue;
            var along = new Vec2(d.X / length, d.Z / length);
            var inward = new Vec2(-along.Z, along.X);
            if (!Inside(shape, Moved(a, d, 0.5, inward, inset))) inward = new Vec2(-inward.X, -inward.Z);
            for (double s = gap; s <= length - gap; s += gap)
            {
                var at = Moved(a, along, s, inward, inset);
                if (Inside(shape, at)) bays.Add((at, along));
            }
        }
        if (bays.Count == 0) return null;
        long m = bays.Count, step = m % Prime == 0 ? Prime + 1 : Prime;
        return bays[(int)((Math.Abs((long)n) * step) % m)];
    }

    public static Vec2 Laned(Vec2 point, double heading, double along, double length, int seed, double upm)
    {
        double side = (seed & 1) == 0 ? 1 : -1, lane = (LeastLaneM + LaneSpreadM * ((uint)seed % 101) / 100.0) * side * upm;
        double ease = Math.Clamp(Math.Min(along, length - along) / (LaneEaseM * upm), 0, 1);
        return new Vec2(point.X + Math.Cos(heading) * lane * ease, point.Z - Math.Sin(heading) * lane * ease);
    }

    const double Rows = 0.866;

    static Vec2 Jittered(double x, double z, double upm)
    {
        uint h = unchecked((uint)(x * 73856093) ^ (uint)(z * 19349663) * 83492791u);
        double a = (h & 0xffff) / 65536.0 * Math.Tau, d = (h >> 16) / 65536.0 * JitterM * upm;
        return new Vec2(x + Math.Cos(a) * d, z + Math.Sin(a) * d);
    }

    static Vec2 Moved(Vec2 a, Vec2 d, double t, Vec2 n, double m) => new(a.X + d.X * t + n.X * m, a.Z + d.Z * t + n.Z * m);

    static (Vec2 Low, Vec2 High) Box(IReadOnlyList<Vec2> shape) =>
        (new Vec2(shape.Min(p => p.X), shape.Min(p => p.Z)), new Vec2(shape.Max(p => p.X), shape.Max(p => p.Z)));

    public static bool Inside(IReadOnlyList<Vec2> shape, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = shape.Count - 1; i < shape.Count; j = i++)
        {
            Vec2 a = shape[i], b = shape[j];
            if (a.Z > p.Z != b.Z > p.Z && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
        }
        return inside;
    }

    static double Edge(IReadOnlyList<Vec2> shape, Vec2 p)
    {
        double least = double.MaxValue;
        for (int i = 0; i < shape.Count; i++)
        {
            Vec2 a = shape[i], d = shape[(i + 1) % shape.Count] - a;
            double t = Math.Clamp(((p.X - a.X) * d.X + (p.Z - a.Z) * d.Z) / Math.Max(d.X * d.X + d.Z * d.Z, 1e-12), 0, 1);
            least = Math.Min(least, (Moved(a, d, t, d, 0) - p).Length);
        }
        return least;
    }
}
