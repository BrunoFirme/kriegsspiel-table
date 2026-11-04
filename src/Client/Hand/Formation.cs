using KTable.Core.Model;

namespace KTable.Client.Hand;

public static class Formation
{
    public static Vec2 Turned(Vec2 o, double rad) => new(o.X * Math.Cos(rad) + o.Z * Math.Sin(rad), -o.X * Math.Sin(rad) + o.Z * Math.Cos(rad));

    public static void Turn<T>(IEnumerable<HeldPiece<T>> held, double deg) where T : class
    {
        var rad = deg * Math.PI / 180;
        foreach (var h in held)
        {
            h.Offset = Turned(h.Offset, rad);
            h.Rot += deg;
        }
    }

    public static void FormHeld<T>(List<HeldPiece<T>> held, int rows) where T : class, ITablePiece
    {
        var units = held.Where(h => h.Parent == null).ToList();
        var before = units.ToDictionary(h => h, h => (h.Offset, h.Rot, h.Lift));
        var slots = Rows(units.Select(h => (h.Node, h.Offset)).ToList(), units[0].Rot, rows, new Vec2(0, 0));
        for (int i = 0; i < units.Count; i++) { units[i].Offset = slots[i]; units[i].Rot = units[0].Rot; units[i].Lift = 0; }
        Follow(held, before);
    }

    public static void Gather<T>(List<HeldPiece<T>> held, Random rng) where T : class, ITablePiece
    {
        var units = held.Where(h => h.Parent == null).ToList();
        if (units.Count == 0) return;
        var before = units.ToDictionary(h => h, h => (h.Offset, h.Rot, h.Lift));
        double baseRot = units[0].Rot;
        static (double hw, double hd) Half(T n) => (n.Footprint.X * 0.51, n.Footprint.Y * 0.51);
        double cell = 2 * units.Max(h => { var (hw, hd) = Half(h.Node); return Math.Sqrt(hw * hw + hd * hd); });
        var laid = new List<Box>();
        double from = 0;
        var grid = new Dictionary<(long, long), List<Box>>();
        (long, long) Cell(Vec2 at) => ((long)Math.Floor(at.X / cell), (long)Math.Floor(at.Z / cell));
        bool Free(Box b)
        {
            var (cx, cz) = Cell(b.At);
            for (long x = cx - 1; x <= cx + 1; x++)
                for (long z = cz - 1; z <= cz + 1; z++)
                    if (grid.TryGetValue((x, z), out var near) && near.Any(o => o.Overlaps(b))) return false;
            return true;
        }
        foreach (var h in units.Select(h => (h, key: rng.Next())).OrderBy(x => x.key).Select(x => x.h))
        {
            double rot = h.Node.IsDie ? rng.NextDouble() * 360 : baseRot + (rng.NextDouble() * 2 - 1) * 45;
            var (hw, hd) = Half(h.Node);
            double reach = Math.Sqrt(hw * hw + hd * hd);
            Box? best = null;
            for (double radius = from; best == null; radius += reach * 0.5)
                for (int k = 0; k < 40; k++)
                {
                    double a = rng.NextDouble() * 2 * Math.PI, d = radius * Math.Sqrt(rng.NextDouble());
                    var b = new Box(new Vec2(Math.Cos(a) * d, Math.Sin(a) * d), rot, hw, hd);
                    if ((best == null || b.At.Length < best.At.Length) && Free(b)) best = b;
                }
            laid.Add(best);
            from = Math.Max(0, best.At.Length - 2 * reach);
            var key = Cell(best.At);
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new();
            list.Add(best);
            h.Offset = best.At;
            h.Rot = rot;
            h.Lift = 0;
        }
        Follow(held, before);
    }

    sealed record Box(Vec2 At, double Rot, double Hw, double Hd)
    {
        double R => Math.Sqrt(Hw * Hw + Hd * Hd);

        (Vec2 along, Vec2 back) Axes()
        {
            var rad = Rot * Math.PI / 180;
            return (new Vec2(-Math.Cos(rad), Math.Sin(rad)), new Vec2(-Math.Sin(rad), -Math.Cos(rad)));
        }

        public bool Overlaps(Box o)
        {
            var d = o.At - At;
            if (d.Length >= R + o.R) return false;
            var (a1, b1) = Axes();
            var (a2, b2) = o.Axes();
            foreach (var axis in new[] { a1, b1, a2, b2 })
            {
                double Spread(Box b, Vec2 along, Vec2 back) => b.Hw * Math.Abs(Dot(along, axis)) + b.Hd * Math.Abs(Dot(back, axis));
                if (Math.Abs(Dot(d, axis)) >= Spread(this, a1, b1) + Spread(o, a2, b2)) return false;
            }
            return true;
        }

        static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Z * b.Z;
    }

    static void Follow<T>(List<HeldPiece<T>> held, Dictionary<HeldPiece<T>, (Vec2 Offset, double Rot, float Lift)> before) where T : class
    {
        foreach (var h in held.Where(h => h.Parent != null && before.ContainsKey(h.Parent)))
        {
            var (po, pr, pl) = before[h.Parent!];
            var rel = h.Offset - po; var d = h.Parent!.Rot - pr; var rad = d * Math.PI / 180;
            h.Offset = h.Parent.Offset + Turned(rel, rad);
            h.Rot += d;
            h.Lift += h.Parent.Lift - pl;
        }
    }

    public static List<Vec2> Rows<T>(List<(T node, Vec2 offset)> items, double rotDeg, int rows, Vec2 center) where T : ITablePiece
    {
        rows = Math.Clamp(rows, 1, items.Count);
        int cols = (int)Math.Ceiling(items.Count / (double)rows);
        var rad = rotDeg * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        double w = items.Max(i => i.node.Footprint.X) * 1.08, d = items.Max(i => i.node.Footprint.Y) * 1.25;
        var order = items.Select((it, idx) => (idx, a: it.offset.X * along.X + it.offset.Z * along.Z, b: it.offset.X * back.X + it.offset.Z * back.Z))
            .OrderBy(t => Math.Round(t.b / d)).ThenBy(t => t.a).Select(t => t.idx).ToList();
        var result = new Vec2[items.Count];
        for (int k = 0; k < order.Count; k++)
        {
            int r = k / cols, c = k % cols;
            int inRow = Math.Min(cols, items.Count - r * cols);
            double x = (c - (inRow - 1) / 2.0) * w, z = (r - (rows - 1) / 2.0) * d;
            result[order[k]] = center + new Vec2(along.X * x + back.X * z, along.Z * x + back.Z * z);
        }
        return result.ToList();
    }
}
