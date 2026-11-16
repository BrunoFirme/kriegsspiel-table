namespace KTable.Core.Model;

static class Benches
{
    const double PoolDeepM = 0.8, LeatHalfM = 0.9, ChannelHalfM = 0.5, LeatDeepM = 0.6, ChannelDeepM = 0.4, BankM = 1.5, Parallel = 1e-12, StationShare = 0.5;
    const int LeastCells = 2;

    public static bool Works(BuiltPart part) => Terraces.Shapes(part) || Watercourses.Runs(part);

    static double Cross(Spot a, Spot b) => a.X * b.Y - a.Y * b.X;

    readonly record struct Box(double X0, double Y0, double X1, double Y1)
    {
        public static Box Of(IReadOnlyList<Spot> ring) => new(ring.Min(p => p.X), ring.Min(p => p.Y), ring.Max(p => p.X), ring.Max(p => p.Y));

        public bool Meets(Box o) => X0 <= o.X1 && o.X0 <= X1 && Y0 <= o.Y1 && o.Y0 <= Y1;

        public Box Grown(double by) => new(X0 - by, Y0 - by, X1 + by, Y1 + by);
    }

    sealed record Grid(ReliefGrid Bare, float[] Raised, float[] Cut, int Cols, int Rows, double CellX, double CellY)
    {
        public double Cell => Math.Max(CellX, CellY);

        public double HeightAt(Spot p) => Bare.HeightAt(p.X / ((Cols - 1) * CellX), p.Y / ((Rows - 1) * CellY));

        public double BareAt(int x, int y) => Bare.HeightAt(x / (double)(Cols - 1), y / (double)(Rows - 1));

        public int Index(Spot p) => Math.Clamp((int)Math.Round(p.Y / CellY), 0, Rows - 1) * Cols + Math.Clamp((int)Math.Round(p.X / CellX), 0, Cols - 1);

        public double Worked(Spot p) => HeightAt(p) + Raised[Index(p)] + Cut[Index(p)];

        public (int X0, int Y0, int X1, int Y1) Nodes(Box box) => (Math.Clamp((int)(box.X0 / CellX), 0, Cols - 1), Math.Clamp((int)(box.Y0 / CellY), 0, Rows - 1),
            Math.Clamp((int)Math.Ceiling(box.X1 / CellX), 0, Cols - 1), Math.Clamp((int)Math.Ceiling(box.Y1 / CellY), 0, Rows - 1));
    }

    static Spot Tilt(IReadOnlyList<Spot> ring, Func<Spot, double> bare)
    {
        var middle = Terraces.Middle(ring);
        double heights = ring.Average(bare), xx = 0, xy = 0, yy = 0, xh = 0, yh = 0;
        foreach (var p in ring)
        {
            var d = p - middle;
            double h = bare(p) - heights;
            (xx, xy, yy, xh, yh) = (xx + d.X * d.X, xy + d.X * d.Y, yy + d.Y * d.Y, xh + d.X * h, yh + d.Y * h);
        }
        double det = xx * yy - xy * xy;
        if (Math.Abs(det) < Parallel) return (ring[ring.Count / 2] - ring[0]).Unit;
        return new Spot(-(yy * xh - xy * yh) / det, -(xx * yh - xy * xh) / det).Unit;
    }

    static Func<Spot, double> Across(IReadOnlyList<Spot> ring, Func<Spot, double> bare)
    {
        var fall = Tilt(ring, bare);
        return node =>
        {
            double up = double.MinValue, down = double.MaxValue;
            for (int i = 0; i < ring.Count; i++)
            {
                Spot a = ring[i], run = ring[(i + 1) % ring.Count] - a, from = a - node;
                double turn = Cross(fall, run);
                if (Math.Abs(turn) < Parallel) continue;
                double s = Cross(from, run) / turn, r = Cross(from, fall) / turn;
                if (r < 0 || r > 1) continue;
                (up, down) = s <= 0 ? (Math.Max(up, s), down) : (up, Math.Min(down, s));
            }
            return up == double.MinValue || down == double.MaxValue ? bare(node) : (bare(node + fall * up) + bare(node + fall * down)) / 2;
        };
    }

    static double Median(IReadOnlyList<Spot> ring, Func<Spot, double> bare)
    {
        var heights = ring.Select(bare).OrderBy(h => h).ToList();
        return heights.Count % 2 == 1 ? heights[heights.Count / 2] : (heights[heights.Count / 2 - 1] + heights[heights.Count / 2]) / 2;
    }

