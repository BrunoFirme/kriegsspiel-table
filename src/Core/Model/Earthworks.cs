namespace KTable.Core.Model;

public static class Earthworks
{
    const double CrestShare = 0.42, CellShare = 1.25, PitSlopeM = 4, FineCellM = 1.6, CoarseCellM = 2.5, WaterCutM = 1.4;
    const int CoarseSamples = 4, FineSamples = 2, MostFiner = 4, WaterSoftening = 5;

    readonly record struct Work(double Metres, double HalfM, bool Ring);

    static Work? Of(BuiltPart part) => (part.Kind, part.Class) switch
    {
        (PartKinds.Line, "rampart") => new(3.6, 6, false),
        (PartKinds.Line, "causeway") => new(1.2, 3.2, false),
        (PartKinds.Line, "ditch") => new(-1.8, 2.2, false),
        (PartKinds.Line, "moat") => new(-3, 7.5, false),
        (PartKinds.Area, "moat") => new(-3, PitSlopeM, true),
        (PartKinds.Area, "quarry") => new(-5, PitSlopeM, true),
        _ => null,
    };

    public static long Key(IEnumerable<BuiltThing> things)
    {
        const long Prime = 1099511628211;
        long key = 0;
        foreach (var part in things.SelectMany(t => t.Parts).Where(p => Of(p) != null))
        {
            key = unchecked((key ^ part.Class.GetHashCode(StringComparison.Ordinal) ^ 1469598103934665603) * Prime);
            foreach (int v in part.At) key = unchecked((key ^ v) * Prime);
        }
        return key;
    }

    static double Off(Spot p, Spot a, Spot b)
    {
        var run = b - a;
        double along = run.Length <= 0 ? 0 : Math.Clamp(((p.X - a.X) * run.X + (p.Y - a.Y) * run.Y) / (run.Length * run.Length), 0, 1);
        return (p - (a + run * along)).Length;
    }

