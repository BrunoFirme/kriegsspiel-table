namespace KTable.Core.Model;

public static class WayGrading
{
    const double BankRun = 1.5, SlopeLeastM = 5, CellShare = 1.25, LeastM = 0.7, BucketCells = 8;

    public static IReadOnlyList<(BuiltPart Part, Profile Profile)> Profiles(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre,
        double heightScale) => Of(grid, things, fullWidth, fullHeight, unitsPerMetre, heightScale, _ => false);

    internal static List<(BuiltPart Part, Profile Profile)> Of(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double perMetre, double heightScale,
        Func<Spot, bool> bed)
    {
        var graded = new List<(BuiltPart Part, Profile Profile)>();
        if (perMetre <= 0 || heightScale <= 0) return graded;
        double step = Math.Max(fullWidth / Math.Max(1, grid.Cols - 1), fullHeight / Math.Max(1, grid.Rows - 1)), metres = heightScale / perMetre;
        var net = new WayNet(step, perMetre, p => grid.HeightAt(p.X / fullWidth, p.Y / fullHeight) * metres,
            p => grid.KindAt(p.X / fullWidth, p.Y / fullHeight) == GroundKinds.Water || bed(p));
        foreach (var thing in things.Where(Footings.Marked)) graded.AddRange(WayMarks.Graded(thing.Parts, Manners.Of(thing), net, perMetre));
        return graded;
    }

    internal static Func<Spot, bool> Beds(IEnumerable<(List<Spot> At, double Half)> waters, double cell)
    {
        double bucket = cell * BucketCells;
        var beds = new Dictionary<(int, int), List<(Spot At, double Half)>>();
        foreach (var (at, half) in waters)
            foreach (var p in at)
            {
                var key = ((int)Math.Floor(p.X / bucket), (int)Math.Floor(p.Y / bucket));
                if (!beds.TryGetValue(key, out var list)) beds[key] = list = new();
                list.Add((p, half));
            }
        if (beds.Count == 0) return _ => false;
        return p =>
        {
            int bx = (int)Math.Floor(p.X / bucket), by = (int)Math.Floor(p.Y / bucket);
            for (int y = by - 1; y <= by + 1; y++)
                for (int x = bx - 1; x <= bx + 1; x++)
                    if (beds.TryGetValue((x, y), out var near) && near.Any(b => (b.At - p).Length <= b.Half)) return true;
            return false;
        };
    }

    static double Cross(Func<Spot, double> bareM, Spot a, Spot b, double half)
    {
        var mid = (a + b) * 0.5;
        var aside = (b - a).Unit.Left * half;
        return Math.Abs(bareM(mid + aside) - bareM(mid - aside)) / 2;
    }

    internal static bool Worth((BuiltPart Part, Profile Profile) way, Func<Spot, double> bareM, double perMetre)
    {
        var p = way.Profile;
        double half = Grades.HalfM(way.Part.Class) * perMetre;
        for (int j = 0; j + 1 < p.Count; j++)
            if (p.Written(j) && !p.Stepped(j) && Math.Max(Math.Abs(p.ChangeM(j)), Math.Abs(p.ChangeM(j + 1))) + Cross(bareM, p.At[j], p.At[j + 1], half) >= LeastM) return true;
        return false;
    }

    internal readonly record struct Ground(ReliefGrid Grid, int Cols, int Rows, double CellX, double CellY, double PerMetre, double HeightScale);

    internal static bool[] Padded(IEnumerable<IReadOnlyList<Spot>> rings, Ground g)
    {
        var padded = new bool[g.Cols * g.Rows];
        foreach (var ring in rings)
        {
            int x0 = Math.Clamp((int)(ring.Min(p => p.X) / g.CellX), 0, g.Cols - 1), x1 = Math.Clamp((int)Math.Ceiling(ring.Max(p => p.X) / g.CellX), 0, g.Cols - 1);
            int y0 = Math.Clamp((int)(ring.Min(p => p.Y) / g.CellY), 0, g.Rows - 1), y1 = Math.Clamp((int)Math.Ceiling(ring.Max(p => p.Y) / g.CellY), 0, g.Rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    padded[y * g.Cols + x] |= Enclosures.Inside(ring, new Spot(x * g.CellX, y * g.CellY));
        }
        return padded;
    }

    internal static void Write(IEnumerable<(BuiltPart Part, Profile Profile)> ways, Ground g, float[] raised, float[] cut, Func<int, bool> wet, bool[] padded)
    {
        var closest = new float[g.Cols * g.Rows];
        Array.Fill(closest, float.MaxValue);
        double raw = g.PerMetre / g.HeightScale, cell = Math.Max(g.CellX, g.CellY);
        double Bare(int x, int y) => g.Grid.HeightAt(x / (double)(g.Cols - 1), y / (double)(g.Rows - 1));
        double BareM(Spot p) => g.Grid.HeightAt(p.X / ((g.Cols - 1) * g.CellX), p.Y / ((g.Rows - 1) * g.CellY)) / raw;
        foreach (var (part, p) in ways)
        {
            double half = Math.Max(Grades.HalfM(part.Class) * g.PerMetre, cell * CellShare);
            for (int j = 0; j + 1 < p.Count; j++)
            {
                if (!p.Written(j)) continue;
                Spot a = p.At[j], b = p.At[j + 1];
                double most = Math.Max(Math.Abs(p.ChangeM(j)), Math.Abs(p.ChangeM(j + 1))) + Cross(BareM, a, b, half / g.PerMetre);
                double reach = half + Math.Max(SlopeLeastM, BankRun * most) * g.PerMetre;
                int x0 = Math.Clamp((int)((Math.Min(a.X, b.X) - reach) / g.CellX), 0, g.Cols - 1), x1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.X, b.X) + reach) / g.CellX), 0, g.Cols - 1);
                int y0 = Math.Clamp((int)((Math.Min(a.Y, b.Y) - reach) / g.CellY), 0, g.Rows - 1), y1 = Math.Clamp((int)Math.Ceiling((Math.Max(a.Y, b.Y) + reach) / g.CellY), 0, g.Rows - 1);
                var run = b - a;
                double runSquared = Math.Max(run.Dot(run), 1e-18);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * g.Cols + x;
                        var node = new Spot(x * g.CellX, y * g.CellY);
                        double t = Math.Clamp((node - a).Dot(run) / runSquared, 0, 1), off = (node - (a + run * t)).Length;
                        if (off > reach || off >= closest[i] || padded[i] && off > half || wet(i)) continue;
                        closest[i] = (float)off;
                        double share = 1 - Math.Clamp((off - half) / Math.Max(reach - half, 1e-9), 0, 1);
                        double by = ((p.Level[j] + (p.Level[j + 1] - p.Level[j]) * t) * raw - Bare(x, y)) * share;
                        raised[i] = (float)Math.Max(0, by);
                        cut[i] = (float)Math.Min(0, by);
                    }
            }
        }
    }
}
