using KTable.Core.Model;

namespace KTable.Client.Hand;

public enum FormationDepth { None, Ranks, Abreast }

public sealed record FormationKind(string Id, string Name, string What, FormationDepth Depth, bool TakesFront);

public readonly record struct Slot(double X, double Z, double Turn);

public readonly record struct Pitch(double Across, double Deep);

public static class Formations
{
    public static readonly FormationKind[] All =
    {
        new("line", "Line", "Side by side, facing the front, in as many ranks as you set", FormationDepth.Ranks, true),
        new("column", "Column", "One behind another, as many abreast as you set: a column of march or of attack", FormationDepth.Abreast, true),
        new("block", "Block", "A dense mass about as deep as it is wide: phalanx, pike block, tercio, testudo", FormationDepth.None, true),
        new("square", "Square", "A hollow square, every face looking out", FormationDepth.None, false),
        new("ring", "Ring", "A ring looking out: orbis, schiltron, laager", FormationDepth.None, false),
        new("wedge", "Wedge", "A filled triangle, its point at the front: cuneus, boar's snout, Panzerkeil", FormationDepth.None, false),
        new("arrowhead", "Arrowhead", "Two arms meeting in a point at the front, open behind", FormationDepth.None, false),
        new("vee", "Vee", "Two arms reaching forward, open in front", FormationDepth.None, false),
        new("echelon-right", "Echelon right", "A slanting line, the right flank leading, each piece a step behind the one on its right", FormationDepth.None, false),
        new("echelon-left", "Echelon left", "A slanting line, the left flank leading, each piece a step behind the one on its left", FormationDepth.None, false),
        new("diamond", "Diamond", "Points to the front, the rear and both flanks", FormationDepth.None, false),
        new("chequer", "Chequer", "Ranks with a piece's width between pieces, each rank covering the gaps of the one in front: quincunx, triplex acies", FormationDepth.Ranks, true),
        new("crescent", "Crescent", "A line bowed out towards the front", FormationDepth.Ranks, true),
        new("open", "Open order", "Loose and uneven, well spread out: skirmishers, dispersed troops", FormationDepth.Ranks, true),
    };

    static readonly Dictionary<string, (string Kind, int Depth)> Older = new() { ["two-lines"] = ("line", 2), ["grid"] = ("block", 0) };

    public static IEnumerable<string> Names => All.Select(k => k.Id).Concat(Older.Keys);

