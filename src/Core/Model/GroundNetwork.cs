namespace KTable.Core.Model;

public sealed record GroundSettlement(byte[] Lines, byte[] Sites, int Farms, int Lanes)
{
    public static GroundSettlement Empty(int cells) => new(new byte[cells * GroundNetwork.Channels], new byte[cells * GroundNetwork.Channels], 0, 0);
}

public static class GroundNetwork
{
    public const int Channels = 4;
    public const byte Track = 1, Lane = 2;
    public const float LineOffsetScale = 32, SiteOffsetScale = 8;
    const double CandidateMetres = 140, Jitter = 0.6, FarmSpacingMetres = 70, ClumpMetres = 900;
    const double RoomMetres = 25, RoadClearMetres = 15, ReachMetres = 900, RouteCellMetres = 13, FarmReachMetres = 45;
    const double SparseShare = 0.1, DenseShare = 1.6, ClumpFrom = 0.35, ClumpTo = 0.8;
    const double SlopeWeight = 8, Steepest = 0.7, WoodFactor = 2.5, MarshFactor = 6, RockFactor = 10, SandFactor = 3, SnowFactor = 4;
    const float LineReachCells = 2.5f, EndReachCells = 0.75f;
    const int SiteReachMost = 15, Smoothings = 3, PullReach = 16;
    const float MarkStep = 0.5f, Meander = 0.35f;
    const float Diagonal = 1.41421356f;
    static readonly (int dx, int dy)[] Eight = [(1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1), (0, -1), (1, -1)];

    readonly record struct Farm(float X, float Y, uint Seed);

    sealed class Path
    {
        public readonly List<(float x, float y)> Points = new();
        public int Joined;
    }

