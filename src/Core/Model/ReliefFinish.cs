namespace KTable.Core.Model;

public static class ReliefFinish
{
    const int Octaves = 3, BankCells = 3, CellA = 73856093, CellB = 19349663;
    const double OctaveShare = 0.5, DeeperByOrder = 0.4;

    static CellBox Whole(ReliefGrid g) => new(0, 0, g.Cols - 1, g.Rows - 1);

    public static CellBox Lift(ReliefGrid g, double by)
    {
        if (by == 0) return CellBox.None;
        for (int i = 0; i < g.H.Length; i++) g.H[i] = (float)Math.Max(0, g.H[i] + by);
        return Whole(g);
    }

    static double Knot(int x, int y, int seed)
    {
        uint h = unchecked((uint)(x * CellA ^ y * CellB ^ seed * 668265263));
        h ^= h >> 13;
        h = unchecked(h * 1274126177);
        h ^= h >> 16;
        return h / (double)uint.MaxValue * 2 - 1;
    }

    static double Wave(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0, sx = fx * fx * (3 - 2 * fx), sy = fy * fy * (3 - 2 * fy);
        double top = Knot(x0, y0, seed) + (Knot(x0 + 1, y0, seed) - Knot(x0, y0, seed)) * sx, foot = Knot(x0, y0 + 1, seed) + (Knot(x0 + 1, y0 + 1, seed) - Knot(x0, y0 + 1, seed)) * sx;
        return top + (foot - top) * sy;
    }

    public static CellBox Roughen(ReliefGrid g, double amount, double grainCells, int seed)
    {
        if (amount <= 0 || grainCells <= 0) return CellBox.None;
        double whole = 0;
        for (int o = 0; o < Octaves; o++) whole += Math.Pow(OctaveShare, o);
        for (int y = 0; y < g.Rows; y++)
            for (int x = 0; x < g.Cols; x++)
            {
                int i = y * g.Cols + x;
                if (g.Kind[i] == GroundKinds.Water) continue;
                double sum = 0;
                for (int o = 0; o < Octaves; o++) sum += Wave(x / grainCells * (1 << o), y / grainCells * (1 << o), seed + o) * Math.Pow(OctaveShare, o);
                g.H[i] = (float)Math.Max(0, g.H[i] + amount * sum / whole);
            }
        return Whole(g);
    }

    public static CellBox Erode(ReliefGrid g, double depth, double cellMetres, double metresPerHeightUnit)
    {
        if (depth <= 0 || cellMetres <= 0) return CellBox.None;
        var flow = GroundStreams.Flow(g.Cols, g.Rows, g.H, GroundStreams.Waters(g), cellMetres, metresPerHeightUnit);
        var cut = new float[g.H.Length];
        bool any = false;
        for (int y = 0; y < g.Rows; y++)
            for (int x = 0; x < g.Cols; x++)
            {
                int order = flow.Order[y * g.Cols + x];
                if (order == 0) continue;
                any = true;
                double deepest = depth * (1 + DeeperByOrder * (order - 1));
                for (int dy = -BankCells; dy <= BankCells; dy++)
                    for (int dx = -BankCells; dx <= BankCells; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= g.Cols || ny >= g.Rows) continue;
                        double share = 1 - Math.Sqrt(dx * dx + dy * dy) / (BankCells + 1);
                        if (share > 0) cut[ny * g.Cols + nx] = Math.Max(cut[ny * g.Cols + nx], (float)(deepest * share * share));
                    }
            }
        if (!any) return CellBox.None;
        for (int i = 0; i < cut.Length; i++)
            if (g.Kind[i] != GroundKinds.Water) g.H[i] = Math.Max(0, g.H[i] - cut[i]);
        return Whole(g);
    }
}
