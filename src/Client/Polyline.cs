using KTable.Core.Model;

namespace KTable.Client;

public static class Polyline
{
    public static double ToSegment(Vec2 p, Vec2 a, Vec2 b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double len2 = dx * dx + dz * dz;
        if (len2 < 1e-12) return (p - a).Length;
        double t = Math.Clamp(((p.X - a.X) * dx + (p.Z - a.Z) * dz) / len2, 0, 1);
        return (p - new Vec2(a.X + dx * t, a.Z + dz * t)).Length;
    }

    public static List<Vec2> Thin(List<Vec2> pts, double tol)
    {
        if (pts.Count < 3) return pts.ToList();
        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var work = new Stack<(int a, int b)>();
        work.Push((0, pts.Count - 1));
        while (work.Count > 0)
        {
            var (a, b) = work.Pop();
            if (b <= a + 1) continue;
            double worst = 0;
            int at = -1;
            for (int i = a + 1; i < b; i++)
            {
                double d = ToSegment(pts[i], pts[a], pts[b]);
                if (d > worst) { worst = d; at = i; }
            }
            if (worst <= tol || at < 0) continue;
            keep[at] = true;
            work.Push((a, at));
            work.Push((at, b));
        }
        return pts.Where((_, i) => keep[i]).ToList();
    }

    public static List<Vec2> Simplify(List<Vec2> pts)
    {
        var res = new List<Vec2> { pts[0] };
        foreach (var p in pts.Skip(1)) if ((p - res[^1]).Length > 0.05) res.Add(p);
        if (res.Count == 1) res.Add(pts[^1]);
        return res;
    }

    static Vec2 Unit(Vec2? across) => across is { Length: > 1e-9 } x ? new Vec2(x.X / x.Length, x.Z / x.Length) : new Vec2(1, 0);

    public static Vec2 Squared(Vec2 a, Vec2 b, Vec2? across = null)
    {
        var u = Unit(across);
        var v = new Vec2(-u.Z, u.X);
        var d = b - a;
        double du = d.X * u.X + d.Z * u.Z, dv = d.X * v.X + d.Z * v.Z, side = Math.Max(Math.Abs(du), Math.Abs(dv));
        du = Math.CopySign(side, du);
        dv = Math.CopySign(side, dv);
        return new Vec2(a.X + u.X * du + v.X * dv, a.Z + u.Z * du + v.Z * dv);
    }

    public static List<Vec2> ShapePoints(string kind, Vec2 a, Vec2 b, Vec2? across = null)
    {
        var u = Unit(across);
        var v = new Vec2(-u.Z, u.X);
        var d = b - a;
        double du = d.X * u.X + d.Z * u.Z, dv = d.X * v.X + d.Z * v.Z;
        switch (kind)
        {
            case "rect":
                return new() { a, a + new Vec2(u.X * du, u.Z * du), b, a + new Vec2(v.X * dv, v.Z * dv), a };
            case "ellipse":
            {
                const int Steps = 56;
                var c = new Vec2((a.X + b.X) / 2, (a.Z + b.Z) / 2);
                double ru = Math.Abs(du) / 2, rv = Math.Abs(dv) / 2;
                var pts = new List<Vec2>();
                for (int i = 0; i <= Steps; i++)
                {
                    double t = i * Math.Tau / Steps, cu = Math.Cos(t) * ru, sv = Math.Sin(t) * rv;
                    pts.Add(new Vec2(c.X + u.X * cu + v.X * sv, c.Z + u.Z * cu + v.Z * sv));
                }
                return pts;
            }
            case "arrow":
            {
                return new() { a, b };
            }
            default:
                return new() { a, b };
        }
    }

    public const string Curve = "curve", CurvedArrow = "curvearrow", Area = "area";

    public static bool IsPath(string kind) => kind is Curve or CurvedArrow or Area;

    const int SmoothSteps = 12;
    const double KnotGap = 1e-6;

    public static List<Vec2> Smooth(List<Vec2> knots, bool closed)
    {
        var k = new List<Vec2>();
        foreach (var p in knots) if (k.Count == 0 || (p - k[^1]).Length > KnotGap) k.Add(p);
        if (closed && k.Count > 1 && (k[0] - k[^1]).Length <= KnotGap) k.RemoveAt(k.Count - 1);
        if (k.Count < 3) return closed && k.Count == 2 ? new() { k[0], k[1], k[0] } : k;
        int n = k.Count, spans = closed ? n : n - 1;
        Vec2 At(int i) => closed ? k[((i % n) + n) % n] : i < 0 ? k[0] + (k[0] - k[1]) : i >= n ? k[n - 1] + (k[n - 1] - k[n - 2]) : k[i];
        var res = new List<Vec2> { k[0] };
        for (int s = 0; s < spans; s++)
        {
            Vec2 p0 = At(s - 1), p1 = At(s), p2 = At(s + 1), p3 = At(s + 2);
            double t0 = 0, t1 = t0 + Knot(p0, p1), t2 = t1 + Knot(p1, p2), t3 = t2 + Knot(p2, p3);
            for (int i = 1; i <= SmoothSteps; i++)
            {
                double t = t1 + (t2 - t1) * i / SmoothSteps;
                var a1 = Lerp(p0, p1, t0, t1, t);
                var a2 = Lerp(p1, p2, t1, t2, t);
                var a3 = Lerp(p2, p3, t2, t3, t);
                var b1 = Lerp(a1, a2, t0, t2, t);
                var b2 = Lerp(a2, a3, t1, t3, t);
                res.Add(Lerp(b1, b2, t1, t2, t));
            }
        }
        return res;
    }

