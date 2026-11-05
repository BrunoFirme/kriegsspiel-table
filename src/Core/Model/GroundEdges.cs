namespace KTable.Core.Model;

public static class GroundEdges
{
    public const int StepsPerCell = 16;
    public const int FieldStepsPerCell = 4;
    public const int TownStepsPerCell = 8;
    public const int RoadStepsPerCell = 4;
    const int ClimbSteps = 24;
    const int ClimbGain = 10;
    public const byte Far = 255;
    public const int Channels = 4;
    const float Diagonal = 1.41421356f;

    public static byte[] Distances(ReliefGrid g) => Chamfer(g, Edges(g), StepsPerCell);

    public static byte[] Roads(ReliefGrid g)
    {
        var road = Cells(g, k => k == GroundKinds.Road || k == GroundKinds.Bridge);
        var away = Chamfer(g, road, RoadStepsPerCell);
        var wide = Widest(g, Distances(g), road);
        var o = new byte[g.Kind.Length];
        for (int i = 0; i < o.Length; i++) o[i] = road[i] ? wide[i] : away[i];
        return o;
    }

    static byte[] Widest(ReliefGrid g, byte[] inner, bool[] road)
    {
        int w = g.Cols, h = g.Rows;
        var o = new byte[inner.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                if (!road[i]) continue;
                int cx = x, cy = y;
                byte v = inner[i];
                for (int step = 0; step < ClimbSteps; step++)
                {
                    int bx = -1, by = -1;
                    byte bv = v;
                    for (int dy = -1; dy <= 1; dy++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = cx + dx, ny = cy + dy;
                            if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                            int j = ny * w + nx;
                            if (!road[j] || inner[j] <= bv) continue;
                            bv = inner[j];
                            bx = nx;
                            by = ny;
                        }
                    if (bx < 0 || bv - v < ClimbGain) break;
                    cx = bx;
                    cy = by;
                    v = bv;
                }
                o[i] = v;
            }
        return o;
    }

    public static byte[] Fields(ReliefGrid g)
    {
        var o = new byte[g.Kind.Length * Channels];
        var edges = Distances(g);
        var town = Chamfer(g, Cells(g, k => k == GroundKinds.Town), FieldStepsPerCell);
        var wet = Chamfer(g, Cells(g, k => k == GroundKinds.Water || k == GroundKinds.Of("marsh")), FieldStepsPerCell);
        var depth = Chamfer(g, Cells(g, k => k != GroundKinds.Town && k != GroundKinds.Road && k != GroundKinds.Bridge), TownStepsPerCell);
        for (int i = 0; i < g.Kind.Length; i++)
        {
            o[i * Channels] = edges[i];
            o[i * Channels + 1] = town[i];
            o[i * Channels + 2] = wet[i];
            o[i * Channels + 3] = depth[i];
        }
        return o;
    }

    static bool[] Edges(ReliefGrid g)
    {
        var e = new bool[g.Kind.Length];
        if (!g.Painted) return e;
        int w = g.Cols, h = g.Rows;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte k = GroundKinds.BrushOf(g.Kind[i]);
                bool Differs(int j) => GroundKinds.BrushOf(g.Kind[j]) != k;
                e[i] = (x > 0 && Differs(i - 1)) || (x < w - 1 && Differs(i + 1)) || (y > 0 && Differs(i - w)) || (y < h - 1 && Differs(i + w));
            }
        return e;
    }

    static bool[] Cells(ReliefGrid g, Func<byte, bool> of)
    {
        var e = new bool[g.Kind.Length];
        for (int i = 0; i < e.Length; i++) e[i] = of(g.Kind[i]);
        return e;
    }

    static byte[] Chamfer(ReliefGrid g, bool[] seeds, int stepsPerCell)
    {
        var o = new byte[g.Kind.Length];
        if (!seeds.Any(s => s)) { Array.Fill(o, Far); return o; }
        int w = g.Cols, h = g.Rows;
        var d = new float[w * h];
        float far = Far / (float)stepsPerCell;
        for (int i = 0; i < d.Length; i++) d[i] = seeds[i] ? 0.5f : far;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                float v = d[i];
                if (x > 0) v = Math.Min(v, d[i - 1] + 1);
                if (y > 0)
                {
                    v = Math.Min(v, d[i - w] + 1);
                    if (x > 0) v = Math.Min(v, d[i - w - 1] + Diagonal);
                    if (x < w - 1) v = Math.Min(v, d[i - w + 1] + Diagonal);
                }
                d[i] = v;
            }
        for (int y = h - 1; y >= 0; y--)
            for (int x = w - 1; x >= 0; x--)
            {
                int i = y * w + x;
                float v = d[i];
                if (x < w - 1) v = Math.Min(v, d[i + 1] + 1);
                if (y < h - 1)
                {
                    v = Math.Min(v, d[i + w] + 1);
                    if (x < w - 1) v = Math.Min(v, d[i + w + 1] + Diagonal);
                    if (x > 0) v = Math.Min(v, d[i + w - 1] + Diagonal);
                }
                d[i] = v;
                o[i] = (byte)Math.Min(Far, (int)Math.Round(v * stepsPerCell));
            }
        return o;
    }
}

public sealed record ContourPlan(double Step, double Every, string Unit)
{
    public const int IndexEvery = 5;
    public bool Numbered => Every > 0;
}

public static class Contours
{
    const double Wanted = 10;

    public static ContourPlan? Plan(double highest, double heightScale, Calibration calibration)
    {
        if (!(heightScale > 1e-6) || !(highest > 1e-6)) return null;
        if (calibration.UnitsPerMeter is not > 0)
            return Interval(highest) is var step and > 0 ? new ContourPlan(step, 0, "") : null;
        double every = Interval(calibration.FromTableUnits(highest / heightScale));
        return every > 0 ? new ContourPlan(calibration.ToTableUnits(every) * heightScale, every, calibration.UnitName) : null;
    }

    public static double Interval(double highest)
    {
        if (!(highest > 1e-6) || !double.IsFinite(highest)) return 0;
        double raw = highest / Wanted;
        double ten = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        double f = raw / ten;
        return (f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10) * ten;
    }
}

public static class Roman
{
    const int Largest = 3999;
    static readonly (int Value, string Letters)[] Steps =
        [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];

    public static string? Of(double value)
    {
        if (value < 1 || value > Largest || Math.Abs(value - Math.Round(value)) > 1e-9) return null;
        int n = (int)Math.Round(value);
        var sb = new System.Text.StringBuilder();
        foreach (var (v, letters) in Steps)
            while (n >= v) { sb.Append(letters); n -= v; }
        return sb.ToString();
    }
}
