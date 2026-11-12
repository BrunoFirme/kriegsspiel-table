namespace KTable.Core.Model;

public static class GroundEdges
{
    public const int StepsPerCell = 16;
    public const int FieldStepsPerCell = 4;
    public const byte Far = 255;
    public const int Channels = 2;
    const float Diagonal = 1.41421356f;

    public static byte[] Distances(ReliefGrid g) => Chamfer(g.Cols, g.Rows, Edges(g.Cols, g.Rows, g.Kind), StepsPerCell);

    public const int Reach = 64;

    public static void Derive(int cols, int rows, byte[] kinds, CellBox box, byte[] fields)
    {
        var window = box.Grow(Reach * 2).Clip(cols, rows);
        var inner = box.Grow(Reach).Clip(cols, rows);
        if (window.Empty || inner.Empty) return;
        int w = window.Width, h = window.Height;
        var sub = new byte[w * h];
        for (int y = 0; y < h; y++) Array.Copy(kinds, (window.Y0 + y) * cols + window.X0, sub, y * w, w);
        var f = Derive(w, h, sub);
        for (int y = inner.Y0; y <= inner.Y1; y++)
            Array.Copy(f, ((y - window.Y0) * w + inner.X0 - window.X0) * Channels, fields, (y * cols + inner.X0) * Channels, inner.Width * Channels);
    }

    public static byte[] Derive(int cols, int rows, byte[] kinds)
    {
        var wet = new bool[kinds.Length];
        for (int i = 0; i < wet.Length; i++) wet[i] = kinds[i] is GroundKinds.Water or GroundKinds.Marsh;
        var edges = Chamfer(cols, rows, Edges(cols, rows, kinds), StepsPerCell);
        var water = Chamfer(cols, rows, wet, FieldStepsPerCell);
        var fields = new byte[kinds.Length * Channels];
        for (int i = 0; i < kinds.Length; i++)
        {
            fields[i * Channels] = edges[i];
            fields[i * Channels + 1] = water[i];
        }
        return fields;
    }

    static bool[] Edges(int w, int h, byte[] kinds)
    {
        var e = new bool[kinds.Length];
        if (Array.TrueForAll(kinds, k => k == GroundKinds.None)) return e;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                byte k = GroundKinds.BrushOf(kinds[i]);
                bool Differs(int j) => GroundKinds.BrushOf(kinds[j]) != k;
                e[i] = (x > 0 && Differs(i - 1)) || (x < w - 1 && Differs(i + 1)) || (y > 0 && Differs(i - w)) || (y < h - 1 && Differs(i + w));
            }
        return e;
    }

    static byte[] Chamfer(int w, int h, bool[] seeds, int stepsPerCell)
    {
        var o = new byte[seeds.Length];
        if (!Array.Exists(seeds, s => s)) { Array.Fill(o, Far); return o; }
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
