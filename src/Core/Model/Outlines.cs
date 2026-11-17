namespace KTable.Core.Model;

public static class Outlines
{
    const int MostCells = 160, MostCorners = 36, CircleSteps = 16, LeastCorners = 3, LegProbes = 8;
    const double CellsToBrush = 5, FirstSlack = 0.8, SlackGrowth = 1.35, OffMiddle = 0.1, StrokeStep = 0.4;

    public sealed record Frame(Spot Centre, double Radius, double Heading, double Size);

    public static List<Spot>? Of(BuiltThing place)
    {
        var given = BuiltGeometry.Points(place.Outline).Select(Spot.Of).ToList();
        return place.Kind == BuiltKinds.Settlement && given.Count >= 2 + LeastCorners ? given.Skip(2).ToList() : null;
    }

    public static List<int> Stored(Frame frame, IReadOnlyList<Spot> ring) =>
        BuiltGeometry.Flat(new[] { frame.Centre, frame.Centre + Spot.Toward(frame.Heading) * frame.Radius }.Concat(ring).Select(p => p.Stored));

    public static List<Spot> Circle(Spot centre, double radius) =>
        Enumerable.Range(0, CircleSteps).Select(i => centre + Spot.Toward(i * Math.Tau / CircleSteps) * radius).ToList();

    public static bool Inside(IReadOnlyList<Spot> ring, Spot p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if (ring[i].Y > p.Y != ring[j].Y > p.Y && p.X < (ring[j].X - ring[i].X) * (p.Y - ring[i].Y) / (ring[j].Y - ring[i].Y) + ring[i].X)
                inside = !inside;
        return inside;
    }

