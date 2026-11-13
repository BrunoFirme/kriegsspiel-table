namespace KTable.Core.Model;

public static class Earthworks
{
    const double CrestShare = 0.42, CellShare = 1.25, PitSlopeM = 4, FineCellM = 1.6, CoarseCellM = 2.5;
    const int CoarseSamples = 4, FineSamples = 2, MostFiner = 4;

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

    public static ReliefGrid Shaped(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        var works = things.SelectMany(t => t.Parts).Select(p => (Part: p, Work: Of(p))).Where(w => w.Work != null).ToList();
        if (works.Count == 0 || unitsPerMetre <= 0 || heightScale <= 0) return grid;
        int most = Math.Max(grid.Cols - 1, grid.Rows - 1), wanted = (int)Math.Ceiling(Math.Max(fullWidth / (grid.Cols - 1), fullHeight / (grid.Rows - 1)) / unitsPerMetre / FineCellM);
        int times = Math.Clamp(Math.Min(wanted, Ground.MostVertsAcross * Ground.WorkedFiner / most), 1, MostFiner);
        var shaped = times > 1 && grid.CanBeFiner(times) ? grid.Finer(times) : grid.Copy();
        shaped.Worked = true;
        float[] raised = new float[shaped.H.Length], cut = new float[shaped.H.Length];
        double cellX = fullWidth / (shaped.Cols - 1), cellY = fullHeight / (shaped.Rows - 1), cell = Math.Max(cellX, cellY);
        int samples = cell / unitsPerMetre > CoarseCellM ? CoarseSamples : FineSamples;
        foreach (var (part, found) in works)
        {
            var work = found!.Value;
            var at = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (at.Count < 2) continue;
            double half = Math.Max(work.HalfM * unitsPerMetre, cell * CellShare), reach = (work.Ring ? 0 : half) + cell, change = work.Metres * unitsPerMetre / heightScale;
            int x0 = Math.Clamp((int)((at.Min(p => p.X) - reach) / cellX), 0, shaped.Cols - 1), x1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.X) + reach) / cellX), 0, shaped.Cols - 1);
            int y0 = Math.Clamp((int)((at.Min(p => p.Y) - reach) / cellY), 0, shaped.Rows - 1), y1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.Y) + reach) / cellY), 0, shaped.Rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double share = Spread(at, new Spot(x * cellX, y * cellY), cellX, cellY, work.Ring, half, samples);
                    if (share <= 0) continue;
                    int i = y * shaped.Cols + x;
                    if (change > 0) raised[i] = Math.Max(raised[i], (float)(change * share));
                    else cut[i] = Math.Min(cut[i], (float)(change * share));
                }
        }
        Softened(raised, shaped.Cols, shaped.Rows);
        Softened(cut, shaped.Cols, shaped.Rows);
        float lift = 0;
        for (int i = 0; i < cut.Length; i++) lift = Math.Max(lift, -(shaped.H[i] + cut[i]));
        for (int i = 0; i < shaped.H.Length; i++) shaped.H[i] += lift + (cut[i] < 0 ? cut[i] : raised[i]);
        return shaped;
    }
}
