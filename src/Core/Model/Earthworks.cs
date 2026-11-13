namespace KTable.Core.Model;

public static class Earthworks
{
    const double CrestShare = 0.42, CellShare = 1.25, PitSlopeM = 4, FineCellM = 1.6, CoarseCellM = 2.5, WaterCutM = 1.4, GradeWindowM = 35, GradeLeastM = 0.7, GradeHalfM = 3.4, GradeSlopeM = 5;
    const double WaterCutOfACell = 0.1, StreamCutM = 0.8, PondCutM = 1, DryFallM = 6, BankRun = 1.5, RiverHalfM = 7.5, StreamHalfM = 1.8;
    public const string Dried = "dry";
    const int CoarseSamples = 4, FineSamples = 2, MostFiner = 4, WaterSoftening = 5;

    readonly record struct Work(double Metres, double HalfM, bool Ring, byte On = GroundKinds.None, bool Crisp = true);

    static double WaterCut(double metres, double cellMetres) => Math.Max(metres, cellMetres * WaterCutOfACell);

    static Work? Of(BuiltPart part) => (part.Kind, part.Class) switch
    {
        (PartKinds.Line, "rampart") => new(3.6, 6, false),
        (PartKinds.Line, "causeway") => new(1.2, 3.2, false),
        (PartKinds.Line, "quay") => new(1.3, 3.4, false),
        (PartKinds.Line, "dam") => new(1.6, 2.2, false),
        (PartKinds.Line, "mole") => new(1.9, 2.6, false),
        (PartKinds.Line, "glacis") => new(1, 9, false),
        (PartKinds.Line, "track" or "lane" or "road" or "street" or "chaussee") => new(1, 3.4, false, GroundKinds.Marsh),
        (PartKinds.Line, "ditch") => new(-1.8, 2.2, false),
        (PartKinds.Line, "moat") => new(-3, 7.5, false),
        (PartKinds.Area, "moat") => new(-3, PitSlopeM, true),
        (PartKinds.Area, "quarry") => new(-5, PitSlopeM, true),
        (PartKinds.Area, "pond") => new(-PondCutM, PitSlopeM, true, Crisp: false),
        _ => null,
    };

    public static long Key(IEnumerable<BuiltThing> things)
    {
        const long Prime = 1099511628211;
        long key = 0;
        foreach (var part in things.SelectMany(t => t.Parts).Where(p => Of(p) != null || Runs(p)))
        {
            key = unchecked((key ^ part.Class.GetHashCode(StringComparison.Ordinal) ^ 1469598103934665603) * Prime);
            foreach (int v in part.At) key = unchecked((key ^ v) * Prime);
        }
        return key;
    }

    static bool Runs(BuiltPart part) => part.Kind == PartKinds.Line && part.Class is "river" or "stream";

    static bool Stands(BuiltPart part) => (part.Kind, part.Class) is (PartKinds.Line, "moat" or "ditch") or (PartKinds.Area, "moat" or "pond");