    public static double Depth(IReadOnlyList<Spot> ring, Spot p)
    {
        double least = double.MaxValue;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) least = Math.Min(least, p.FromSegment(ring[j], ring[i]));
        return Inside(ring, p) ? least : -least;
    }

    static double Area(IReadOnlyList<Spot> ring)
    {
        double twice = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) twice += ring[j].X * ring[i].Y - ring[i].X * ring[j].Y;
        return Math.Abs(twice) / 2;
    }

    public static Frame Framed(IReadOnlyList<Spot> ring)
    {
        double left = ring.Min(p => p.X), right = ring.Max(p => p.X), low = ring.Min(p => p.Y), high = ring.Max(p => p.Y);
        const int Probes = 40;
        Spot mean = new(ring.Average(p => p.X), ring.Average(p => p.Y)), centre = mean;
        double deepest = double.MinValue;
        for (int y = 0; y <= Probes; y++)
            for (int x = 0; x <= Probes; x++)
            {
                var p = new Spot(left + (right - left) * x / Probes, low + (high - low) * y / Probes);
                if (Depth(ring, p) is var depth && depth > 0 && depth - (p - mean).Length * OffMiddle is var worth && worth > deepest) (centre, deepest) = (p, worth);
            }
        double xx = ring.Sum(p => (p.X - mean.X) * (p.X - mean.X)), yy = ring.Sum(p => (p.Y - mean.Y) * (p.Y - mean.Y)), xy = ring.Sum(p => (p.X - mean.X) * (p.Y - mean.Y));
        return new Frame(centre, ring.Max(p => (p - centre).Length), 0.5 * Math.Atan2(2 * xy, xx - yy), Math.Sqrt(Area(ring) / Math.PI));
    }

    public static List<Spot>? Around(IReadOnlyList<Spot> stroke, double radius) => Traced(null, stroke, radius, true);

    public static List<Spot>? Grown(IReadOnlyList<Spot> ring, IReadOnlyList<Spot> stroke, double radius) => Traced(ring, stroke, radius, true);

    public static List<Spot>? Shrunk(IReadOnlyList<Spot> ring, IReadOnlyList<Spot> stroke, double radius) => Traced(ring, stroke, radius, false);

    static double FromStroke(IReadOnlyList<Spot> stroke, Spot p)
    {
        double least = (p - stroke[0]).Length;
        for (int i = 1; i < stroke.Count; i++) least = Math.Min(least, p.FromSegment(stroke[i - 1], stroke[i]));
        return least;
    }

    static List<Spot> Thinned(IReadOnlyList<Spot> stroke, double apart)
    {
        var kept = new List<Spot> { stroke[0] };
        for (int i = 1; i < stroke.Count; i++)
            if (i == stroke.Count - 1 || (stroke[i] - kept[^1]).Length >= apart)
                kept.Add(stroke[i]);
        return kept;
    }

    static List<Spot>? Traced(IReadOnlyList<Spot>? ring, IReadOnlyList<Spot> stroke, double radius, bool adds)
    {
        if (stroke.Count == 0 || radius <= 0) return ring?.ToList();
        stroke = Thinned(stroke, radius * StrokeStep);
        var all = (ring ?? []).Concat(stroke).ToList();
        double left = all.Min(p => p.X) - radius, right = all.Max(p => p.X) + radius, low = all.Min(p => p.Y) - radius, high = all.Max(p => p.Y) + radius;
        double cell = Math.Max(Math.Max(right - left, high - low) / MostCells, radius / CellsToBrush / 4);
        int wide = (int)Math.Ceiling((right - left) / cell) + 2, tall = (int)Math.Ceiling((high - low) / cell) + 2;
        var origin = new Spot(left - cell, low - cell);
        var filled = new bool[wide, tall];
        for (int y = 0; y < tall; y++)
            for (int x = 0; x < wide; x++)
            {
                var p = origin + new Spot((x + 0.5) * cell, (y + 0.5) * cell);
                bool brushed = FromStroke(stroke, p) <= radius;
                filled[x, y] = adds ? brushed || ring != null && Inside(ring, p) : !brushed && ring != null && Inside(ring, p);
            }
        if (Largest(filled, wide, tall) is not { } start) return null;
        var corners = Followed(filled, wide, tall, start).Select(v => origin + new Spot(v.X * cell, v.Y * cell)).ToList();
        for (double slack = cell * FirstSlack; ; slack *= SlackGrowth)
            if (Simplified(corners, slack) is { Count: <= MostCorners } few)
                return few.Count >= LeastCorners ? few : null;
    }

    static (int X, int Y)? Largest(bool[,] filled, int wide, int tall)
    {
        var mark = new int[wide, tall];
        int best = 0, bestSize = 0, next = 0;
        var queue = new Queue<(int X, int Y)>();
        for (int y = 0; y < tall; y++)
            for (int x = 0; x < wide; x++)
            {
                if (!filled[x, y] || mark[x, y] != 0) continue;
                int size = 0;
                mark[x, y] = ++next;
                queue.Enqueue((x, y));
                while (queue.Count > 0)
                {
                    var (cx, cy) = queue.Dequeue();
                    size++;
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                        if (nx >= 0 && ny >= 0 && nx < wide && ny < tall && filled[nx, ny] && mark[nx, ny] == 0)
                        {
                            mark[nx, ny] = next;
                            queue.Enqueue((nx, ny));
                        }
                }
                if (size > bestSize) (best, bestSize) = (next, size);
            }
        if (bestSize == 0) return null;
        (int X, int Y)? first = null;
        for (int y = 0; y < tall; y++)
            for (int x = 0; x < wide; x++)
            {
                filled[x, y] = mark[x, y] == best;
                if (filled[x, y]) first ??= (x, y);
            }
        return first;
    }

    static List<(int X, int Y)> Followed(bool[,] filled, int wide, int tall, (int X, int Y) start)
    {
        bool At(int x, int y) => x >= 0 && y >= 0 && x < wide && y < tall && filled[x, y];
        var corners = new List<(int X, int Y)>();
        var seen = new HashSet<(int, int, int, int)>();
        var (x, y) = start;
        int dx = 1, dy = 0;
        while (seen.Add((x, y, dx, dy)))
        {
            int rx = -dy, ry = dx;
            bool ahead = At(x + (dx - rx - 1) / 2, y + (dy - ry - 1) / 2), beside = At(x + (dx + rx - 1) / 2, y + (dy + ry - 1) / 2);
            var (nx, ny) = !beside ? (rx, ry) : ahead ? (dy, -dx) : (dx, dy);
            if ((nx, ny) != (dx, dy)) corners.Add((x, y));
            (dx, dy) = (nx, ny);
            (x, y) = (x + dx, y + dy);
        }
        return corners;
    }

    static List<Spot> Simplified(List<Spot> ring, double slack)
    {
        if (ring.Count <= LeastCorners) return ring;
        int far = Enumerable.Range(0, ring.Count).MaxBy(i => (ring[i] - ring[0]).Length);
        var first = Reduced(ring.Take(far + 1).ToList(), slack);
        var second = Reduced(ring.Skip(far).Append(ring[0]).ToList(), slack);
        return first.Take(first.Count - 1).Concat(second.Take(second.Count - 1)).ToList();
    }

    static List<Spot> Reduced(List<Spot> line, double slack)
    {
        if (line.Count < 3) return line;
        int far = Enumerable.Range(1, line.Count - 2).MaxBy(i => line[i].FromSegment(line[0], line[^1]));
        if (line[far].FromSegment(line[0], line[^1]) <= slack) return [line[0], line[^1]];
        var head = Reduced(line.Take(far + 1).ToList(), slack);
        return head.Take(head.Count - 1).Concat(Reduced(line.Skip(far).ToList(), slack)).ToList();
    }

    static bool Leaves(IReadOnlyList<Spot> ring, Spot a, Spot b) => Enumerable.Range(1, LegProbes - 1).Any(i => !Inside(ring, a + (b - a) * (i / (double)LegProbes)));

    public static void Keep(List<BuiltPart> parts, IReadOnlyList<Spot> ring)
    {
        var kept = new List<BuiltPart>();
        foreach (var part in parts)
        {
            var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
            if (part.Kind != PartKinds.Line)
            {
                if (Inside(ring, BuiltSite.Round(part).At)) kept.Add(part);
                continue;
            }
            var run = new List<Spot>();
            void Close()
            {
                if (run.Count >= 2)
                {
                    var piece = part.Clone();
                    piece.At = BuiltGeometry.Flat(run.Select(p => p.Stored));
                    kept.Add(run.Count == points.Count ? part : piece);
                }
                run = [];
            }
            foreach (var p in points)
            {
                if (run.Count > 0 && Leaves(ring, run[^1], p)) Close();
                if (Inside(ring, p)) run.Add(p);
                else Close();
            }
            Close();
        }
        parts.Clear();
        parts.AddRange(kept);
    }
}
