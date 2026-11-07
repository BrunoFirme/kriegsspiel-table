using KTable.Core.Model;
using SkiaSharp;

namespace KTable.Converter;

public static class TokenOutline
{
    const int LargestSide = 1024;
    const byte Opaque = 255;
    const double Centre = 0.5;
    const int Digits = 5;

    sealed class Vertex(int x, int y)
    {
        public readonly int X = x, Y = y;
    }

    sealed class Edge(Vertex a, Vertex b)
    {
        public Vertex V1 = a, V2 = b;
        public void Switch() => (V1, V2) = (V2, V1);
    }

    public static List<List<Vec2>>? Of(SKBitmap picture, double mergeDistance)
    {
        var (alpha, w, h) = OutlineImage(picture);
        if (w < 1 || h < 1) return null;
        var loops = Loops(Edges(alpha, w, h));
        if (loops.Count == 0) return null;
        var kept = loops.Count > 1 ? loops : loops.Take(1).ToList();
        var outline = new List<List<Vec2>>();
        foreach (var loop in kept)
        {
            var simple = Simplified(loop);
            var merged = Merged(simple, mergeDistance);
            if (merged.Count < 3) continue;
            outline.Add(merged.Select(e => new Vec2(Math.Round((e.V1.X + Centre) / w, Digits), Math.Round(1 - (e.V1.Y + Centre) / h, Digits))).ToList());
        }
        return outline.Count > 0 ? outline : null;
    }

    static (byte[] alpha, int w, int h) OutlineImage(SKBitmap picture)
    {
        int w = picture.Width, h = picture.Height;
        var alpha = new byte[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) alpha[i + w * j] = picture.GetPixel(i, h - 1 - j).Alpha;
        while (w > LargestSide && h > LargestSide)
        {
            int hw = w / 2, hh = h / 2;
            var half = new byte[hw * hh];
            for (int j = 0; j < hh; j++)
                for (int i = 0; i < hw; i++)
                {
                    int sum = alpha[2 * i + w * 2 * j] + alpha[2 * i + 1 + w * 2 * j] + alpha[2 * i + w * (2 * j + 1)] + alpha[2 * i + 1 + w * (2 * j + 1)];
                    half[i + hw * j] = (byte)((sum + 2) / 4);
                }
            (alpha, w, h) = (half, hw, hh);
        }
        return (alpha, w, h);
    }

    static List<Edge> Edges(byte[] alpha, int w, int h)
    {
        var vertices = new Dictionary<(int, int), Vertex>();
        Vertex V(int x, int y) => vertices.TryGetValue((x, y), out var v) ? v : vertices[(x, y)] = new Vertex(x, y);
        bool S(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && alpha[x + w * y] >= Opaque;
        var edges = new List<Edge>();
        for (int i = 0; i < w; i++)
            for (int j = 0; j < h; j++)
            {
                if (!S(i, j)) continue;
                bool up = S(i, j + 1), down = S(i, j - 1), right = S(i + 1, j), left = S(i - 1, j);
                bool upRight = S(i + 1, j + 1), upLeft = S(i - 1, j + 1), downRight = S(i + 1, j - 1);
                if (up && (!upRight && !right ? upLeft || left : !upLeft && !left && (upRight || right))) edges.Add(new Edge(V(i, j), V(i, j + 1)));
                if (upRight && (!up && right || up && !right)) edges.Add(new Edge(V(i, j), V(i + 1, j + 1)));
                if (right && (!up && !upRight ? down || downRight : !down && !downRight && (up || upRight))) edges.Add(new Edge(V(i, j), V(i + 1, j)));
                if (downRight && (!right && down || right && !down)) edges.Add(new Edge(V(i, j), V(i + 1, j - 1)));
            }
        return edges;
    }

    static List<List<Edge>> Loops(List<Edge> edges)
    {
        var loops = new List<List<Edge>>();
        if (edges.Count == 0) return loops;
        var loop = new List<Edge> { edges[0] };
        edges.RemoveAt(0);
        loops.Add(loop);
        while (edges.Count > 0)
        {
            if (Closed(loop))
            {
                loop = new List<Edge> { edges[0] };
                edges.RemoveAt(0);
                loops.Add(loop);
            }
            var added = new HashSet<Edge>();
            foreach (var e in edges) if (Attach(loop, e)) added.Add(e);
            if (added.Count == 0 && edges.Count > 0 && !Closed(loop))
            {
                loop = new List<Edge> { edges[0] };
                edges.RemoveAt(0);
                loops.Add(loop);
                continue;
            }
            edges.RemoveAll(added.Contains);
        }
        return loops;
    }

    static bool Attach(List<Edge> loop, Edge e)
    {
        var end = loop[^1].V2;
        var start = loop[0].V1;
        if (e.V2 == end || (e.V1 != end && e.V1 == start)) e.Switch();
        if (e.V1 == end)
        {
            loop.Add(e);
            return true;
        }
        if (e.V2 != start) return false;
        loop.Insert(0, e);
        return true;
    }

    static bool Closed(List<Edge> loop) => loop.Count >= 2 && loop[^1].V2.X == loop[0].V1.X && loop[^1].V2.Y == loop[0].V1.Y;

    static List<Edge> Simplified(List<Edge> loop)
    {
        var kept = new List<Edge>();
        var current = loop[0];
        for (int i = 1; i < loop.Count; i++)
        {
            var next = loop[i];
            Vertex a = current.V1, c = next.V2, b = current.V2;
            if (b != next.V1 || a == c) return loop;
            if ((long)(b.X - a.X) * (c.Y - a.Y) == (long)(c.X - a.X) * (b.Y - a.Y))
            {
                current.V2 = c;
                continue;
            }
            kept.Add(current);
            current = next;
        }
        kept.Add(current);
        return kept;
    }

    static List<Edge> Merged(List<Edge> loop, double mergeDistance)
    {
        if (mergeDistance < 0) return loop;
        var kept = new List<Edge>();
        var current = loop[0];
        for (int i = 1; i < loop.Count; i++)
        {
            var next = loop[i];
            double dx = current.V1.X - next.V2.X, dy = current.V1.Y - next.V2.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < mergeDistance)
            {
                current.V2 = next.V2;
                continue;
            }
            kept.Add(current);
            current = next;
        }
        kept.Add(current);
        return kept;
    }
}