    static List<Spot> Stations(List<Spot> path, double step)
    {
        var at = new List<Spot>();
        for (int i = 0; i + 1 < path.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((path[i + 1] - path[i]).Length / step));
            for (int k = i == 0 ? 0 : 1; k <= steps; k++) at.Add(path[i] + (path[i + 1] - path[i]) * (k / (double)steps));
        }
        return at;
    }

    static (double Level, double FallM) Lie(ReliefGrid grid, BuiltPart part, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        var path = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        if (part.Kind == PartKinds.Area && path.Count > 0) path.Add(path[0]);
        double step = Math.Max(fullWidth / Math.Max(1, grid.Cols - 1), fullHeight / Math.Max(1, grid.Rows - 1));
        double low = double.MaxValue, high = double.MinValue;
        foreach (var p in Stations(path, step))
        {
            double h = grid.HeightAt(p.X / fullWidth, p.Y / fullHeight);
            low = Math.Min(low, h);
            high = Math.Max(high, h);
        }
        return low > high ? (0, 0) : (low, (high - low) * heightScale / unitsPerMetre);
    }

    public static bool Dry(ReliefGrid grid, BuiltPart part, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale) =>
        Stands(part) && part.Class != "pond" && Lie(grid, part, fullWidth, fullHeight, unitsPerMetre, heightScale).FallM > DryFallM;

    public static IReadOnlyList<BuiltThing> Seen(ReliefGrid? grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        if (grid == null || unitsPerMetre <= 0 || heightScale <= 0) return things;
        List<BuiltThing>? seen = null;
        for (int t = 0; t < things.Count; t++)
        {
            BuiltThing? dried = null;
            for (int p = 0; p < things[t].Parts.Count; p++)
            {
                if (!Dry(grid, things[t].Parts[p], fullWidth, fullHeight, unitsPerMetre, heightScale)) continue;
                dried ??= things[t].Clone();
                dried.Parts[p].Class = Dried + things[t].Parts[p].Class;
            }
            if (dried == null) continue;
            seen ??= things.ToList();
            seen[t] = dried;
        }
        return seen ?? things;
    }

    static (List<Spot> At, double[] Change, double HalfM)? Carved(ReliefGrid grid, BuiltPart part, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale, double step)
    {
        if (!Runs(part)) return null;
        var at = Stations(BuiltGeometry.Points(part.At).Select(Spot.Of).ToList(), step);
        if (at.Count < 2) return null;
        bool river = part.Class == "river";
        double deep = WaterCut(river ? WaterCutM : StreamCutM, step / unitsPerMetre) * unitsPerMetre / heightScale, level = double.MaxValue;
        var change = new double[at.Count];
        for (int i = 0; i < at.Count; i++)
        {
            double high = grid.HeightAt(at[i].X / fullWidth, at[i].Y / fullHeight);
            level = Math.Min(level, high);
            change[i] = level - deep - high;
        }
        return (at, change, river ? RiverHalfM : StreamHalfM);
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

    public sealed record Laid(int Times, bool Fine, float[] Raised, float[] Cut);

    static (List<Spot> At, double[] Change)? Graded(ReliefGrid grid, BuiltPart part, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale, double step)
    {
        if (part.Kind != PartKinds.Line || part.Class is not ("track" or "lane" or "road" or "street" or "chaussee")) return null;
        var at = Stations(BuiltGeometry.Points(part.At).Select(Spot.Of).ToList(), step);
        if (at.Count < 3) return null;
        var high = at.Select(p => (double)grid.HeightAt(p.X / fullWidth, p.Y / fullHeight)).ToArray();
        int window = Math.Max(1, (int)(GradeWindowM * unitsPerMetre / step));
        var change = new double[at.Count];
        double most = 0;
        for (int i = 0; i < at.Count; i++)
        {
            int a = Math.Max(0, i - window), b = Math.Min(at.Count - 1, i + window);
            double even = 0;
            for (int j = a; j <= b; j++) even += high[j];
            change[i] = even / (b - a + 1) - high[i];
            most = Math.Max(most, Math.Abs(change[i]));
        }
        return most * heightScale / unitsPerMetre < GradeLeastM ? null : (at, change);
    }

    static void Grade(List<(List<Spot> At, double[] Change)> roads, float[] raised, float[] cut, int cols, int rows, double cellX, double cellY, double half, double reach)
    {
        var closest = new float[cols * rows];
        Array.Fill(closest, float.MaxValue);
        foreach (var (at, change) in roads)
            for (int j = 0; j < at.Count; j++)
            {
                int x0 = Math.Clamp((int)((at[j].X - reach) / cellX), 0, cols - 1), x1 = Math.Clamp((int)Math.Ceiling((at[j].X + reach) / cellX), 0, cols - 1);
                int y0 = Math.Clamp((int)((at[j].Y - reach) / cellY), 0, rows - 1), y1 = Math.Clamp((int)Math.Ceiling((at[j].Y + reach) / cellY), 0, rows - 1);
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        int i = y * cols + x;
                        double off = (new Spot(x * cellX, y * cellY) - at[j]).Length;
                        if (off > reach || off >= closest[i]) continue;
                        closest[i] = (float)off;
                        float by = (float)(change[j] * (1 - Math.Clamp((off - half) / (reach - half), 0, 1)));
                        raised[i] = Math.Max(0, by);
                        cut[i] = Math.Min(0, by);
                    }
            }
    }

    public static Laid? Lay(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        bool Lies(BuiltPart part, byte on) => on == GroundKinds.None || BuiltGeometry.Points(part.At).Select(Spot.Of).Any(p => grid.KindAt(p.X / fullWidth, p.Y / fullHeight) == on);
        var works = things.SelectMany(t => t.Parts).Select(p => (Part: p, Work: Of(p))).Where(w => w.Work != null && Lies(w.Part, w.Work.Value.On)).ToList();
        if (unitsPerMetre <= 0 || heightScale <= 0) return null;
        double coarse = Math.Max(fullWidth / (grid.Cols - 1), fullHeight / (grid.Rows - 1));
        var roads = things.SelectMany(t => t.Parts).Select(p => Graded(grid, p, fullWidth, fullHeight, unitsPerMetre, heightScale, coarse)).Where(r => r != null).Select(r => r!.Value).ToList();
        var waters = things.SelectMany(t => t.Parts).Select(p => Carved(grid, p, fullWidth, fullHeight, unitsPerMetre, heightScale, coarse)).Where(w => w != null).Select(w => w!.Value).ToList();
        if (works.Count == 0 && roads.Count == 0 && waters.Count == 0) return null;
        int most = Math.Max(grid.Cols - 1, grid.Rows - 1), wanted = (int)Math.Ceiling(Math.Max(fullWidth / (grid.Cols - 1), fullHeight / (grid.Rows - 1)) / unitsPerMetre / FineCellM);
        int times = Math.Clamp(Math.Min(wanted, Ground.MostVertsAcross * Ground.WorkedFiner / most), 1, MostFiner);
        bool crisp = works.Any(w => w.Work!.Value.Crisp);
        if (!crisp || !grid.CanBeFiner(times)) times = 1;
        int cols = (grid.Cols - 1) * times + 1, rows = (grid.Rows - 1) * times + 1;
        float[] raised = new float[cols * rows], cut = new float[cols * rows];
        double cellX = fullWidth / (cols - 1), cellY = fullHeight / (rows - 1), cell = Math.Max(cellX, cellY);
        int samples = cell / unitsPerMetre > CoarseCellM ? CoarseSamples : FineSamples;
        double roadHalf = Math.Max(GradeHalfM * unitsPerMetre, cell * CellShare);
        if (roads.Count > 0) Grade(roads, raised, cut, cols, rows, cellX, cellY, roadHalf, roadHalf + GradeSlopeM * unitsPerMetre);
        foreach (var (bed, change, halfM) in waters)
        {
            double bedHalf = Math.Max(halfM * unitsPerMetre, cell * CellShare), deepestM = -change.Min() * heightScale / unitsPerMetre;
            Grade([(bed, change)], raised, cut, cols, rows, cellX, cellY, bedHalf, bedHalf + Math.Max(GradeSlopeM, deepestM * BankRun) * unitsPerMetre);
        }
        foreach (var (part, found) in works)
        {
            var work = found!.Value;
            var at = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (at.Count < 2) continue;
            double half = Math.Max(work.HalfM * unitsPerMetre, cell * CellShare), reach = (work.Ring ? 0 : half) + cell;
            double change = work.Metres * unitsPerMetre / heightScale;
            bool level = Stands(part) && !Dry(grid, part, fullWidth, fullHeight, unitsPerMetre, heightScale);
            double floor = level ? Lie(grid, part, fullWidth, fullHeight, unitsPerMetre, heightScale).Level + change : 0;
            int x0 = Math.Clamp((int)((at.Min(p => p.X) - reach) / cellX), 0, cols - 1), x1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.X) + reach) / cellX), 0, cols - 1);
            int y0 = Math.Clamp((int)((at.Min(p => p.Y) - reach) / cellY), 0, rows - 1), y1 = Math.Clamp((int)Math.Ceiling((at.Max(p => p.Y) + reach) / cellY), 0, rows - 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (work.On != GroundKinds.None && grid.KindAt(x / (double)(cols - 1), y / (double)(rows - 1)) != work.On) continue;
                    double share = Spread(at, new Spot(x * cellX, y * cellY), cellX, cellY, work.Ring, half, samples);
                    if (share <= 0) continue;
                    int i = y * cols + x;
                    if (level) cut[i] = Math.Min(cut[i], (float)Math.Min(0, (floor - grid.HeightAt(x / (double)(cols - 1), y / (double)(rows - 1))) * share));
                    else if (change > 0) raised[i] = Math.Max(raised[i], (float)(change * share));
                    else cut[i] = Math.Min(cut[i], (float)(change * share));
                }
        }
        Softened(raised, cols, rows);
        Softened(cut, cols, rows);
        return new Laid(times, crisp, raised, cut);
    }

    public sealed record Dug(float[] Cut, float Lift, long Water);

    public static long Waters(ReliefGrid g)
    {
        long mark = 0;
        for (int i = 0; i < g.Kind.Length; i++)
            if (g.Kind[i] == GroundKinds.Water) mark = unchecked(mark * 31 + i + 1);
        return mark;
    }

    static Dug? Dig(ReliefGrid shaped, Laid? laid, double fullWidth, double unitsPerMetre, double heightScale)
    {
        bool wet = Array.IndexOf(shaped.Kind, GroundKinds.Water) >= 0;
        if (laid == null && !wet) return null;
        var cut = laid == null ? new float[shaped.H.Length] : (float[])laid.Cut.Clone();
        if (wet)
        {
            float deep = (float)(WaterCut(WaterCutM, fullWidth / Math.Max(1, shaped.Cols - 1) / unitsPerMetre) * unitsPerMetre / heightScale);
            var water = new float[shaped.H.Length];
            var levels = Levels(shaped, heightScale / unitsPerMetre, fullWidth / Math.Max(1, shaped.Cols - 1) / unitsPerMetre);
            for (int i = 0; i < water.Length; i++)
                if (shaped.Kind[i] == GroundKinds.Water) water[i] = levels[i] - deep - shaped.H[i];
            var beds = (float[])water.Clone();
            for (int pass = 0; pass < WaterSoftening; pass++) Softened(water, shaped.Cols, shaped.Rows);
            for (int i = 0; i < cut.Length; i++) cut[i] = Math.Min(cut[i], Math.Min(water[i], beds[i]));
        }
        float lift = 0;
        for (int i = 0; i < cut.Length; i++) lift = Math.Max(lift, -(shaped.H[i] + cut[i]));
        return new Dug(cut, lift, Waters(shaped));
    }

    static float[] Levels(ReliefGrid g, double metresPerHeightUnit, double cellMetres)
    {
        int n = g.H.Length, cols = g.Cols, rows = g.Rows;
        var kinds = GroundStreams.Waters(g);
        var away = GroundStreams.Away(cols, rows, g.H, kinds, metresPerHeightUnit);
        var narrow = GroundStreams.Narrow(cols, rows, kinds, cellMetres);
        var level = (float[])g.H.Clone();
        var running = Enumerable.Range(0, n).Where(i => GroundStreams.River(kinds[i]) && away[i] != GroundStreams.NoMouth).OrderByDescending(i => away[i]).ToList();
        foreach (int i in running)
            foreach (int j in Beside(i, cols, rows))
                if (GroundStreams.River(kinds[j]) && away[j] < away[i]) level[j] = Math.Min(level[j], level[i]);
        var seen = new bool[n];
        var body = new List<int>();
        var queue = new Queue<int>();
        var wide = new int[n];
        for (int i = 0; i < n; i++)
            if (GroundStreams.River(kinds[i]) && !narrow[i])
            {
                wide[i] = GroundStreams.Reach(cellMetres) + 1;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            int i = queue.Dequeue();
            foreach (int j in Beside(i, cols, rows))
            {
                if (!GroundStreams.River(kinds[j]) || wide[j] >= wide[i] - 1) continue;
                wide[j] = wide[i] - 1;
                queue.Enqueue(j);
            }
        }
        bool Still(int i) => kinds[i] == GroundStreams.SeaWater || GroundStreams.River(kinds[i]) && (away[i] == GroundStreams.NoMouth || wide[i] > 0);
        for (int start = 0; start < n; start++)
        {
            if (seen[start] || !Still(start)) continue;
            body.Clear();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                body.Add(i);
                foreach (int j in Beside(i, cols, rows))
                {
                    if (seen[j] || !Still(j) || kinds[j] == GroundStreams.SeaWater != (kinds[start] == GroundStreams.SeaWater)) continue;
                    seen[j] = true;
                    queue.Enqueue(j);
                }
            }
            float low = body.Min(i => level[i]);
            foreach (int i in body) level[i] = low;
        }
        return level;
    }

    static IEnumerable<int> Beside(int i, int cols, int rows)
    {
        int x = i % cols, y = i / cols;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if ((dx != 0 || dy != 0) && x + dx >= 0 && y + dy >= 0 && x + dx < cols && y + dy < rows) yield return i + dy * cols + dx;
    }

    public static ReliefGrid Shaped(ReliefGrid grid, Laid? laid, ref Dug? dug, bool kept, double fullWidth, double unitsPerMetre, double heightScale)
    {
        if (unitsPerMetre <= 0 || heightScale <= 0) return grid;
        if (laid == null && (kept ? dug == null : Array.IndexOf(grid.Kind, GroundKinds.Water) < 0))
        {
            if (!kept) dug = null;
            return grid;
        }
        var shaped = laid is { Times: > 1 } ? grid.Finer(laid.Times) : grid.Copy();
        if (laid != null && laid.Raised.Length != shaped.H.Length) return grid;
        shaped.Worked = laid is { Fine: true };
        if (!kept || dug == null || dug.Cut.Length != shaped.H.Length) dug = Dig(shaped, laid, fullWidth, unitsPerMetre, heightScale);
        if (dug == null) return grid;
        for (int i = 0; i < shaped.H.Length; i++) shaped.H[i] = Math.Max(0, shaped.H[i] + dug.Lift + dug.Cut[i] + (laid?.Raised[i] ?? 0));
        return shaped;
    }

    public static void Redone(ReliefGrid grid, Laid? laid, Dug dug, ReliefGrid shaped, CellBox touched)
    {
        int times = (shaped.Cols - 1) / Math.Max(1, grid.Cols - 1);
        var box = new CellBox((touched.X0 - 1) * times, (touched.Y0 - 1) * times, (touched.X1 + 1) * times, (touched.Y1 + 1) * times).Clip(shaped.Cols, shaped.Rows);
        grid.FinerInto(shaped, times, box);
        for (int y = box.Y0; y <= box.Y1; y++)
            for (int x = box.X0; x <= box.X1; x++)
            {
                int i = y * shaped.Cols + x;
                shaped.H[i] = Math.Max(0, shaped.H[i] + dug.Lift + dug.Cut[i] + (laid?.Raised[i] ?? 0));
            }
    }

    public static ReliefGrid Shaped(ReliefGrid grid, IReadOnlyList<BuiltThing> things, double fullWidth, double fullHeight, double unitsPerMetre, double heightScale)
    {
        Dug? dug = null;
        return Shaped(grid, Lay(grid, things, fullWidth, fullHeight, unitsPerMetre, heightScale), ref dug, false, fullWidth, unitsPerMetre, heightScale);
    }
}