    static Func<Spot, double> Level(BuiltPart part, IReadOnlyList<Spot> ring, Func<Spot, double> bare, double perMetre)
    {
        if (part.Class == Terraces.Terrace) return Across(ring, bare);
        double level = Median(ring, bare) - (part.Class == Terraces.Pool ? PoolDeepM * perMetre : 0);
        return _ => level;
    }

    internal static void Lay(ReliefGrid bare, IReadOnlyList<BuiltPart> parts, IEnumerable<IReadOnlyList<Spot>> pads, float[] raised, float[] cut, int cols, int rows, double cellX, double cellY,
        double unitsPerMetre, double heightScale)
    {
        var grid = new Grid(bare, raised, cut, cols, rows, cellX, cellY);
        var footed = pads.Select(ring => (Ring: ring, Box: Box.Of(ring))).ToList();
        foreach (var part in parts.Where(Terraces.Shapes)) Bench(grid, part, footed, unitsPerMetre / heightScale);
        foreach (var part in parts.Where(Watercourses.Runs)) Flow(grid, part, unitsPerMetre, unitsPerMetre / heightScale);
    }

    static void Bench(Grid grid, BuiltPart part, List<(IReadOnlyList<Spot> Ring, Box Box)> footed, double perMetre)
    {
        var ring = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        if (ring.Count < 3 || Footings.Narrowest(ring) < LeastCells * grid.Cell) return;
        var level = Level(part, ring, grid.HeightAt, perMetre);
        var box = Box.Of(ring);
        var under = footed.Where(f => f.Box.Meets(box)).Select(f => f.Ring).ToList();
        var (x0, y0, x1, y1) = grid.Nodes(box);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var node = new Spot(x * grid.CellX, y * grid.CellY);
                if (!Enclosures.Inside(ring, node) || under.Any(pad => Enclosures.Inside(pad, node))) continue;
                double change = level(node) - grid.BareAt(x, y);
                grid.Raised[y * grid.Cols + x] = (float)Math.Max(0, change);
                grid.Cut[y * grid.Cols + x] = (float)Math.Min(0, change);
            }
    }

    static List<Spot> Stations(List<Spot> line, double step)
    {
        var at = new List<Spot> { line[0] };
        for (int i = 0; i + 1 < line.Count; i++)
        {
            int steps = Math.Max(1, (int)Math.Ceiling((line[i + 1] - line[i]).Length / step));
            for (int k = 1; k <= steps; k++) at.Add(line[i] + (line[i + 1] - line[i]) * (k / (double)steps));
        }
        return at;
    }

    static void Flow(Grid grid, BuiltPart part, double unitsPerMetre, double perMetre)
    {
        var line = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        if (line.Count < 2) return;
        bool leat = part.Class == Watercourses.Leat;
        double half = Math.Max((leat ? LeatHalfM : ChannelHalfM) * unitsPerMetre, grid.Cell), reach = half + Math.Max(BankM * unitsPerMetre, grid.Cell);
        var stations = Stations(line, grid.Cell * StationShare);
        var beds = new double[stations.Count];
        double low = double.MaxValue;
        for (int j = 0; j < stations.Count; j++)
        {
            low = Math.Min(low, grid.Worked(stations[j]));
            beds[j] = low - (leat ? LeatDeepM : ChannelDeepM) * perMetre;
        }
        var (x0, y0, x1, y1) = grid.Nodes(Box.Of(stations).Grown(reach));
        int across = x1 - x0 + 1;
        var closest = new double[across * (y1 - y0 + 1)];
        var bed = new double[closest.Length];
        Array.Fill(closest, double.MaxValue);
        for (int j = 0; j < stations.Count; j++)
        {
            var (a0, b0, a1, b1) = grid.Nodes(new Box(stations[j].X - reach, stations[j].Y - reach, stations[j].X + reach, stations[j].Y + reach));
            for (int y = b0; y <= b1; y++)
                for (int x = a0; x <= a1; x++)
                {
                    int k = (y - y0) * across + x - x0;
                    double off = (new Spot(x * grid.CellX, y * grid.CellY) - stations[j]).Length;
                    if (off >= closest[k]) continue;
                    closest[k] = off;
                    bed[k] = beds[j];
                }
        }
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int k = (y - y0) * across + x - x0, i = y * grid.Cols + x;
                if (closest[k] > reach) continue;
                double worked = grid.BareAt(x, y) + grid.Raised[i] + grid.Cut[i], share = 1 - Math.Clamp((closest[k] - half) / (reach - half), 0, 1);
                if (bed[k] < worked) grid.Cut[i] += (float)((bed[k] - worked) * share);
            }
    }
}
