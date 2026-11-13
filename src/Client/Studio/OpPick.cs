using System.Numerics;
using KTable.Client.Geometry;

namespace KTable.Client.Studio;

public static class OpPick
{
    const string Path = "path", Shape = "shape";

    public static bool Pickable(PaintOp op) => !op.E && op.K switch
    {
        Shape => op.P.Count >= 4,
        Path => op.P.Count == 4 || (op.G != null && op.P.Count >= 4),
        _ => false,
    };

    public static List<Vector2> Points(PaintOp op)
    {
        var pts = new List<Vector2>(op.P.Count / 2);
        for (int i = 0; i + 1 < op.P.Count; i += 2) pts.Add(new Vector2(op.P[i], op.P[i + 1]));
        return pts;
    }

    public static bool Hit(PaintOp op, Vector2 p, float slack)
    {
        var pts = Points(op);
        if (pts.Count == 0) return false;
        bool closed = op.K == Shape;
        if (closed && op.Fc != null && pts.Count >= 3 && Inside(pts, p)) return true;
        float reach = MathF.Max(op.W, 0) / 2 + slack;
        if (pts.Count == 1) return (pts[0] - p).Norm() <= reach;
        int edges = closed && pts.Count > 2 ? pts.Count : pts.Count - 1;
        for (int i = 0; i < edges; i++)
            if ((HandleMath.ClosestOnSegment(p, pts[i], pts[(i + 1) % pts.Count]) - p).Norm() <= reach) return true;
        return false;
    }

    public static bool Inside(IReadOnlyList<Vector2> ring, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Vector2 a = ring[i], b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }
        return inside;
    }

    public static List<int> Mates(IReadOnlyList<PaintOp> ops, int index)
    {
        var g = ops[index].G;
        if (g == null) return new() { index };
        var res = new List<int>();
        for (int i = 0; i < ops.Count; i++) if (ops[i].G == g && Pickable(ops[i])) res.Add(i);
        return res;
    }

    public static bool IsHead(PaintOp op, IReadOnlyList<PaintOp> group) =>
        op.K == Shape && op.W <= 0 && op.Fc != null && op.P.Count == 6 && group.Any(o => o.K == Path);

    public static bool IsLine(IReadOnlyList<PaintOp> group) => group.Count == 1 && group[0].K == Path && group[0].P.Count == 4;

    public static bool IsArrow(IReadOnlyList<PaintOp> group) =>
        group.Count == 2 && group[0].K == Path && group[0].P.Count == 4 && IsHead(group[1], group);

    public static (Vector2 A, Vector2 B)? Ends(IReadOnlyList<PaintOp> group)
    {
        if (IsLine(group)) return (new Vector2(group[0].P[0], group[0].P[1]), new Vector2(group[0].P[2], group[0].P[3]));
        if (IsArrow(group)) return (new Vector2(group[0].P[0], group[0].P[1]), new Vector2(group[1].P[0], group[1].P[1]));
        return null;
    }

    public static void SetEnds(IReadOnlyList<PaintOp> group, Vector2 a, Vector2 b)
    {
        if (IsLine(group)) group[0].P = PaintOp.Flat(new[] { a, b });
        else if (IsArrow(group) && (b - a).Norm() > 1e-3f)
        {
            group[0].P = PaintOp.Flat(new[] { a, ArtShapes.ArrowShaftEnd(a, b, group[0].W) });
            group[1].P = PaintOp.Flat(ArtShapes.ArrowHead(a, b, group[0].W));
        }
    }

    public static List<List<int>> Objects(IReadOnlyList<PaintOp> ops)
    {
        var res = new List<List<int>>();
        var seen = new HashSet<int>();
        for (int i = 0; i < ops.Count; i++)
        {
            if (seen.Contains(i) || !Pickable(ops[i])) continue;
            var mates = Mates(ops, i);
            seen.UnionWith(mates);
            res.Add(mates);
        }
        return res;
    }

    public static int Freehand(IReadOnlyList<PaintOp> ops) => ops.Count(o => !o.E && !Pickable(o));

    public static string Named(IReadOnlyList<PaintOp> group) => ArtShapes.Name(group[0].S ?? (IsArrow(group) ? ArtShapes.Arrow : IsLine(group) ? ArtShapes.Line : null));

    public static string Tint(IReadOnlyList<PaintOp> group)
    {
        var body = group.FirstOrDefault(o => !IsHead(o, group)) ?? group[0];
        return body.K == Shape && body.Fc != null ? body.Fc : body.C;
    }

    public static bool CanRestack(IReadOnlyList<PaintOp> ops, IReadOnlyList<int> at, bool forward) =>
        at.Count > 0 && (forward ? at.Max() < ops.Count - 1 : at.Min() > 0);

    public static List<int>? Restack(List<PaintOp> ops, IReadOnlyList<int> at, bool forward)
    {
        if (!CanRestack(ops, at, forward)) return null;
        var mine = at.OrderBy(i => i).ToList();
        int next = forward ? mine[^1] + 1 : mine[0] - 1;
        var passed = Pickable(ops[next]) ? Mates(ops, next).Except(mine).ToList() : new List<int> { next };
        var mark = ops[forward ? passed.Max() : passed.Min()];
        var moved = mine.Select(i => ops[i]).ToList();
        for (int i = mine.Count - 1; i >= 0; i--) ops.RemoveAt(mine[i]);
        int to = ops.IndexOf(mark) + (forward ? 1 : 0);
        ops.InsertRange(to, moved);
        return Enumerable.Range(to, moved.Count).ToList();
    }
}
