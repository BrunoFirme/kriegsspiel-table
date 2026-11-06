using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Studio;

public static class ArtShapes
{
    public static List<Vector2> Rect(Vector2 a, Vector2 b) => new() { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) };

    public static List<Vector2> RoundRect(Vector2 a, Vector2 b, float r)
    {
        var lo = new Vector2(Mathf.Min(a.X, b.X), Mathf.Min(a.Y, b.Y));
        var hi = new Vector2(Mathf.Max(a.X, b.X), Mathf.Max(a.Y, b.Y));
        r = Mathf.Min(r, Mathf.Min(hi.X - lo.X, hi.Y - lo.Y) / 2);
        if (r < 0.5f) return Rect(lo, hi);
        var pts = new List<Vector2>();
        foreach (var (cx, cy, start) in new[] { (hi.X - r, hi.Y - r, 0f), (lo.X + r, hi.Y - r, 90f), (lo.X + r, lo.Y + r, 180f), (hi.X - r, lo.Y + r, 270f) })
            for (int i = 0; i <= 8; i++) { var t = Mathf.DegToRad(start + 90f * i / 8); pts.Add(new Vector2(cx + Mathf.Cos(t) * r, cy + Mathf.Sin(t) * r)); }
        return pts;
    }

    public static List<Vector2> Ellipse(Vector2 a, Vector2 b, int n = 96)
    {
        var c = (a + b) / 2;
        var r = (b - a).Abs() / 2;
        var pts = new List<Vector2>(n);
        for (int i = 0; i < n; i++) { var t = Mathf.Tau * i / n; pts.Add(c + new Vector2(Mathf.Cos(t) * r.X, Mathf.Sin(t) * r.Y)); }
        return pts;
    }

    public static List<Vector2> Ngon(Vector2 c, Vector2 corner, int sides)
    {
        sides = Math.Clamp(sides, 3, 64);
        float r = (corner - c).Norm(), a0 = (corner - c).Angle();
        var pts = new List<Vector2>();
        for (int i = 0; i < sides; i++) { var t = a0 + Mathf.Tau * i / sides; pts.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r); }
        return pts;
    }

    public static List<Vector2> Star(Vector2 c, Vector2 tip, int points, float inner)
    {
        points = Math.Clamp(points, 3, 48);
        float r = (tip - c).Norm(), a0 = (tip - c).Angle();
        var pts = new List<Vector2>();
        for (int i = 0; i < points * 2; i++)
        {
            var t = a0 + Mathf.Pi * i / points;
            pts.Add(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * (i % 2 == 0 ? r : r * Mathf.Clamp(inner, 0.05f, 0.95f)));
        }
        return pts;
    }

    public static List<Vector2> ArrowHead(Vector2 a, Vector2 b, float width)
    {
        var d = b - a;
        float len = d.Norm();
        if (len < 1e-3f) return new();
        var u = d / len;
        var n = new Vector2(-u.Y, u.X);
        float hl = Mathf.Min(len * 0.45f, Mathf.Max(width * 4.5f, 12)), hw = hl * 0.55f;
        return new() { b, b - u * hl + n * hw, b - u * hl - n * hw };
    }

    public static Vector2 ArrowShaftEnd(Vector2 a, Vector2 b, float width)
    {
        var d = b - a;
        float len = d.Norm();
        if (len < 1e-3f) return b;
        float hl = Mathf.Min(len * 0.45f, Mathf.Max(width * 4.5f, 12));
        return b - d / len * hl * 0.8f;
    }

    public static List<Affine2> Symmetry(string mode, int radial, Vector2 c)
    {
        var list = new List<Affine2> { Affine2.Identity };
        Affine2 About(Affine2 m) => Affine2.Turn(0, c) * m * Affine2.Turn(0, -c);
        var mx = About(new Affine2(new Vector2(-1, 0), new Vector2(0, 1), Vector2.Zero));
        var my = About(new Affine2(new Vector2(1, 0), new Vector2(0, -1), Vector2.Zero));
        switch (mode)
        {
            case "x":
                list.Add(mx);
                break;
            case "y":
                list.Add(my);
                break;
            case "xy":
                list.Add(mx);
                list.Add(my);
                list.Add(mx * my);
                break;
            case "radial":
                for (int i = 1; i < Math.Clamp(radial, 2, 24); i++) list.Add(About(Affine2.Turn(Mathf.Tau * i / Math.Clamp(radial, 2, 24), Vector2.Zero)));
                break;
        }
        return list;
    }

    public static float[] PolyCoverage(int w, int h, IReadOnlyList<Vector2> poly)
    {
        var cov = new float[w * h];
        if (poly.Count < 3) return cov;
        const int Sub = 4;
        var xs = new List<float>();
        for (int y = 0; y < h; y++)
            for (int s = 0; s < Sub; s++)
            {
                float sy = y + (s + 0.5f) / Sub;
                xs.Clear();
                for (int i = 0; i < poly.Count; i++)
                {
                    var a = poly[i];
                    var b = poly[(i + 1) % poly.Count];
                    if ((a.Y <= sy && b.Y > sy) || (b.Y <= sy && a.Y > sy))
                        xs.Add(a.X + (sy - a.Y) / (b.Y - a.Y) * (b.X - a.X));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    float x0 = Mathf.Clamp(xs[k], 0, w), x1 = Mathf.Clamp(xs[k + 1], 0, w);
                    if (x1 <= x0) continue;
                    int i0 = (int)x0, i1 = Math.Min(w - 1, (int)x1);
                    for (int x = i0; x <= i1; x++)
                    {
                        float l = Mathf.Max(x0, x), r = Mathf.Min(x1, x + 1);
                        if (r > l) cov[y * w + x] += (r - l) / Sub;
                    }
                }
            }
        return cov;
    }
}
