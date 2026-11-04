namespace KTable.Core.Model;

public static class GroundScatter
{
    const double ClumpCells = 7;
    const double ClumpShare = 0.85;
    const int SampleSide = 256;

    static readonly Lazy<float[]> Ranked = new(Rank);

    public static bool Takes(int x, int y, byte kind, double density)
    {
        if (density >= 1) return true;
        if (density <= 0) return false;
        var ranked = Ranked.Value;
        return Raw(x, y, kind) < ranked[Math.Min(ranked.Length - 1, (int)(density * ranked.Length))];
    }

    static float[] Rank()
    {
        var v = new float[SampleSide * SampleSide];
        for (int y = 0; y < SampleSide; y++)
            for (int x = 0; x < SampleSide; x++) v[y * SampleSide + x] = (float)Raw(x, y, 0);
        Array.Sort(v);
        return v;
    }

    static double Raw(int x, int y, byte kind) => ClumpShare * Smooth(x / ClumpCells, y / ClumpCells, kind) + (1 - ClumpShare) * Hash(x, y, kind + 101);

    static double Smooth(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double top = Lerp(Hash(x0, y0, seed), Hash(x0 + 1, y0, seed), fx);
        double bottom = Lerp(Hash(x0, y0 + 1, seed), Hash(x0 + 1, y0 + 1, seed), fx);
        return Lerp(top, bottom, fy);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)seed * 0xCB1AB31Fu;
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return h / 4294967296.0;
        }
    }
}
