namespace KTable.Core.Model;

public static class GroundHeights
{
    public static float[] Blurred(ReliefGrid g, int radius)
    {
        if (radius <= 0) return (float[])g.H.Clone();
        var along = Pass(g.H, g.Cols, g.Rows, radius, true);
        return Pass(along, g.Cols, g.Rows, radius, false);
    }

    static float[] Pass(float[] src, int cols, int rows, int r, bool alongRows)
    {
        var o = new float[src.Length];
        int n = alongRows ? cols : rows, lines = alongRows ? rows : cols;
        int stride = alongRows ? 1 : cols, line = alongRows ? cols : 1;
        double window = 2 * r + 1;
        for (int l = 0; l < lines; l++)
        {
            int at = l * line;
            double sum = 0;
            for (int i = -r; i <= r; i++) sum += src[at + Math.Clamp(i, 0, n - 1) * stride];
            for (int i = 0; i < n; i++)
            {
                o[at + i * stride] = (float)(sum / window);
                sum += src[at + Math.Min(i + r + 1, n - 1) * stride] - src[at + Math.Max(i - r, 0) * stride];
            }
        }
        return o;
    }
}
