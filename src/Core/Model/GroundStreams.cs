namespace KTable.Core.Model;

public sealed record GroundFlow(byte[] Order, byte[] Direction, (float x, float y)[] Node, byte[] Heading, byte[] Current, bool[] Culvert);

public static class GroundStreams
{
    public const byte NoFlow = 8;
    public const byte Still = 0, Flows = 1, Rapids = 2, Fall = 3;
    public const int Headings = 32;
    public const double StreamKm2 = 0.15, BrookKm2 = 1, RiverKm2 = 10;
    const double FloorSlope = 0.002, RapidsSlope = 0.02, RiverHalfWidthMetres = 50, FallMetres = 5;
    const double CrossingCosine = 0.866, RoadAxisClarity = 0.2;
    const int MaxReach = 8, RoadReach = 4, CrossingRun = 3;
    const float Lift = 1e-6f, NodeReach = 0.45f, NodePull = 0.25f;
    const int NodeSteps = 31, NodeShift = 4, NodeBits = 5, CurrentBit = 1 << 14, CulvertBit = 1 << 15;
    static readonly (int dx, int dy)[] Eight = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    public static GroundFlow Flow(int cols, int rows, float[] heights, byte[]? kinds, double cellMetres, double metresPerHeightUnit)
    {
        int n = cols * rows;
        bool Sink(int i) => kinds != null && kinds[i] == GroundKinds.Water;
        var filled = Filled(heights, cols, rows, Sink);
        var direction = new byte[n];
        var drop = new float[n];
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int i = y * cols + x;
                direction[i] = NoFlow;
                if (Sink(i)) continue;
                double best = 0;
                for (int d = 0; d < Eight.Length; d++)
                {
                    int nx = x + Eight[d].dx, ny = y + Eight[d].dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    double fall = (filled[i] - filled[ny * cols + nx]) / (d % 2 == 1 ? 1.41421356 : 1);
                    if (fall <= best) continue;
                    best = fall;
                    direction[i] = (byte)d;
                    drop[i] = (float)fall;
                }
            }
        var area = new int[n];
        Array.Fill(area, 1);
        var downhill = new int[n];
        for (int i = 0; i < n; i++) downhill[i] = i;
        Array.Sort((float[])filled.Clone(), downhill);
        for (int k = n - 1; k >= 0; k--)
        {
            int i = downhill[k];
            if (direction[i] == NoFlow) continue;
            area[Downstream(i, direction[i], cols)] += area[i];
        }
        double cellKm2 = cellMetres * cellMetres / 1e6;
        var order = new byte[n];
        for (int i = 0; i < n; i++)
        {
            if (Sink(i)) continue;
            double km2 = area[i] * cellKm2;
            byte o = km2 >= RiverKm2 ? (byte)3 : km2 >= BrookKm2 ? (byte)2 : km2 >= StreamKm2 ? (byte)1 : (byte)0;
            if (o is 1 or 2 && drop[i] * metresPerHeightUnit / cellMetres < FloorSlope) o = 0;
            order[i] = o;
        }
        var (heading, current) = Currents(cols, rows, heights, kinds, cellMetres, metresPerHeightUnit);
        return new GroundFlow(order, direction, Nodes(cols, direction, order, area), heading, current, Culverts(cols, rows, kinds, order, direction));
    }

    public static float Packed(byte direction, (float x, float y) node, bool culvert = false)
    {
        int qx = Quantised(node.x), qy = Quantised(node.y);
        return direction | qx << NodeShift | qy << (NodeShift + NodeBits) | (culvert ? CulvertBit : 0);
    }

    public static float PackedCurrent(byte heading, byte current) => NoFlow | heading << NodeShift | current << (NodeShift + NodeBits) | CurrentBit;

    public static float Packed(GroundFlow f, int i) => f.Current[i] > Still ? PackedCurrent(f.Heading[i], f.Current[i]) : Packed(f.Direction[i], f.Node[i], f.Culvert[i]);

    static int Quantised(float offset) => (int)Math.Round((Math.Clamp(offset, -NodeReach, NodeReach) / NodeReach * 0.5 + 0.5) * NodeSteps);

    static int Downstream(int i, byte d, int cols) => i + Eight[d].dy * cols + Eight[d].dx;

    static (byte[] Heading, byte[] Current) Currents(int cols, int rows, float[] heights, byte[]? kinds, double cellMetres, double metresPerHeightUnit)
    {
        int n = cols * rows;
        var heading = new byte[n];
        var current = new byte[n];
        if (kinds == null || cellMetres <= 0) return (heading, current);
        int reach = Math.Clamp((int)Math.Ceiling(RiverHalfWidthMetres / cellMetres), 1, MaxReach);
        bool Water(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && kinds[y * cols + x] == GroundKinds.Water;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                if (!Water(x, y)) continue;
                var (ax, ay) = Axis(x, y, reach, Water);
                if (!ShoreWithin(x, y, -ay, ax, reach, Water) || !ShoreWithin(x, y, ay, -ax, reach, Water)) continue;
                var (hBack, back) = Along(x, y, -ax, -ay, reach, heights, cols, Water);
                var (hOn, on) = Along(x, y, ax, ay, reach, heights, cols, Water);
                if (back + on == 0) continue;
                double slope = (hBack - hOn) * metresPerHeightUnit / ((back + on) * cellMetres);
                if (slope < 0) { (ax, ay, slope) = (-ax, -ay, -slope); }
                if (slope < FloorSlope) continue;
                int i = y * cols + x;
                heading[i] = (byte)(((int)Math.Round(Math.Atan2(ay, ax) / (2 * Math.PI) * Headings) % Headings + Headings) % Headings);
                int nx = x + (int)Math.Round(ax), ny = y + (int)Math.Round(ay);
                bool fall = Water(nx, ny) && (heights[i] - heights[ny * cols + nx]) * metresPerHeightUnit > FallMetres;
                current[i] = fall ? Fall : slope > RapidsSlope ? Rapids : Flows;
            }
        return (heading, current);
    }

    static (double x, double y) Axis(int x, int y, int reach, Func<int, int, bool> within)
    {
        var (sxx, sxy, syy) = Tensor(x, y, reach, within);
        double angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        return (Math.Cos(angle), Math.Sin(angle));
    }

    static double Clarity(int x, int y, int reach, Func<int, int, bool> within)
    {
        var (sxx, sxy, syy) = Tensor(x, y, reach, within);
        double sum = sxx + syy;
        return sum > 0 ? Math.Sqrt((sxx - syy) * (sxx - syy) + 4 * sxy * sxy) / sum : 0;
    }

    static (double xx, double xy, double yy) Tensor(int x, int y, int reach, Func<int, int, bool> within)
    {
        double sxx = 0, sxy = 0, syy = 0;
        for (int dy = -reach; dy <= reach; dy++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (!within(x + dx, y + dy)) continue;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }
        return (sxx, sxy, syy);
    }

    static bool ShoreWithin(int x, int y, double nx, double ny, int reach, Func<int, int, bool> water)
    {
        for (int t = 1; t <= reach; t++)
            if (!water(x + (int)Math.Round(nx * t), y + (int)Math.Round(ny * t))) return true;
        return false;
    }

    static (double Height, int Steps) Along(int x, int y, double ax, double ay, int reach, float[] heights, int cols, Func<int, int, bool> water)
    {
        double h = heights[y * cols + x];
        int steps = 0;
        for (int t = 1; t <= reach; t++)
        {
            int cx = x + (int)Math.Round(ax * t), cy = y + (int)Math.Round(ay * t);
            if (!water(cx, cy)) break;
            h = heights[cy * cols + cx];
            steps = t;
        }
        return (h, steps);
    }

    static bool[] Culverts(int cols, int rows, byte[]? kinds, byte[] order, byte[] direction)
    {
        var culvert = new bool[order.Length];
        if (kinds == null) return culvert;
        bool Road(int x, int y) => x >= 0 && y >= 0 && x < cols && y < rows && kinds[y * cols + x] is GroundKinds.Road or GroundKinds.Bridge;
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                int i = y * cols + x;
                if (order[i] == 0 || direction[i] == NoFlow || !Road(x, y)) continue;
                if (Clarity(x, y, RoadReach, Road) < RoadAxisClarity) { culvert[i] = true; continue; }
                var (rx, ry) = Axis(x, y, RoadReach, Road);
                double sx = 0, sy = 0;
                int j = i;
                for (int step = 0; step < CrossingRun && direction[j] != NoFlow; step++)
                {
                    var (dx, dy) = Eight[direction[j]];
                    double len = dx != 0 && dy != 0 ? 1.41421356 : 1;
                    sx += dx / len;
                    sy += dy / len;
                    j = Downstream(j, direction[j], cols);
                }
                double run = Math.Sqrt(sx * sx + sy * sy);
                culvert[i] = run > 0 && Math.Abs(sx * rx + sy * ry) / run < CrossingCosine;
            }
        return culvert;
    }

    static (float x, float y)[] Nodes(int cols, byte[] direction, byte[] order, int[] area)
    {
        int n = direction.Length;
        var feeder = new int[n];
        Array.Fill(feeder, -1);
        for (int i = 0; i < n; i++)
        {
            if (direction[i] == NoFlow) continue;
            int j = Downstream(i, direction[i], cols);
            if (feeder[j] < 0 || area[i] > area[feeder[j]]) feeder[j] = i;
        }
        var node = new (float x, float y)[n];
        for (int i = 0; i < n; i++)
        {
            if (order[i] == 0 || direction[i] == NoFlow) continue;
            var (dx, dy) = Eight[direction[i]];
            float x = dx * NodePull, y = dy * NodePull;
            if (feeder[i] >= 0)
            {
                x += (feeder[i] % cols - i % cols) * NodePull;
                y += (feeder[i] / cols - i / cols) * NodePull;
            }
            node[i] = (Math.Clamp(x, -NodeReach, NodeReach), Math.Clamp(y, -NodeReach, NodeReach));
        }
        return node;
    }

    public static float[] Filled(float[] heights, int cols, int rows, Func<int, bool> sink)
    {
        var o = (float[])heights.Clone();
        var seen = new bool[o.Length];
        var queue = new PriorityQueue<int, float>();
        for (int x = 0; x < cols; x++) { Seed(x); Seed((rows - 1) * cols + x); }
        for (int y = 1; y < rows - 1; y++) { Seed(y * cols); Seed(y * cols + cols - 1); }
        for (int i = 0; i < o.Length; i++) if (sink(i)) Seed(i);
        void Seed(int i)
        {
            if (seen[i]) return;
            seen[i] = true;
            queue.Enqueue(i, o[i]);
        }
        while (queue.TryDequeue(out int i, out _))
        {
            int x = i % cols, y = i / cols;
            foreach (var (dx, dy) in Eight)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                int j = ny * cols + nx;
                if (seen[j]) continue;
                seen[j] = true;
                if (o[j] <= o[i]) o[j] = o[i] + Lift;
                queue.Enqueue(j, o[j]);
            }
        }
        return o;
    }
}
