namespace KTable.Core.Model;

public sealed class Ground
{
    public static readonly Ground Flat = new(Array.Empty<SheetDef>(), _ => null);

    const int Probes = 2;
    const double ProbeStep = 0.255;

    readonly List<(SheetDef sheet, ReliefGrid grid, double scale)> _reliefs = new();
    readonly Dictionary<(long, long), List<int>> _areas = new();
    readonly double _area = 1;

    public Ground(IEnumerable<SheetDef> sheets, Func<SheetDef, ReliefGrid?> grid)
    {
        foreach (var s in sheets.OrderByDescending(s => s.Z))
            if (grid(s) is { } g) _reliefs.Add((s, g, FullWidth(s) * (s.Relief is { Length: > 0 } ? s.Height : 1)));
        Highest = _reliefs.Count == 0 ? 0 : _reliefs.Max(r => r.grid.Highest * r.scale);
        if (_reliefs.Count == 0) return;
        _area = Math.Max(1, _reliefs.Average(r => Math.Max(r.sheet.Size.W, r.sheet.Size.H)));
        for (int i = 0; i < _reliefs.Count; i++)
        {
            var corners = new[] { (0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0) }.Select(c => Corner(_reliefs[i].sheet, c.Item1, c.Item2)).ToList();
            var (x0, z0) = AreaOf(new Vec2(corners.Min(c => c.X), corners.Min(c => c.Z)));
            var (x1, z1) = AreaOf(new Vec2(corners.Max(c => c.X), corners.Max(c => c.Z)));
            for (long x = x0; x <= x1; x++)
                for (long z = z0; z <= z1; z++)
                {
                    if (!_areas.TryGetValue((x, z), out var list)) _areas[(x, z)] = list = new();
                    list.Add(i);
                }
        }
    }

    static Vec2 Corner(SheetDef s, double fu, double fv)
    {
        var c = s.Crop ?? SheetGeometry.Whole;
        return FromUv(s, c.X + fu * c.W, c.Y + fv * c.H);
    }

    (long x, long z) AreaOf(Vec2 p) => ((long)Math.Floor(p.X / _area), (long)Math.Floor(p.Z / _area));

    IEnumerable<(SheetDef sheet, ReliefGrid grid, double scale)> Covering(Vec2 p)
    {
        if (!_areas.TryGetValue(AreaOf(p), out var list)) yield break;
        foreach (var i in list) yield return _reliefs[i];
    }

    public bool Any => _reliefs.Count > 0;
    public double Highest { get; }

    public static double FullWidth(SheetDef s) => s.Size.W / Math.Max(1e-6, (s.Crop ?? SheetGeometry.Whole).W);

    public static (double u, double v)? Uv(SheetDef s, Vec2 p)
    {
        var (lx, lz) = Local(s, p);
        if (Math.Abs(lx) > s.Size.W / 2 || Math.Abs(lz) > s.Size.H / 2) return null;
        var c = s.Crop ?? SheetGeometry.Whole;
        return (c.X + (lx / s.Size.W + 0.5) * c.W, c.Y + (lz / s.Size.H + 0.5) * c.H);
    }

    public static (double x, double z) Local(SheetDef s, Vec2 p)
    {
        double wx = p.X - s.Pos.X, wz = -(p.Z - s.Pos.Z);
        double a = -(180 - s.RotDeg) * Math.PI / 180;
        double c = Math.Cos(a), n = Math.Sin(a);
        return (wx * c + wz * n, -wx * n + wz * c);
    }

    public static Vec2 FromUv(SheetDef s, double u, double v)
    {
        var c = s.Crop ?? SheetGeometry.Whole;
        double lx = ((u - c.X) / c.W - 0.5) * s.Size.W, lz = ((v - c.Y) / c.H - 0.5) * s.Size.H;
        double a = (180 - s.RotDeg) * Math.PI / 180;
        double cs = Math.Cos(a), sn = Math.Sin(a);
        double wx = lx * cs + lz * sn, wz = -lx * sn + lz * cs;
        return new Vec2(s.Pos.X + wx, s.Pos.Z - wz);
    }