    static double Share(List<Spot> at, Spot p, bool ring, double half)
    {
        double least = double.MaxValue;
        bool inside = false;
        for (int i = ring ? 0 : 1, j = ring ? at.Count - 1 : 0; i < at.Count; j = i++)
        {
            Spot a = at[j], b = at[i];
            if (ring && a.Y > p.Y != b.Y > p.Y && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            least = Math.Min(least, Off(p, a, b));
        }
        return ring ? inside ? Math.Clamp(least / half, 0, 1) : 0 : 1 - Math.Clamp((least / half - CrestShare) / (1 - CrestShare), 0, 1);
    }

    static double Spread(List<Spot> at, Spot node, double cellX, double cellY, bool ring, double half, int samples)
    {
        double sum = 0;
        for (int sy = 0; sy < samples; sy++)
            for (int sx = 0; sx < samples; sx++)
                sum += Share(at, node + new Spot(((sx + 0.5) / samples - 0.5) * cellX, ((sy + 0.5) / samples - 0.5) * cellY), ring, half);
        return sum / (samples * samples);
    }

    static void Softened(float[] change, int cols, int rows)
    {
        var from = (float[])change.Clone();
        for (int y = 1; y < rows - 1; y++)
            for (int x = 1; x < cols - 1; x++)
            {
                int i = y * cols + x;
                if (from[i] == 0 && from[i - 1] == 0 && from[i + 1] == 0 && from[i - cols] == 0 && from[i + cols] == 0) continue;
                change[i] = (from[i] * 4 + (from[i - 1] + from[i + 1] + from[i - cols] + from[i + cols]) * 2 + from[i - cols - 1] + from[i - cols + 1] + from[i + cols - 1] + from[i + cols + 1]) / 16;
            }
    }

    public sealed record Laid(int Times, float[] Raised, float[] Cut);

    public static Laid? Lay(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        var works = things.SelectMany(t => t.Parts).Select(p => (Part: p, Work: Of(p))).Where(w => w.Work != null).ToList();
        if (works.Count == 0 || unitsPerMetre <= 0 || heightScale <= 0) return null;
        int most = Math.Max(grid.Cols - 1, grid.Rows - 1), wanted = (int)Math.Ceiling(Math.Max(fullWidth / (grid.Cols - 1), fullHeight / (grid.Rows - 1)) / unitsPerMetre / FineCellM);
        int times = Math.Clamp(Math.Min(wanted, Ground.MostVertsAcross * Ground.WorkedFiner / most), 1, MostFiner);
        if (!grid.CanBeFiner(times)) times = 1;
        int cols = (grid.Cols - 1) * times + 1, rows = (grid.Rows - 1) * times + 1;
        float[] raised = new float[cols * rows], cut = new float[cols * rows];
        double cellX = fullWidth / (cols - 1), cellY = fullHeight / (rows - 1), cell = Math.Max(cellX, cellY);
        int samples = cell / unitsPerMetre > CoarseCellM ? CoarseSamples : FineSamples;
        foreach (var (part, found) in works)
        {
            var work = found!.Value;
            var at = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (at.Count < 2) continue;
            double half = Math.Max(work.HalfM * unitsPerMetre, cell * CellShare), reach = (work.Ring ? 0 : half) + cell, change = work.Metres * unitsPerMetre / heightScale;
            int x0 = Math.Clamp((int)((at.Min(p => p.X) - reach) / cellX), 0, cols - 1), x1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.X) + reach) / cellX), 0, cols - 1);
            int y0 = Math.Clamp((int)((at.Min(p => p.Y) - reach) / cellY), 0, rows - 1), y1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.Y) + reach) / cellY), 0, rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double share = Spread(at, new Spot(x * cellX, y * cellY), cellX, cellY, work.Ring, half, samples);
                    if (share <= 0) continue;
                    int i = y * cols + x;
                    if (change > 0) raised[i] = Math.Max(raised[i], (float)(change * share));
                    else cut[i] = Math.Min(cut[i], (float)(change * share));
                }
        }
        Softened(raised, cols, rows);
        Softened(cut, cols, rows);
        return new Laid(times, raised, cut);
    }

    public sealed record Dug(float[] Cut, float Lift);

    static Dug? Dig(ReliefGrid shaped, Laid? laid, double unitsPerMetre, double heightScale)
    {
        bool wet = Array.IndexOf(shaped.Kind, GroundKinds.Water) >= 0;
        if (laid == null && !wet) return null;
        var cut = laid == null ? new float[shaped.H.Length] : (float[])laid.Cut.Clone();
        if (wet)
        {
            float deep = (float)(WaterCutM * unitsPerMetre / heightScale);
            var water = new float[shaped.H.Length];
            for (int i = 0; i < water.Length; i++)
                if (shaped.Kind[i] == GroundKinds.Water) water[i] = -deep;
            for (int pass = 0; pass < WaterSoftening; pass++) Softened(water, shaped.Cols, shaped.Rows);
            for (int i = 0; i < cut.Length; i++) cut[i] = Math.Min(cut[i], water[i]);
        }
        float lift = 0;
        for (int i = 0; i < cut.Length; i++) lift = Math.Max(lift, -(shaped.H[i] + cut[i]));
        return new Dug(cut, lift);
    }

    public static ReliefGrid Shaped(ReliefGrid grid, Laid? laid, ref Dug? dug, bool kept, double unitsPerMetre, double heightScale)
    {
        if (unitsPerMetre <= 0 || heightScale <= 0) return grid;
        if (laid == null && (kept ? dug == null : Array.IndexOf(grid.Kind, GroundKinds.Water) < 0))
        {
            if (!kept) dug = null;
            return grid;
        }
        var shaped = laid is { Times: > 1 } ? grid.Finer(laid.Times) : grid.Copy();
        if (laid != null && laid.Raised.Length != shaped.H.Length) return grid;
        shaped.Worked = laid != null;
        if (!kept || dug == null || dug.Cut.Length != shaped.H.Length) dug = Dig(shaped, laid, unitsPerMetre, heightScale);
        if (dug == null) return grid;
        for (int i = 0; i < shaped.H.Length; i++) shaped.H[i] = Math.Max(0, shaped.H[i] + dug.Lift + (dug.Cut[i] < 0 ? dug.Cut[i] : laid?.Raised[i] ?? 0));
        return shaped;
    }

    public static ReliefGrid Shaped(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        Dug? dug = null;
        return Shaped(grid, Lay(grid, things, fullWidth, fullHeight, unitsPerMetre, heightScale), ref dug, false, unitsPerMetre, heightScale);
    }
}
