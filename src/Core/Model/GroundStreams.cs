namespace KTable.Core.Model;

public static class GroundStreams
{
    public const byte NoFlow = 8;
    public const double StreamKm2 = 0.15, BrookKm2 = 1, RiverKm2 = 10;
    const double FloorSlope = 0.002;
    const float Lift = 1e-6f, NodeReach = 0.45f, NodePull = 0.25f;
    const int NodeSteps = 31, NodeShift = 4, NodeBits = 5;
    static readonly (int dx, int dy)[] Eight = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    public static (byte[] Order, byte[] Direction, (float x, float y)[] Node) Flow(int cols, int rows, float[] heights, byte[]? kinds, double cellMetres, double metresPerHeightUnit)
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
        return (order, direction, Nodes(cols, direction, order, area));
    }

    public static float Packed(byte direction, (float x, float y) node)
    {
        int qx = Quantised(node.x), qy = Quantised(node.y);
        return direction | qx << NodeShift | qy << (NodeShift + NodeBits);
    }

    static int Quantised(float offset) => (int)Math.Round((Math.Clamp(offset, -NodeReach, NodeReach) / NodeReach * 0.5 + 0.5) * NodeSteps);

    static int Downstream(int i, byte d, int cols) => i + Eight[d].dy * cols + Eight[d].dx;

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
