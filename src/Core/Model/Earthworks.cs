namespace KTable.Core.Model;

public static class Earthworks
{
    const double CrestShare = 0.4, CellShare = 0.8, PitSlopeM = 4;

    readonly record struct Work(double Metres, double HalfM, bool Ring);

    static Work? Of(BuiltPart part) => (part.Kind, part.Class) switch
    {
        (PartKinds.Line, "rampart") => new(3.5, 5.5, false),
        (PartKinds.Line, "ditch") => new(-1.8, 1.6, false),
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

    static double Smooth(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
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
        return ring ? inside ? Smooth(0, half, least) : 0 : 1 - Smooth(CrestShare, 1, least / half);
    }

    public static ReliefGrid Shaped(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        var works = things.SelectMany(t => t.Parts).Select(p => (Part: p, Work: Of(p))).Where(w => w.Work != null).OrderByDescending(w => w.Work!.Value.Metres).ToList();
        if (works.Count == 0 || unitsPerMetre <= 0 || heightScale <= 0) return grid;
        var shaped = grid.Copy();
        double cellX = fullWidth / (grid.Cols - 1), cellY = fullHeight / (grid.Rows - 1), cell = Math.Max(cellX, cellY);
        foreach (var (part, found) in works)
        {
            var work = found!.Value;
            var at = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (at.Count < 2) continue;
            double half = Math.Max(work.HalfM * unitsPerMetre, cell * CellShare), reach = work.Ring ? 0 : half, change = work.Metres * unitsPerMetre / heightScale;
            int x0 = Math.Clamp((int)((at.Min(p => p.X) - reach) / cellX), 0, grid.Cols - 1), x1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.X) + reach) / cellX), 0, grid.Cols - 1);
            int y0 = Math.Clamp((int)((at.Min(p => p.Y) - reach) / cellY), 0, grid.Rows - 1), y1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.Y) + reach) / cellY), 0, grid.Rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    double share = Share(at, new Spot(x * cellX, y * cellY), work.Ring, half);
                    if (share <= 0) continue;
                    int i = y * grid.Cols + x;
                    float to = (float)Math.Max(0, grid.H[i] + change * share);
                    shaped.H[i] = change > 0 ? Math.Max(shaped.H[i], to) : Math.Min(shaped.H[i], to);
                }
        }
        return shaped;
    }
}