    public static FormationKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);

    public static (FormationKind Kind, int Depth)? Named(string name) =>
        Find(name) is { } k ? (k, 0) : Older.TryGetValue(name, out var o) ? (Find(o.Kind)!, o.Depth) : null;

    public const int MostRanks = 12;
    public const double DefaultGap = 0.1, MostGap = 3;
    const double ChequerLines = 1.6, OpenAcross = 2.2, OpenDeep = 1.8, OpenJitter = 0.2, OpenTurn = 10, EchelonStep = 0.5, CrescentArc = 1.9;

    public static int DefaultDepth(FormationKind kind, int n) => kind.Id switch
    {
        "chequer" => n >= 9 ? 3 : 2,
        "open" => n >= 4 ? 2 : 1,
        _ => 1,
    };

    public static List<Slot> Slots(FormationKind kind, int n, Pitch p, int depth = 0, int? front = null)
    {
        if (n <= 0) return new();
        int d = Math.Clamp(depth > 0 ? depth : DefaultDepth(kind, n), 1, MostRanks);
        int Wide(int fallback) => Math.Clamp(kind.TakesFront && front is > 0 ? front.Value : fallback, 1, n);
        double a = p.Across, b = p.Deep;
        var s = kind.Id switch
        {
            "line" => Grid(n, Wide(Ceil(n, d)), a, b),
            "column" => Grid(n, Wide(d), a, b),
            "block" => Grid(n, Wide(Math.Max(1, (int)Math.Round(Math.Sqrt(n * b / a)))), a, b),
            "square" => Square(n, a, b),
            "ring" => Ring(n, a),
            "wedge" => Wedge(n, a, b),
            "arrowhead" => Arms(n, a, Math.Max(b, 0.7 * a)),
            "vee" => Arms(n, a, -Math.Max(b, 0.7 * a)),
            "echelon-right" => Enumerable.Range(0, n).Select(j => new Slot((j - (n - 1) / 2.0) * a, (n - 1 - j) * b * EchelonStep, 0)).ToList(),
            "echelon-left" => Enumerable.Range(0, n).Select(j => new Slot((j - (n - 1) / 2.0) * a, j * b * EchelonStep, 0)).ToList(),
            "diamond" => Diamond(n, a, b),
            "chequer" => Chequer(n, Wide(Ceil(n, d)), a, b),
            "crescent" => Crescent(n, Wide(Ceil(n, d)), a, b),
            "open" => Open(n, Wide(Ceil(n, d)), a, b),
            _ => Grid(n, n, a, b),
        };
        return Centred(s);
    }

    public static List<(Vec2 Pos, double Rot)> Place(List<Slot> slots, Vec2 at, double facing, bool frontAt = false)
    {
        double lift = frontAt && slots.Count > 0 ? -slots.Min(q => q.Z) : 0;
        var (right, back) = Axes(facing);
        return slots.Select(q => (new Vec2(at.X + right.X * q.X + back.X * (q.Z + lift), at.Z + right.Z * q.X + back.Z * (q.Z + lift)), Angles.Norm360(facing + q.Turn))).ToList();
    }

    public static (Vec2 Right, Vec2 Back) Axes(double facing)
    {
        double f = facing * Math.PI / 180;
        return (new Vec2(-Math.Cos(f), Math.Sin(f)), new Vec2(Math.Sin(f), Math.Cos(f)));
    }

    public static double FacingAlong(Vec2 from, Vec2 to)
    {
        var d = to - from;
        return Angles.Norm360(Math.Atan2(d.Z, -d.X) * 180 / Math.PI);
    }

    public static double? Facing(IEnumerable<double> rots)
    {
        double x = 0, z = 0;
        int n = 0;
        foreach (var r in rots)
        {
            x += Math.Cos(r * Math.PI / 180);
            z += Math.Sin(r * Math.PI / 180);
            n++;
        }
        if (n == 0 || Math.Sqrt(x * x + z * z) < 0.3 * n) return null;
        return Angles.Norm360(Math.Atan2(z, x) * 180 / Math.PI);
    }

    public const int MostMatched = 400;

    public static int[] Match(IReadOnlyList<Vec2> from, IReadOnlyList<Vec2> to)
    {
        int n = Math.Min(from.Count, to.Count);
        if (n == 0) return Array.Empty<int>();
        if (n > MostMatched) return InOrder(from, to, n);
        var cost = new double[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++) { var d = from[i] - to[j]; cost[i, j] = d.X * d.X + d.Z * d.Z; }
        return Hungarian(cost, n);
    }

    static int[] InOrder(IReadOnlyList<Vec2> from, IReadOnlyList<Vec2> to, int n)
    {
        static int[] Order(IReadOnlyList<Vec2> pts, int n) => Enumerable.Range(0, n).OrderBy(i => Math.Round(pts[i].Z, 3)).ThenBy(i => pts[i].X).ToArray();
        var a = Order(from, n);
        var b = Order(to, n);
        var r = new int[n];
        for (int k = 0; k < n; k++) r[a[k]] = b[k];
        return r;
    }

    static int[] Hungarian(double[,] a, int n)
    {
        var u = new double[n + 1];
        var v = new double[n + 1];
        var p = new int[n + 1];
        var way = new int[n + 1];
        var minv = new double[n + 1];
        var used = new bool[n + 1];
        for (int i = 1; i <= n; i++)
        {
            p[0] = i;
            int j0 = 0;
            Array.Fill(minv, double.PositiveInfinity);
            Array.Fill(used, false);
            do
            {
                used[j0] = true;
                int i0 = p[j0], j1 = 0;
                double delta = double.PositiveInfinity;
                for (int j = 1; j <= n; j++)
                {
                    if (used[j]) continue;
                    double cur = a[i0 - 1, j - 1] - u[i0] - v[j];
                    if (cur < minv[j]) { minv[j] = cur; way[j] = j0; }
                    if (minv[j] < delta) { delta = minv[j]; j1 = j; }
                }
                for (int j = 0; j <= n; j++)
                    if (used[j]) { u[p[j]] += delta; v[j] -= delta; }
                    else minv[j] -= delta;
                j0 = j1;
            } while (p[j0] != 0);
            do
            {
                int j1 = way[j0];
                p[j0] = p[j1];
                j0 = j1;
            } while (j0 != 0);
        }
        var result = new int[n];
        for (int j = 1; j <= n; j++) result[p[j] - 1] = j - 1;
        return result;
    }

    static int Ceil(int n, int d) => (n + d - 1) / d;

    static List<Slot> Centred(List<Slot> s)
    {
        double cx = s.Average(q => q.X), cz = s.Average(q => q.Z);
        return s.Select(q => q with { X = q.X - cx, Z = q.Z - cz }).ToList();
    }

    static List<Slot> Grid(int n, int front, double a, double b)
    {
        var s = new List<Slot>(n);
        for (int r = 0; r * front < n; r++)
        {
            int m = Math.Min(front, n - r * front);
            for (int j = 0; j < m; j++) s.Add(new((j - (m - 1) / 2.0) * a, r * b, 0));
        }
        return s;
    }

    static List<Slot> Square(int n, double a, double b)
    {
        int per = Ceil(n, 4);
        double half = per * a / 2 + b / 2;
        var sides = new[] { (0, n / 4 + (n % 4 > 0 ? 1 : 0)), (2, n / 4 + (n % 4 > 1 ? 1 : 0)), (1, n / 4 + (n % 4 > 2 ? 1 : 0)), (3, n / 4) };
        var s = new List<Slot>(n);
        foreach (var (side, m) in sides)
            for (int j = 0; j < m; j++)
            {
                double along = (j - (m - 1) / 2.0) * a;
                s.Add(side switch
                {
                    0 => new Slot(along, -half, 0),
                    1 => new Slot(half, along, 90),
                    2 => new Slot(-along, half, 180),
                    _ => new Slot(-half, -along, 270),
                });
            }
        return s;
    }

    static List<Slot> Ring(int n, double a)
    {
        double r = n <= 1 ? 0 : Math.Max(a / (2 * Math.Sin(Math.PI / n)), a * 0.6);
        return Enumerable.Range(0, n).Select(i =>
        {
            double phi = 2 * Math.PI * i / n;
            return new Slot(r * Math.Sin(phi), -r * Math.Cos(phi), phi * 180 / Math.PI);
        }).ToList();
    }

    static List<Slot> Wedge(int n, double a, double b)
    {
        var s = new List<Slot>(n);
        for (int k = 0; s.Count < n; k++)
        {
            int m = Math.Min(k + 1, n - s.Count);
            for (int j = 0; j < m; j++) s.Add(new((j - (m - 1) / 2.0) * a, k * b, 0));
        }
        return s;
    }

    static List<Slot> Arms(int n, double a, double step) =>
        Enumerable.Range(0, n).Select(i =>
        {
            int rank = (i + 1) / 2, side = i == 0 ? 0 : i % 2 == 1 ? -1 : 1;
            return new Slot(side * rank * a, rank * step, 0);
        }).ToList();

    static List<Slot> Diamond(int n, double a, double b)
    {
        int widest = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(n)));
        var widths = Enumerable.Range(1, widest).Concat(Enumerable.Range(1, widest - 1).Reverse());
        var s = new List<Slot>(n);
        int row = 0;
        foreach (var w in widths)
        {
            int m = Math.Min(w, n - s.Count);
            if (m <= 0) break;
            for (int j = 0; j < m; j++) s.Add(new((j - (m - 1) / 2.0) * a, row * b, 0));
            row++;
        }
        return s;
    }

    static List<Slot> Chequer(int n, int front, double a, double b)
    {
        var s = new List<Slot>(n);
        double shift = (2 * (front - 1) + 1) * a / 2;
        for (int r = 0; r * front < n; r++)
        {
            int m = Math.Min(front, n - r * front);
            for (int j = 0; j < m; j++) s.Add(new((2 * j + r % 2) * a - shift, r * b * ChequerLines, 0));
        }
        return s;
    }

    static List<Slot> Crescent(int n, int front, double a, double b)
    {
        double arc = front <= 2 ? 0.6 : CrescentArc;
        double radius = Math.Max(front * a / arc, a);
        var s = new List<Slot>(n);
        for (int r = 0; r * front < n; r++)
        {
            int m = Math.Min(front, n - r * front);
            double rr = Math.Max(radius - r * b, a * 0.5);
            double step = 2 * Math.Asin(Math.Min(1, a / (2 * rr)));
            for (int j = 0; j < m; j++)
            {
                double phi = (j - (m - 1) / 2.0) * step;
                s.Add(new(rr * Math.Sin(phi), radius - rr * Math.Cos(phi), phi * 180 / Math.PI));
            }
        }
        return s;
    }

    static List<Slot> Open(int n, int front, double a, double b)
    {
        var rng = new Random(n * 7919 + front);
        double ax = a * OpenAcross, bz = b * OpenDeep;
        double Jitter(double size) => (rng.NextDouble() * 2 - 1) * OpenJitter * size;
        var s = new List<Slot>(n);
        for (int r = 0; r * front < n; r++)
        {
            int m = Math.Min(front, n - r * front);
            for (int j = 0; j < m; j++)
                s.Add(new((j - (m - 1) / 2.0 + (r % 2) * 0.5) * ax + Jitter(ax), r * bz + Jitter(bz), (rng.NextDouble() * 2 - 1) * OpenTurn));
        }
        return s;
    }
}
