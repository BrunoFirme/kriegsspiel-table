using System.Numerics;

namespace KTable.Client.Studio;

public static class HandleMath
{
    public static Vector2[] Points(Vector2 min, Vector2 max)
    {
        var c = (min + max) / 2;
        return new[]
        {
            min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y),
            new Vector2(c.X, min.Y), new Vector2(max.X, c.Y), new Vector2(c.X, max.Y), new Vector2(min.X, c.Y),
        };
    }

    public static int Opposite(int handle) => handle < 4 ? (handle + 2) % 4 : 4 + (handle - 4 + 2) % 4;

    public static bool MovesLeft(int handle) => handle is 0 or 3 or 7;
    public static bool MovesRight(int handle) => handle is 1 or 2 or 5;
    public static bool MovesTop(int handle) => handle is 0 or 1 or 4;
    public static bool MovesBottom(int handle) => handle is 2 or 3 or 6;

    public static (float sx, float sy) Scale(int handle, Vector2 anchor, Vector2 grip, Vector2 now, bool free)
    {
        var span = grip - anchor; var to = now - anchor;
        float sx = 1, sy = 1;
        if (handle < 4)
        {
            if (free) { sx = MathF.Abs(span.X) > 1e-4f ? to.X / span.X : 1; sy = MathF.Abs(span.Y) > 1e-4f ? to.Y / span.Y : 1; }
            else { float k = span.LengthSquared() > 1e-6f ? Vector2.Dot(to, span) / span.LengthSquared() : 1; sx = sy = k; }
        }
        else if (handle is 4 or 6) sy = MathF.Abs(span.Y) > 1e-4f ? to.Y / span.Y : 1;
        else sx = MathF.Abs(span.X) > 1e-4f ? to.X / span.X : 1;
        return (AtLeastAHundredth(sx), AtLeastAHundredth(sy));
    }

    static float AtLeastAHundredth(float s) => MathF.Abs(s) < 0.01f ? 0.01f * (s < 0 ? -1 : 1) : s;

    public static float[] Crop(int handle, float[] start, Vector2 at, float srcW, float srcH)
    {
        float x0 = start[0], y0 = start[1], x1 = x0 + start[2], y1 = y0 + start[3];
        if (MovesLeft(handle)) x0 = Clamp(at.X, 0, x1 - 2);
        if (MovesRight(handle)) x1 = Clamp(at.X, x0 + 2, srcW);
        if (MovesTop(handle)) y0 = Clamp(at.Y, 0, y1 - 2);
        if (MovesBottom(handle)) y1 = Clamp(at.Y, y0 + 2, srcH);
        return new[] { MathF.Round(x0), MathF.Round(y0), MathF.Round(x1 - x0), MathF.Round(y1 - y0) };
    }

    public static (double x, double y, double w, double h) Box(int handle, double x, double y, double w, double h, double dx, double dy)
    {
        double x0 = x, y0 = y, x1 = x + w, y1 = y + h;
        if (handle == 9) { x0 += dx; x1 += dx; y0 += dy; y1 += dy; }
        else
        {
            if (MovesLeft(handle)) x0 += dx;
            if (MovesRight(handle)) x1 += dx;
            if (MovesTop(handle)) y0 += dy;
            if (MovesBottom(handle)) y1 += dy;
        }
        double nw = Math.Max(0.02, x1 - x0), nh = Math.Max(0.02, y1 - y0);
        return (Math.Clamp(x0, 0, 1 - Math.Min(nw, 1)), Math.Clamp(y0, 0, 1 - Math.Min(nh, 1)), Math.Min(nw, 1), Math.Min(nh, 1));
    }

    public static int NearestPoint(IReadOnlyList<Vector2> pts, Vector2 p, float within)
    {
        for (int i = 0; i < pts.Count; i++) if ((pts[i] - p).Length() < within) return i;
        return -1;
    }

    public static int NearestEdge(IReadOnlyList<Vector2> pts, Vector2 p, float within)
    {
        if (pts.Count < 2) return -1;
        int best = -1; float bd = within;
        int edges = pts.Count == 2 ? 1 : pts.Count;
        for (int i = 0; i < edges; i++)
        {
            float d = (ClosestOnSegment(p, pts[i], pts[(i + 1) % pts.Count]) - p).Length();
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public static Vector2 ClosestOnSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var n = b - a;
        float l2 = n.LengthSquared();
        if (l2 < 1e-20f) return a;
        float t = Vector2.Dot(p - a, n) / l2;
        return t <= 0 ? a : t >= 1 ? b : a + n * t;
    }

    public static Vector2 Constrain(Vector2 a, Vector2 b, float stepDeg = 15)
    {
        var d = b - a;
        float deg = MathF.Round(MathF.Atan2(d.Y, d.X) * 180 / MathF.PI / stepDeg) * stepDeg;
        float rad = deg * MathF.PI / 180;
        return a + new Vector2(MathF.Cos(rad), MathF.Sin(rad)) * d.Length();
    }

    static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
}
