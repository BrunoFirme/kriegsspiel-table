namespace KTable.Core.Model;

public static class GroundRoads
{
    public const int Channels = 4;
    public const float OffsetScale = 16;
    public const byte Present = 1, BridgeFlag = 2;
    public const int ReachCells = 6;
    const int WindowLeast = 3, WindowMost = 10;
    const double WindowPerHalf = 2.5, JunctionRatio = 0.35;
    const int Shifts = 3;
    public const int Influence = ReachCells + WindowMost * 3;

    readonly record struct Axis(float X, float Y, byte Angle, byte Class, bool Bridge);

    public static byte[] Axes(int cols, int rows, byte[] kinds, byte[] detail)
    {
        var axes = new byte[cols * rows * Channels];
        Fill(cols, rows, kinds, detail, axes);
        return axes;
    }

    public static void Axes(int cols, int rows, byte[] kinds, byte[] detail, CellBox box, byte[] axes)
    {
        var window = box.Grow(Influence * 2).Clip(cols, rows);
        var inner = box.Grow(Influence).Clip(cols, rows);
        if (window.Empty || inner.Empty) return;
        int w = window.Width, h = window.Height;
        var subKinds = new byte[w * h];
        var subDetail = new byte[w * h];
        for (int y = 0; y < h; y++)
        {
            Array.Copy(kinds, (window.Y0 + y) * cols + window.X0, subKinds, y * w, w);
            Array.Copy(detail, (window.Y0 + y) * cols + window.X0, subDetail, y * w, w);
        }
        var sub = new byte[w * h * Channels];
        Fill(w, h, subKinds, subDetail, sub);
        for (int y = inner.Y0; y <= inner.Y1; y++)
            Array.Copy(sub, ((y - window.Y0) * w + inner.X0 - window.X0) * Channels, axes, (y * cols + inner.X0) * Channels, inner.Width * Channels);
    }

    static void Fill(int cols, int rows, byte[] kinds, byte[] detail, byte[] axes)
    {
        Array.Clear(axes);
        int n = cols * rows;
        var road = new bool[n];
        var open = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            road[i] = kinds[i] is GroundKinds.Road or GroundKinds.Bridge;
            open[i] = !road[i];
            any |= road[i];
        }
        if (!any) return;
        var half = GroundNetwork.Distance(cols, rows, open);
        var found = new List<Axis>();
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int i = y * cols + x;
                if (!road[i]) continue;
                if (Estimate(cols, rows, road, half, x, y) is not { } a) continue;
                found.Add(a with { Class = (byte)((detail[i] & 15) * 17), Bridge = kinds[i] == GroundKinds.Bridge });
            }
        Stamp(cols, rows, found, axes);
    }

    static Axis? Estimate(int cols, int rows, bool[] road, float[] half, int x, int y)
    {
        double cx = x, cy = y, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
        int count = 0;
        for (int shift = 0; shift < Shifts; shift++)
        {
            int at = Math.Clamp((int)Math.Round(cy), 0, rows - 1) * cols + Math.Clamp((int)Math.Round(cx), 0, cols - 1);
            int r = Math.Clamp((int)Math.Round(WindowPerHalf * Math.Min(half[at], WindowMost)), WindowLeast, WindowMost);
            sx = sy = sxx = syy = sxy = 0;
            count = 0;
            int x0 = (int)Math.Floor(cx - r), x1 = (int)Math.Ceiling(cx + r), y0 = (int)Math.Floor(cy - r), y1 = (int)Math.Ceiling(cy + r);
            for (int ny = Math.Max(0, y0); ny <= Math.Min(rows - 1, y1); ny++)
                for (int nx = Math.Max(0, x0); nx <= Math.Min(cols - 1, x1); nx++)
                {
                    double dx = nx - cx, dy = ny - cy;
                    if (dx * dx + dy * dy > r * r || !road[ny * cols + nx]) continue;
                    sx += nx;
                    sy += ny;
                    sxx += (double)nx * nx;
                    syy += (double)ny * ny;
                    sxy += (double)nx * ny;
                    count++;
                }
            if (count < 3) return null;
            cx = sx / count;
            cy = sy / count;
        }
        double cxx = sxx / count - cx * cx, cyy = syy / count - cy * cy, cxy = sxy / count - cx * cy;
        double tr = cxx + cyy, det = cxx * cyy - cxy * cxy, disc = Math.Sqrt(Math.Max(0, tr * tr / 4 - det));
        double l1 = tr / 2 + disc, l2 = tr / 2 - disc;
        if (l1 <= 1e-9 || l2 / l1 > JunctionRatio) return null;
        double angle = Math.Abs(cxy) < 1e-12 ? (cxx >= cyy ? 0 : Math.PI / 2) : Math.Atan2(l1 - cxx, cxy);
        int quantised = (int)Math.Round(((angle % Math.PI) + Math.PI) % Math.PI / Math.PI * 255) % 256;
        return new Axis((float)(cx + 0.5), (float)(cy + 0.5), (byte)quantised, 0, false);
    }

    static void Stamp(int cols, int rows, List<Axis> found, byte[] axes)
    {
        var best = new float[cols * rows];
        Array.Fill(best, float.MaxValue);
        foreach (var a in found)
        {
            double angle = a.Angle / 255.0 * Math.PI;
            float nx = (float)-Math.Sin(angle), ny = (float)Math.Cos(angle);
            int ax = (int)a.X, ay = (int)a.Y;
            for (int y = Math.Max(0, ay - ReachCells); y <= Math.Min(rows - 1, ay + ReachCells); y++)
                for (int x = Math.Max(0, ax - ReachCells); x <= Math.Min(cols - 1, ax + ReachCells); x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float d = (qx - a.X) * (qx - a.X) + (qy - a.Y) * (qy - a.Y);
                    int i = y * cols + x;
                    if (d > ReachCells * ReachCells || d >= best[i]) continue;
                    best[i] = d;
                    float off = (a.X - qx) * nx + (a.Y - qy) * ny;
                    axes[i * Channels] = a.Angle;
                    axes[i * Channels + 1] = (byte)Math.Clamp((int)Math.Round(128 + off * OffsetScale), 1, 255);
                    axes[i * Channels + 2] = a.Class;
                    axes[i * Channels + 3] = (byte)(Present | (a.Bridge ? BridgeFlag : 0));
                }
        }
    }
}
