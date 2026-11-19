using KTable.Core.Model;

namespace KTable.Client.Studio;

public static class InkRaster
{
    const double ThinnestHalf = 0.75;

    public static byte[] Paint(IEnumerable<Stroke> strokes, int w, int h, Func<Vec2, (double X, double Y)> toPx, double pxPerUnit)
    {
        var d = new byte[w * h * 4];
        foreach (var st in strokes)
        {
            if (st.Text != null || st.Points.Count == 0) continue;
            var ink = Rgb(st.Color);
            var pts = st.Points.Select(toPx).ToList();
            if (st.Fill && pts.Count >= 3) Polygon(d, w, h, pts, ink);
            if (st.Fill && st.Width <= 0) continue;
            double half = Math.Max(st.Width * pxPerUnit / 2, ThinnestHalf);
            for (int i = 1; i < pts.Count; i++) Line(d, w, h, pts[i - 1], pts[i], half, ink);
            if (st.Fill || pts.Count == 1) Line(d, w, h, pts[^1], pts[0], half, ink);
        }
        return d;
    }

    static (byte R, byte G, byte B) Rgb(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length < 6 || !int.TryParse(s[..6], System.Globalization.NumberStyles.HexNumber, null, out int v)) return (0, 0, 0);
        return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    static void Set(byte[] d, int w, int x, int y, (byte R, byte G, byte B) ink)
    {
        int i = (y * w + x) * 4;
        (d[i], d[i + 1], d[i + 2], d[i + 3]) = (ink.R, ink.G, ink.B, 255);
    }

    static void Line(byte[] d, int w, int h, (double X, double Y) a, (double X, double Y) b, double half, (byte R, byte G, byte B) ink)
    {
        int x0 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X) - half)), x1 = Math.Min(w - 1, (int)Math.Ceiling(Math.Max(a.X, b.X) + half));
        int y0 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y) - half)), y1 = Math.Min(h - 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y) + half));
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                double px = x + 0.5, py = y + 0.5;
                double t = len2 <= 1e-12 ? 0 : Math.Clamp(((px - a.X) * dx + (py - a.Y) * dy) / len2, 0, 1);
                double ex = px - (a.X + t * dx), ey = py - (a.Y + t * dy);
                if (ex * ex + ey * ey <= half * half) Set(d, w, x, y, ink);
            }
    }

    static void Polygon(byte[] d, int w, int h, List<(double X, double Y)> pts, (byte R, byte G, byte B) ink)
    {
        int y0 = Math.Max(0, (int)Math.Floor(pts.Min(p => p.Y))), y1 = Math.Min(h - 1, (int)Math.Ceiling(pts.Max(p => p.Y)));
        var cross = new List<double>();
        for (int y = y0; y <= y1; y++)
        {
            double py = y + 0.5;
            cross.Clear();
            for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            {
                var (a, b) = (pts[j], pts[i]);
                if ((a.Y > py) != (b.Y > py)) cross.Add(a.X + (py - a.Y) / (b.Y - a.Y) * (b.X - a.X));
            }
            cross.Sort();
            for (int k = 0; k + 1 < cross.Count; k += 2)
                for (int x = Math.Max(0, (int)Math.Ceiling(cross[k] - 0.5)); x <= Math.Min(w - 1, (int)Math.Floor(cross[k + 1] - 0.5)); x++) Set(d, w, x, y, ink);
        }
    }
}
