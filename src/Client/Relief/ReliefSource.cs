using System.Numerics;
using KTable.Client.Studio;
using KTable.Core.Model;

namespace KTable.Client.Relief;

public enum PictureFit { None, Direct, Resample }

public sealed record TopTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 Ua, Vector2 Ub, Vector2 Uc, float Ha = 0, float Hb = 0, float Hc = 0);

public sealed class ReliefSource
{
    public const double UnitlessRise = 0.05;

    public required string Name { get; init; }
    public required int Cols { get; init; }
    public required int Rows { get; init; }
    public required float[] Raw { get; init; }
    public required double Width { get; init; }
    public required double Depth { get; init; }
    public double Left { get; init; }
    public double Top { get; init; }
    public bool RealUnits { get; init; }
    public string? Picture { get; init; }
    public PictureFit Fit { get; init; }
    public bool FlipU { get; init; }
    public bool FlipV { get; init; }
    public bool Swap { get; init; }
    public IReadOnlyList<TopTriangle> Triangles { get; init; } = Array.Empty<TopTriangle>();

    public double Low => Raw.Length == 0 ? 0 : Raw.Min();

    public ReliefGrid ToGrid(double zero)
    {
        var (cols, rows, raw) = Fitted();
        var g = new ReliefGrid(cols, rows);
        double k = 1 / Math.Max(1e-9, Width);
        for (int i = 0; i < raw.Length; i++) g.H[i] = (float)Math.Max(0, (raw[i] - zero) * k);
        return g;
    }

    (int cols, int rows, float[] raw) Fitted()
    {
        if (Cols <= ReliefGrid.MaxSide && Rows <= ReliefGrid.MaxSide) return (Cols, Rows, Raw);
        double k = (ReliefGrid.MaxSide - 1) / (double)(Math.Max(Cols, Rows) - 1);
        int cols = Math.Max(2, (int)Math.Round((Cols - 1) * k) + 1), rows = Math.Max(2, (int)Math.Round((Rows - 1) * k) + 1);
        var raw = new float[cols * rows];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int sx = (int)Math.Round(x / (double)(cols - 1) * (Cols - 1)), sy = (int)Math.Round(y / (double)(rows - 1) * (Rows - 1));
                raw[y * cols + x] = Raw[sy * Cols + sx];
            }
        return (cols, rows, raw);
    }

    public static double SharedZero(IEnumerable<ReliefSource> set) => set.Select(s => s.Low).DefaultIfEmpty(0).Min();
}

public static class ReliefBuilder
{
    const int LeastSide = 33;
    const int MostPasses = 4096;
    const double FitTolerance = 0.004;

    public static ReliefSource FromMesh(ModelMesh m, string name)
    {
        if (m.V.Count < 3) throw new InvalidDataException("the model has no triangles");
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        foreach (var v in m.V) { lo = Vector3.Min(lo, v); hi = Vector3.Max(hi, v); }
        var size = hi - lo;
        Func<Vector3, (float x, float z, float h)> top = size.Y <= size.Z && size.Y <= size.X
            ? v => (v.X, v.Z, v.Y)
            : size.Z <= size.X ? v => (v.X, -v.Y, v.Z) : v => (v.Z, -v.Y, v.X);
        var pts = m.V.Select(top).ToArray();
        float x0 = pts.Min(p => p.x), x1 = pts.Max(p => p.x), z0 = pts.Min(p => p.z), z1 = pts.Max(p => p.z);
        double width = x1 - x0, depth = z1 - z0;
        if (width <= 0 || depth <= 0) throw new InvalidDataException("the model is flat from above: no ground to read");
        int tris = pts.Length / 3;
        double cell = Math.Sqrt(width * depth / Math.Max(1, tris / 2.0));
        int cols = Math.Clamp((int)Math.Round(width / cell) + 1, LeastSide, ReliefGrid.MaxSide);
        int rows = Math.Clamp((int)Math.Round(depth / cell) + 1, LeastSide, ReliefGrid.MaxSide);
        var raw = new float[cols * rows];
        var filled = new bool[cols * rows];
        Array.Fill(raw, float.MinValue);
        Vector2 G((float x, float z, float h) p) => new((float)((p.x - x0) / width * (cols - 1)), (float)((p.z - z0) / depth * (rows - 1)));
        var triangles = new List<TopTriangle>(tris);
        for (int t = 0; t + 2 < pts.Length; t += 3)
        {
            var (a, b, c) = (pts[t], pts[t + 1], pts[t + 2]);
            Splat(raw, filled, cols, rows, G(a), G(b), G(c), a.h, b.h, c.h);
            if (m.UV.Count > t + 2)
                triangles.Add(new TopTriangle(Norm(a), Norm(b), Norm(c), m.UV[t], m.UV[t + 1], m.UV[t + 2], a.h, b.h, c.h));
        }
        Vector2 Norm((float x, float z, float h) p) => new((float)((p.x - x0) / width), (float)((p.z - z0) / depth));
        FillHoles(raw, filled, cols, rows);
        var picture = m.Mat.Count > 0 && m.Mat[0] < m.Materials.Count ? m.Materials[m.Mat[0]].Picture : m.Materials.FirstOrDefault(x => x.Picture != null)?.Picture;
        var (fit, flipU, flipV, swap) = triangles.Count == 0 ? (PictureFit.None, false, false, false) : Fit(triangles);
        return new ReliefSource
        {
            Name = name, Cols = cols, Rows = rows, Raw = raw, Width = width, Depth = depth, Left = x0, Top = z0, RealUnits = true,
            Picture = picture, Fit = fit, FlipU = flipU, FlipV = flipV, Swap = swap, Triangles = triangles,
        };
    }

