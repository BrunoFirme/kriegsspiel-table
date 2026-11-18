namespace KTable.Client.Geometry;

public static class Silhouette
{
    public static bool[]? HoleCover(bool[] solid, int n)
    {
        var closed = Erode(Dilate(solid, n), n).Select((sealedUp, i) => sealedUp || solid[i]).ToArray();
        var outside = new bool[n * n];
        var open = new Queue<int>();
        for (int i = 0; i < n; i++)
            foreach (var edge in new[] { i, (n - 1) * n + i, i * n, i * n + n - 1 })
                if (!closed[edge] && !outside[edge]) { outside[edge] = true; open.Enqueue(edge); }
        while (open.Count > 0)
        {
            int at = open.Dequeue(), x = at % n, y = at / n;
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
                if (nx < 0 || ny < 0 || nx >= n || ny >= n) continue;
                int next = ny * n + nx;
                if (closed[next] || outside[next]) continue;
                outside[next] = true;
                open.Enqueue(next);
            }
        }
        var holes = Enumerable.Range(0, n * n).Where(i => !solid[i] && !outside[i]).ToList();
        if (holes.Count == 0) return null;
        var inside = outside.Select(o => !o).ToArray();
        var cover = Erode(inside, n);
        foreach (var i in holes)
        {
            cover[i] = true;
            foreach (var near in Around(i % n, i / n, n))
                if (inside[near]) cover[near] = true;
        }
        return cover;
    }

    static bool[] Dilate(bool[] solid, int n)
    {
        var grown = new bool[n * n];
        for (int i = 0; i < grown.Length; i++)
            grown[i] = solid[i] || Around(i % n, i / n, n).Any(near => solid[near]);
        return grown;
    }

    static bool[] Erode(bool[] solid, int n)
    {
        var shrunk = new bool[n * n];
        for (int i = 0; i < shrunk.Length; i++)
        {
            int x = i % n, y = i / n;
            shrunk[i] = solid[i] && x > 0 && y > 0 && x < n - 1 && y < n - 1 && Around(x, y, n).All(near => solid[near]);
        }
        return shrunk;
    }

    static IEnumerable<int> Around(int x, int y, int n)
    {
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx != 0 || dy != 0) && nx >= 0 && ny >= 0 && nx < n && ny < n) yield return ny * n + nx;
            }
    }
}