    static double Knot(Vec2 a, Vec2 b) => Math.Max(Math.Sqrt((b - a).Length), KnotGap);

    static Vec2 Lerp(Vec2 a, Vec2 b, double ta, double tb, double t)
    {
        double f = (t - ta) / (tb - ta);
        return new Vec2(a.X + (b.X - a.X) * f, a.Z + (b.Z - a.Z) * f);
    }

    public static double Length(List<Vec2> pts)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += (pts[i] - pts[i - 1]).Length;
        return len;
    }

    public static List<Vec2>? PathHead(List<Vec2> pts)
    {
        if (pts.Count < 2) return null;
        var end = pts[^1];
        var d = end - pts[^2];
        for (int i = pts.Count - 3; i >= 0 && d.Length < 1e-6; i--) d = end - pts[i];
        if (d.Length < 1e-6) return null;
        double len = Length(pts);
        return ShapeHead("arrow", new Vec2(end.X - d.X / d.Length * len, end.Z - d.Z / d.Length * len), end);
    }

    public static bool Inside(List<Vec2> ring, Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Vec2 a = ring[i], b = ring[j];
            if ((a.Z > p.Z) != (b.Z > p.Z) && p.X < (b.X - a.X) * (p.Z - a.Z) / (b.Z - a.Z) + a.X) inside = !inside;
        }
        return inside;
    }

    public static List<Vec2>? ShapeHead(string kind, Vec2 a, Vec2 b)
    {
        if (kind != "arrow") return null;
        var d = b - a;
        double len = d.Length;
        if (len < 1e-6) return null;
        var u = new Vec2(d.X / len, d.Z / len);
        var n = new Vec2(-u.Z, u.X);
        double head = Math.Min(len * 0.28, Math.Max(len * 0.12, 0.6));
        var back = new Vec2(b.X - u.X * head, b.Z - u.Z * head);
        return new()
        {
            new Vec2(back.X + n.X * head * 0.5, back.Z + n.Z * head * 0.5),
            b,
            new Vec2(back.X - n.X * head * 0.5, back.Z - n.Z * head * 0.5),
        };
    }

    public static bool Near(List<Vec2> pts, Vec2 c, double r) => pts.Count == 1 ? (pts[0] - c).Length <= r : Rubs(pts, c, r);

    public static bool Rubs(List<Vec2> pts, Vec2 c, double r)
    {
        for (int i = 0; i < pts.Count; i++)
            if ((i == 0 ? (pts[0] - c).Length : ToSegment(c, pts[i - 1], pts[i])) <= r) return true;
        return false;
    }

    public static List<List<Vec2>> Cut(List<Vec2> pts, Vec2 c, double r)
    {
        var pieces = new List<List<Vec2>>();
        var cur = new List<Vec2>();
        void End() { if (cur.Count >= 2) pieces.Add(cur); cur = new List<Vec2>(); }
        static Vec2 At(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            var ts = new List<double> { 0 };
            ts.AddRange(CircleCuts(a, b, c, r));
            ts.Add(1);
            for (int k = 0; k + 1 < ts.Count; k++)
            {
                if ((At(a, b, (ts[k] + ts[k + 1]) / 2) - c).Length <= r) { End(); continue; }
                if (cur.Count == 0) cur.Add(At(a, b, ts[k]));
                cur.Add(At(a, b, ts[k + 1]));
            }
        }
        End();
        return pieces;
    }

    static List<double> CircleCuts(Vec2 a, Vec2 b, Vec2 c, double r)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        double fx = a.X - c.X, fz = a.Z - c.Z;
        double qa = dx * dx + dz * dz;
        if (qa < 1e-12) return new();
        double qb = 2 * (fx * dx + fz * dz);
        double qc = fx * fx + fz * fz - r * r;
        double disc = qb * qb - 4 * qa * qc;
        if (disc <= 0) return new();
        double root = Math.Sqrt(disc);
        var ts = new List<double>();
        foreach (var t in new[] { (-qb - root) / (2 * qa), (-qb + root) / (2 * qa) })
            if (t > 1e-9 && t < 1 - 1e-9) ts.Add(t);
        return ts;
    }
}