    static void Splat(float[] raw, bool[] filled, int cols, int rows, Vector2 a, Vector2 b, Vector2 c, float ha, float hb, float hc)
    {
        void Put(int x, int y, float h)
        {
            if (x < 0 || y < 0 || x >= cols || y >= rows) return;
            int i = y * cols + x;
            if (!filled[i] || h > raw[i]) { raw[i] = h; filled[i] = true; }
        }
        Put((int)Math.Round(a.X), (int)Math.Round(a.Y), ha);
        Put((int)Math.Round(b.X), (int)Math.Round(b.Y), hb);
        Put((int)Math.Round(c.X), (int)Math.Round(c.Y), hc);
        float area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
        if (Math.Abs(area) < 1e-9f) return;
        int minX = Math.Max(0, (int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X)))), maxX = Math.Min(cols - 1, (int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)))), maxY = Math.Min(rows - 1, (int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))));
        const float Edge = -1e-4f;
        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float w0 = ((b.X - x) * (c.Y - y) - (c.X - x) * (b.Y - y)) / area;
                float w1 = ((c.X - x) * (a.Y - y) - (a.X - x) * (c.Y - y)) / area;
                float w2 = 1 - w0 - w1;
                if (w0 < Edge || w1 < Edge || w2 < Edge) continue;
                Put(x, y, w0 * ha + w1 * hb + w2 * hc);
            }
    }

    public static void FillHoles(float[] raw, bool[] filled, int cols, int rows)
    {
        if (!filled.Any(f => f)) throw new InvalidDataException("nothing of the model could be read from above");
        for (int pass = 0; pass < MostPasses && filled.Any(f => !f); pass++)
        {
            var next = (bool[])filled.Clone();
            for (int y = 0; y < rows; y++)
                for (int x = 0; x < cols; x++)
                {
                    int i = y * cols + x;
                    if (filled[i]) continue;
                    float sum = 0;
                    int n = 0;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx, ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= cols || ny >= rows || !filled[ny * cols + nx]) continue;
                            sum += raw[ny * cols + nx];
                            n++;
                        }
                    if (n > 0) { raw[i] = sum / n; next[i] = true; }
                }
            Array.Copy(next, filled, filled.Length);
        }
    }

    static (PictureFit fit, bool flipU, bool flipV, bool swap) Fit(List<TopTriangle> tris)
    {
        var ps = tris.SelectMany(t => new[] { (t.A, t.Ua), (t.B, t.Ub), (t.C, t.Uc) }).ToList();
        var (a, b, c, ra) = Plane(ps, q => q.Ua.X);
        var (d, e, f, rb) = Plane(ps, q => q.Ua.Y);
        if (ra > FitTolerance || rb > FitTolerance) return (PictureFit.Resample, false, false, false);
        static bool Near(double x, double y) => Math.Abs(x - y) < 0.02;
        if (Near(Math.Abs(a), 1) && Near(b, 0) && Near(d, 0) && Near(Math.Abs(e), 1) && Near(c, a < 0 ? 1 : 0) && Near(f, e < 0 ? 1 : 0))
            return (PictureFit.Direct, a < 0, e < 0, false);
        if (Near(a, 0) && Near(Math.Abs(b), 1) && Near(Math.Abs(d), 1) && Near(e, 0) && Near(c, b < 0 ? 1 : 0) && Near(f, d < 0 ? 1 : 0))
            return (PictureFit.Direct, d < 0, b < 0, true);
        return (PictureFit.Resample, false, false, false);
    }

    static (double a, double b, double c, double residual) Plane(List<(Vector2 p, Vector2 Ua)> ps, Func<(Vector2 p, Vector2 Ua), float> value)
    {
        double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = ps.Count, sxv = 0, syv = 0, sv = 0;
        foreach (var q in ps)
        {
            double x = q.p.X, y = q.p.Y, v = value(q);
            sxx += x * x;
            sxy += x * y;
            sx += x;
            syy += y * y;
            sy += y;
            sxv += x * v;
            syv += y * v;
            sv += v;
        }
        var m = new double[,] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        var r = new[] { sxv, syv, sv };
        if (!Solve(m, r, out var s)) return (0, 0, 0, double.MaxValue);
        double worst = ps.Max(q => Math.Abs(s[0] * q.p.X + s[1] * q.p.Y + s[2] - value(q)));
        return (s[0], s[1], s[2], worst);
    }

    static bool Solve(double[,] m, double[] r, out double[] s)
    {
        s = new double[3];
        double det = Det(m);
        if (Math.Abs(det) < 1e-12) return false;
        for (int k = 0; k < 3; k++)
        {
            var mk = (double[,])m.Clone();
            for (int i = 0; i < 3; i++) mk[i, k] = r[i];
            s[k] = Det(mk) / det;
        }
        return true;
    }

    static double Det(double[,] m) =>
        m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
}