    public SheetDef? ReliefAt(Vec2 p)
    {
        foreach (var r in Covering(p))
            if (Uv(r.sheet, p) != null) return r.sheet;
        return null;
    }

    public double At(Vec2 p)
    {
        foreach (var r in Covering(p))
            if (Uv(r.sheet, p) is var (u, v)) return r.grid.HeightAt(u, v) * r.scale;
        return 0;
    }

    public byte KindAt(Vec2 p)
    {
        foreach (var r in Covering(p))
            if (Uv(r.sheet, p) is var (u, v)) return r.grid.KindAt(u, v);
        return GroundKinds.None;
    }

    public double Under(Vec2 pos, double rotDeg, double width, double depth)
    {
        if (!Any) return 0;
        var rad = rotDeg * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        double top = double.MinValue;
        for (int i = -Probes; i <= Probes; i++)
            for (int j = -Probes; j <= Probes; j++)
            {
                var q = pos + new Vec2(along.X * i * width * ProbeStep + back.X * j * depth * ProbeStep, along.Z * i * width * ProbeStep + back.Z * j * depth * ProbeStep);
                top = Math.Max(top, At(q));
            }
        return top;
    }

    public const double MostTiltDeg = 30;
    const double SeatEdge = 0.5;

    public readonly record struct Seat(double Y, double UpX, double UpY, double UpZ)
    {
        public double TiltDeg => Math.Acos(Math.Clamp(UpY, -1, 1)) * 180 / Math.PI;
    }

    public Seat SeatOn(Vec2 pos, double rotDeg, double width, double depth)
    {
        if (!Any) return new Seat(0, 0, 1, 0);
        var rad = rotDeg * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        var pts = new List<(double x, double z, double h)>(9);
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
            {
                double dx = along.X * i * width * SeatEdge + back.X * j * depth * SeatEdge;
                double dz = along.Z * i * width * SeatEdge + back.Z * j * depth * SeatEdge;
                pts.Add((dx, dz, At(new Vec2(pos.X + dx, pos.Z + dz))));
            }
        var (a, b, c) = Plane(pts);
        double slope = Math.Sqrt(a * a + b * b), most = Math.Tan(MostTiltDeg * Math.PI / 180);
        if (slope > most) { a *= most / slope; b *= most / slope; }
        double lift = pts.Max(p => p.h - (a * p.x + b * p.z + c));
        double len = Math.Sqrt(a * a + b * b + 1);
        return new Seat(c + lift, -a / len, 1 / len, -b / len);
    }

    static (double a, double b, double c) Plane(List<(double x, double z, double h)> pts)
    {
        double sxx = 0, sxz = 0, szz = 0, sx = 0, sz = 0, sh = 0, sxh = 0, szh = 0;
        foreach (var (x, z, h) in pts) { sxx += x * x; sxz += x * z; szz += z * z; sx += x; sz += z; sh += h; sxh += x * h; szh += z * h; }
        double n = pts.Count;
        double det = sxx * (szz * n - sz * sz) - sxz * (sxz * n - sz * sx) + sx * (sxz * sz - szz * sx);
        if (Math.Abs(det) < 1e-12) return (0, 0, pts.Max(p => p.h));
        double Det(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22) =>
            m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
        double a = Det(sxh, sxz, sx, szh, szz, sz, sh, sz, n) / det;
        double b = Det(sxx, sxh, sx, sxz, szh, sz, sx, sh, n) / det;
        double c = Det(sxx, sxz, sxh, sxz, szz, szh, sx, sz, sh) / det;
        return (a, b, c);
    }

    public double CellSize(Vec2 p)
    {
        foreach (var r in Covering(p))
            if (Uv(r.sheet, p) != null) return FullWidth(r.sheet) / Math.Max(1, r.grid.Cols - 1);
        return double.PositiveInfinity;
    }
}