    public static GroundSettlement Plan(int cols, int rows, byte[] kinds, byte[] salt, byte[] detail, float[] heights, double cellMetres, double metresPerHeight)
    {
        int n = cols * rows;
        if (cellMetres <= 0 || kinds.Length != n) return GroundSettlement.Empty(n);
        byte field = GroundKinds.Of("field")!.Value, grass = GroundKinds.Of("grass")!.Value;
        var farmland = new bool[n];
        var road = new bool[n];
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            farmland[i] = kinds[i] == field || kinds[i] == grass;
            road[i] = kinds[i] is GroundKinds.Road or GroundKinds.Bridge;
            any |= farmland[i];
        }
        if (!any) return GroundSettlement.Empty(n);
        var notFarmland = new bool[n];
        for (int i = 0; i < n; i++) notFarmland[i] = !farmland[i];
        var room = Distance(cols, rows, notFarmland);
        var roadAway = Distance(cols, rows, road);
        var farms = PlaceFarms(cols, rows, farmland, salt, detail, room, roadAway, cellMetres);
        farms.Sort((a, b) => roadAway[Cell(cols, rows, a.X, a.Y)].CompareTo(roadAway[Cell(cols, rows, b.X, b.Y)]));
        var paths = Route(cols, rows, kinds, heights, cellMetres, metresPerHeight, farms, out var turns);
        var lines = new byte[n * Channels];
        var sites = new byte[n * Channels];
        DrawLines(cols, rows, paths, lines);
        DrawSites(cols, rows, farms, turns, cellMetres, sites);
        return new GroundSettlement(lines, sites, farms.Count, paths.Count);
    }

    static int Cell(int cols, int rows, float x, float y) => Math.Clamp((int)y, 0, rows - 1) * cols + Math.Clamp((int)x, 0, cols - 1);

    static List<Farm> PlaceFarms(int cols, int rows, bool[] farmland, byte[] salt, byte[] detail, float[] room, float[] roadAway, double cellMetres)
    {
        double pitch = CandidateMetres / cellMetres, spacing = FarmSpacingMetres / cellMetres;
        int gx = (int)Math.Ceiling(cols / pitch), gy = (int)Math.Ceiling(rows / pitch);
        var farms = new List<Farm>();
        var near = new Dictionary<(int, int), List<Farm>>();
        for (int b = 0; b < gy; b++)
            for (int a = 0; a < gx; a++)
            {
                float x = (float)((a + 0.5 + (Unit(Hash(a, b, 11)) - 0.5) * Jitter) * pitch);
                float y = (float)((b + 0.5 + (Unit(Hash(a, b, 23)) - 0.5) * Jitter) * pitch);
                if (x < 0 || y < 0 || x >= cols || y >= rows) continue;
                int i = Cell(cols, rows, x, y);
                if (!farmland[i] || room[i] * cellMetres < RoomMetres || roadAway[i] * cellMetres < RoadClearMetres) continue;
                double buildings = GroundKinds.LevelsOf(detail[i]).Second / 100.0;
                if (buildings <= 0) continue;
                double clump = Noise(x * cellMetres / ClumpMetres + 3.3, y * cellMetres / ClumpMetres + 3.3);
                double share = buildings * Lerp(SparseShare, DenseShare, Smooth(ClumpFrom, ClumpTo, clump));
                uint seed = Hash(a, b, salt[i] + 37);
                if (Unit(seed) >= share) continue;
                if (Crowded(near, a, b, x, y, spacing)) continue;
                var f = new Farm(x, y, seed);
                farms.Add(f);
                if (!near.TryGetValue((a, b), out var list)) near[(a, b)] = list = new List<Farm>();
                list.Add(f);
            }
        return farms;
    }

    static bool Crowded(Dictionary<(int, int), List<Farm>> near, int a, int b, float x, float y, double spacing)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
                if (near.TryGetValue((a + dx, b + dy), out var list))
                    foreach (var f in list)
                        if ((f.X - x) * (f.X - x) + (f.Y - y) * (f.Y - y) < spacing * spacing) return true;
        return false;
    }

    static List<Path> Route(int cols, int rows, byte[] kinds, float[] heights, double cellMetres, double metresPerHeight, List<Farm> farms, out float[] turns)
    {
        turns = new float[farms.Count];
        int k = Math.Max(1, (int)Math.Round(RouteCellMetres / cellMetres));
        int cw = (cols + k - 1) / k, ch = (rows + k - 1) / k, cn = cw * ch;
        var factor = new float[cn];
        var height = new float[cn];
        var network = new bool[cn];
        var owner = new int[cn];
        var farmAt = new int[cn];
        Array.Fill(owner, -1);
        Array.Fill(farmAt, -1);
        Coarse(cols, rows, kinds, heights, k, cw, ch, factor, height, network);
        var paths = new List<Path>();
        var dist = new float[cn];
        var parent = new int[cn];
        var stamp = new int[cn];
        int round = 0;
        double step = k * cellMetres;
        var queue = new PriorityQueue<int, float>();
        for (int f = 0; f < farms.Count; f++)
        {
            var farm = farms[f];
            turns[f] = (float)(Unit(farm.Seed ^ 0x9e3779b9u) * Math.PI * 2);
            int sx = Math.Clamp((int)(farm.X / k), 0, cw - 1), sy = Math.Clamp((int)(farm.Y / k), 0, ch - 1), start = sy * cw + sx;
            round++;
            queue.Clear();
            dist[start] = 0;
            parent[start] = -1;
            stamp[start] = round;
            queue.Enqueue(start, 0);
            int found = -1;
            while (queue.TryDequeue(out int c, out float d))
            {
                if (d > dist[c]) continue;
                if (network[c] && c != start) { found = c; break; }
                int cx = c % cw, cy = c / cw;
                for (int e = 0; e < 8; e++)
                {
                    int nx = cx + Eight[e].dx, ny = cy + Eight[e].dy;
                    if (nx < 0 || ny < 0 || nx >= cw || ny >= ch) continue;
                    int j = ny * cw + nx;
                    if (float.IsPositiveInfinity(factor[j])) continue;
                    float run = (e % 2 == 1 ? Diagonal : 1) * (float)step;
                    float slope = (float)(Math.Abs(height[j] - height[c]) * metresPerHeight / run);
                    if (slope > Steepest) continue;
                    float nd = d + run * (1 + (float)SlopeWeight * slope * slope) * factor[j];
                    if (nd > ReachMetres) continue;
                    if (stamp[j] == round && nd >= dist[j]) continue;
                    stamp[j] = round;
                    dist[j] = nd;
                    parent[j] = c;
                    queue.Enqueue(j, nd);
                }
            }
            network[start] = true;
            if (farmAt[start] < 0) farmAt[start] = f;
            if (found < 0) continue;
            var trail = new List<int> { found };
            for (int c = parent[found]; c >= 0; c = parent[c]) trail.Add(c);
            trail.Reverse();
            var pulled = Pulled(trail, cw, factor, height, metresPerHeight, step);
            var path = new Path();
            path.Points.Add((farm.X, farm.Y));
            for (int i = 1; i + 1 < pulled.Count; i++)
            {
                var (px, py) = Centre(pulled[i], cw, k, cols, rows);
                uint h = Hash(pulled[i] % cw, pulled[i] / cw, 71);
                path.Points.Add((px + (float)((Unit(h) - 0.5) * 2 * Meander * k), py + (float)((Unit(h >> 7) - 0.5) * 2 * Meander * k)));
            }
            int joined = owner[found];
            path.Points.Add(farmAt[found] >= 0 && farmAt[found] != f ? (farms[farmAt[found]].X, farms[farmAt[found]].Y)
                : joined >= 0 ? Nearest(paths[joined].Points, Centre(found, cw, k, cols, rows)) : Centre(found, cw, k, cols, rows));
            if (joined >= 0) paths[joined].Joined++;
            Smooth(path.Points);
            var next = path.Points[1];
            turns[f] = (float)Math.Atan2(next.y - farm.Y, next.x - farm.X);
            Mark(path.Points, paths.Count, k, cw, ch, network, owner);
            paths.Add(path);
        }
        return paths;
    }

    static List<int> Pulled(List<int> trail, int cw, float[] factor, float[] height, double metresPerHeight, double step)
    {
        var o = new List<int> { trail[0] };
        int i = 0;
        while (i < trail.Count - 1)
        {
            int j = Math.Min(trail.Count - 1, i + PullReach);
            while (j > i + 1 && !Clear(trail[i], trail[j], cw, factor, height, metresPerHeight, step, Worst(trail, i, j, factor))) j--;
            o.Add(trail[j]);
            i = j;
        }
        return o;
    }

    static float Worst(List<int> trail, int i, int j, float[] factor)
    {
        float w = 0;
        for (int m = i; m <= j; m++) w = Math.Max(w, factor[trail[m]]);
        return w;
    }

    static bool Clear(int a, int b, int cw, float[] factor, float[] height, double metresPerHeight, double step, float worst)
    {
        int ax = a % cw, ay = a / cw, bx = b % cw, by = b / cw;
        int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(by - ay)) * 2.0));
        int prev = a;
        for (int s = 1; s <= steps; s++)
        {
            int x = (int)Math.Round(ax + (bx - ax) * s / (double)steps), y = (int)Math.Round(ay + (by - ay) * s / (double)steps);
            int c = y * cw + x;
            if (c == prev) continue;
            if (factor[c] > worst) return false;
            double run = Math.Sqrt((x - prev % cw) * (x - prev % cw) + (y - prev / cw) * (y - prev / cw)) * step;
            if (Math.Abs(height[c] - height[prev]) * metresPerHeight / run > Steepest) return false;
            prev = c;
        }
        return true;
    }

    static (float x, float y) Nearest(List<(float x, float y)> pts, (float x, float y) at)
    {
        var best = pts[0];
        float bd = float.MaxValue;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, ay) = pts[i];
            var (bx, by) = pts[i + 1];
            float dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            float t = len2 < 1e-8f ? 0 : Math.Clamp(((at.x - ax) * dx + (at.y - ay) * dy) / len2, 0, 1);
            float px = ax + dx * t, py = ay + dy * t, d = (px - at.x) * (px - at.x) + (py - at.y) * (py - at.y);
            if (d < bd) { bd = d; best = (px, py); }
        }
        return best;
    }

    static void Mark(List<(float x, float y)> pts, int id, int k, int cw, int ch, bool[] network, int[] owner)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            var (ax, ay) = pts[i];
            var (bx, by) = pts[i + 1];
            float len = MathF.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay)) / k;
            int steps = Math.Max(1, (int)Math.Ceiling(len / MarkStep));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)steps;
                int cx = Math.Clamp((int)((ax + (bx - ax) * t) / k), 0, cw - 1), cy = Math.Clamp((int)((ay + (by - ay) * t) / k), 0, ch - 1);
                int c = cy * cw + cx;
                network[c] = true;
                if (owner[c] < 0) owner[c] = id;
            }
        }
    }

    static (float x, float y) Centre(int c, int cw, int k, int cols, int rows) =>
        (Math.Min(cols - 0.5f, (c % cw + 0.5f) * k), Math.Min(rows - 0.5f, (c / cw + 0.5f) * k));

    static void Coarse(int cols, int rows, byte[] kinds, float[] heights, int k, int cw, int ch, float[] factor, float[] height, bool[] network)
    {
        byte wood = GroundKinds.Of("wood")!.Value, marsh = GroundKinds.Of("marsh")!.Value, rock = GroundKinds.Of("rock")!.Value;
        byte sand = GroundKinds.Of("sand")!.Value, snow = GroundKinds.Of("snow")!.Value;
        for (int cy = 0; cy < ch; cy++)
            for (int cx = 0; cx < cw; cx++)
            {
                int c = cy * cw + cx;
                bool water = false, net = false;
                for (int y = cy * k; y < Math.Min(rows, cy * k + k); y++)
                    for (int x = cx * k; x < Math.Min(cols, cx * k + k); x++)
                    {
                        byte kind = kinds[y * cols + x];
                        water |= kind == GroundKinds.Water;
                        net |= kind is GroundKinds.Road or GroundKinds.Bridge or GroundKinds.Town;
                    }
                int centre = Math.Min(rows - 1, cy * k + k / 2) * cols + Math.Min(cols - 1, cx * k + k / 2);
                byte at = kinds[centre];
                network[c] = net;
                height[c] = heights[centre];
                factor[c] = net ? 1 : water ? float.PositiveInfinity : at == wood ? (float)WoodFactor : at == marsh ? (float)MarshFactor
                    : at == rock ? (float)RockFactor : at == sand ? (float)SandFactor : at == snow ? (float)SnowFactor : 1;
            }
    }

    static void Smooth(List<(float x, float y)> pts)
    {
        for (int pass = 0; pass < Smoothings && pts.Count > 2; pass++)
        {
            var o = new List<(float x, float y)> { pts[0] };
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var (ax, ay) = pts[i];
                var (bx, by) = pts[i + 1];
                if (i > 0) o.Add((ax * 0.75f + bx * 0.25f, ay * 0.75f + by * 0.25f));
                if (i + 2 < pts.Count) o.Add((ax * 0.25f + bx * 0.75f, ay * 0.25f + by * 0.75f));
            }
            o.Add(pts[^1]);
            pts.Clear();
            pts.AddRange(o);
        }
    }

    static void DrawLines(int cols, int rows, List<Path> paths, byte[] lines)
    {
        var best = new float[cols * rows];
        Array.Fill(best, float.MaxValue);
        foreach (var path in paths)
        {
            byte kind = path.Joined > 0 ? Lane : Track;
            var pts = path.Points;
            for (int s = 0; s + 1 < pts.Count; s++)
            {
                var (ax, ay) = pts[s];
                var (bx, by) = pts[s + 1];
                float dx = bx - ax, dy = by - ay, len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1e-4f) continue;
                int angle = (int)Math.Round(((Math.Atan2(dy, dx) % Math.PI) + Math.PI) % Math.PI / Math.PI * 255) % 256;
                double a = angle / 255.0 * Math.PI;
                float nx = (float)-Math.Sin(a), ny = (float)Math.Cos(a);
                int x0 = Math.Max(0, (int)(Math.Min(ax, bx) - LineReachCells)), x1 = Math.Min(cols - 1, (int)(Math.Max(ax, bx) + LineReachCells));
                int y0 = Math.Max(0, (int)(Math.Min(ay, by) - LineReachCells)), y1 = Math.Min(rows - 1, (int)(Math.Max(ay, by) + LineReachCells));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float qx = x + 0.5f, qy = y + 0.5f;
                        float t = ((qx - ax) * dx + (qy - ay) * dy) / (len * len);
                        bool beforeFarm = s == 0 && t < 0;
                        float tc = Math.Clamp(t, 0, 1);
                        float px = ax + dx * tc - qx, py = ay + dy * tc - qy;
                        float d = MathF.Sqrt(px * px + py * py);
                        if (d > (beforeFarm ? EndReachCells : LineReachCells)) continue;
                        int i = y * cols + x;
                        if (d >= best[i]) continue;
                        best[i] = d;
                        float off = (ax - qx) * nx + (ay - qy) * ny;
                        lines[i * Channels] = (byte)angle;
                        lines[i * Channels + 1] = (byte)Math.Clamp((int)Math.Round(128 + off * LineOffsetScale), 1, 255);
                        lines[i * Channels + 2] = kind;
                        lines[i * Channels + 3] = 255;
                    }
            }
        }
    }

    static void DrawSites(int cols, int rows, List<Farm> farms, float[] turns, double cellMetres, byte[] sites)
    {
        int reach = Math.Min(SiteReachMost, (int)Math.Ceiling(FarmReachMetres / cellMetres));
        float most = 127f / SiteOffsetScale;
        var best = new float[cols * rows];
        Array.Fill(best, float.MaxValue);
        for (int f = 0; f < farms.Count; f++)
        {
            var farm = farms[f];
            int fx = (int)farm.X, fy = (int)farm.Y;
            byte turn = (byte)(1 + (int)Math.Round(((turns[f] % (Math.PI * 2)) + Math.PI * 2) % (Math.PI * 2) / (Math.PI * 2) * 253));
            for (int y = Math.Max(0, fy - reach); y <= Math.Min(rows - 1, fy + reach); y++)
                for (int x = Math.Max(0, fx - reach); x <= Math.Min(cols - 1, fx + reach); x++)
                {
                    float ox = farm.X - (x + 0.5f), oy = farm.Y - (y + 0.5f);
                    float d = ox * ox + oy * oy;
                    int i = y * cols + x;
                    if (Math.Abs(ox) > most || Math.Abs(oy) > most || d >= best[i]) continue;
                    best[i] = d;
                    sites[i * Channels] = (byte)Math.Clamp((int)Math.Round(128 + ox * SiteOffsetScale), 0, 255);
                    sites[i * Channels + 1] = (byte)Math.Clamp((int)Math.Round(128 + oy * SiteOffsetScale), 0, 255);
                    sites[i * Channels + 2] = (byte)(farm.Seed >> 8);
                    sites[i * Channels + 3] = turn;
                }
        }
    }

    public static float[] Distance(int w, int h, bool[] seeds)
    {
        var d = new float[w * h];
        if (!Array.Exists(seeds, s => s)) { Array.Fill(d, float.MaxValue / 4); return d; }
        for (int i = 0; i < d.Length; i++) d[i] = seeds[i] ? 0 : float.MaxValue / 4;
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
            }
        return d;
    }

    static uint Hash(int a, int b, int c)
    {
        uint h = (uint)a * 0x8da6b343u ^ (uint)b * 0xd8163841u ^ (uint)c * 0xcb1ab31fu;
        h ^= h >> 13;
        h *= 0x5bd1e995u;
        return h ^ (h >> 15);
    }

    static double Unit(uint h) => (h & 0xffffff) / (double)0x1000000;

    static double Noise(double x, double y)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        double fx = x - ix, fy = y - iy;
        double ux = fx * fx * (3 - 2 * fx), uy = fy * fy * (3 - 2 * fy);
        double a = Unit(Hash(ix, iy, 5)), b = Unit(Hash(ix + 1, iy, 5)), c = Unit(Hash(ix, iy + 1, 5)), d = Unit(Hash(ix + 1, iy + 1, 5));
        return Lerp(Lerp(a, b, ux), Lerp(c, d, ux), uy);
    }

    static double Lerp(double a, double b, double t) => a + (b - a) * t;

    static double Smooth(double from, double to, double x)
    {
        double t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }
}
