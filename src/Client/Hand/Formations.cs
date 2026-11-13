using KTable.Core.Model;

namespace KTable.Client.Hand;

public enum FormationDepth { None, Ranks, Abreast }

public enum FormationGroup { Lines, Columns, Shapes }

public sealed record FormationKind(string Id, string Name, string What, FormationGroup Group, FormationDepth Depth, bool TakesFront)
{
    public bool AlongLine { get; init; }
    public bool OneDeep { get; init; }
    public bool HasDistance => !AlongLine && !OneDeep;
    public bool Marches => Group == FormationGroup.Columns;
}

public readonly record struct Slot(double X, double Z, double Turn);

public readonly record struct Pitch(double Across, double Deep);

public static class Formations
{
    public static readonly FormationKind[] All =
    {
        new("line", "Line", "Side by side in as many ranks as you set, the front where you drag: hoplite and legion lines, two- and three-deep firing lines, line abreast", FormationGroup.Lines, FormationDepth.Ranks, true),
        new("stretch", "Stretch in a line", "Strung out evenly along a line you draw with a right-drag, the first piece at its start and the last at its end; draw back over it to shorten it: pickets, outposts, a screen", FormationGroup.Lines, FormationDepth.None, false) { AlongLine = true },
        new("chequer", "Chequer", "Ranks with a piece's width between pieces, each rank covering the gaps of the one in front: quincunx, triplex acies, battalions en échiquier", FormationGroup.Lines, FormationDepth.Ranks, true),
        new("crescent", "Crescent", "A line bowed out towards the front, the centre leading", FormationGroup.Lines, FormationDepth.Ranks, true),
        new("echelon-right", "Echelon right", "A slanting line, the right flank leading, each piece a step behind the one on its right: the oblique order, tanks in echelon", FormationGroup.Lines, FormationDepth.None, false),
        new("echelon-left", "Echelon left", "A slanting line, the left flank leading, each piece a step behind the one on its left: the oblique order, tanks in echelon", FormationGroup.Lines, FormationDepth.None, false),
        new("open", "Open order", "Loose and uneven, well spread out: velites, skirmishers, dispersed infantry", FormationGroup.Lines, FormationDepth.Ranks, true),
        new("column", "Column", "One behind another, as many abreast as you set, the head where the drag ends: column of route, attack column, single file at one abreast, a phalanx or pike block when wide", FormationGroup.Columns, FormationDepth.Abreast, false),
        new("staggered-column", "Staggered column", "One behind another, by turns left and right of the line of march: vehicles or files on both sides of a road", FormationGroup.Columns, FormationDepth.None, false),
        new("herringbone", "Herringbone", "A halted column, by turns angled out to the left and the right: vehicles pulled off the road, covering both flanks", FormationGroup.Columns, FormationDepth.None, false),
        new("square", "Square", "A hollow square, every face looking out: agmen quadratum, infantry square against cavalry", FormationGroup.Shapes, FormationDepth.None, false),
        new("ring", "Ring", "A ring looking out: orbis, schiltron, wagon laager, all-round defence", FormationGroup.Shapes, FormationDepth.None, false) { OneDeep = true },
        new("wedge", "Wedge", "A filled triangle, its point at the front: cuneus, embolon, boar's snout, Panzerkeil", FormationGroup.Shapes, FormationDepth.None, false),
        new("vee", "Vee", "Two arms reaching forward, open in front: to envelop, or to keep the point back", FormationGroup.Shapes, FormationDepth.None, false),
        new("diamond", "Diamond", "Points to the front, the rear and both flanks: the cavalry rhomboid, a platoon's diamond", FormationGroup.Shapes, FormationDepth.None, false),
    };

    public const int Mass = -1;

    static readonly Dictionary<string, (string Kind, int Depth)> Older = new()
    {
        ["two-lines"] = ("line", 2), ["grid"] = ("column", Mass), ["block"] = ("column", Mass), ["arrowhead"] = ("wedge", 0),
    };

    public static IEnumerable<string> Names => All.Select(k => k.Id).Concat(Older.Keys);

    public static FormationKind? Find(string id) => All.FirstOrDefault(k => k.Id == id);

    public static (FormationKind Kind, int Depth)? Named(string name) =>
        Find(name) is { } k ? (k, 0) : Older.TryGetValue(name, out var o) ? (Find(o.Kind)!, o.Depth) : null;

    public const int MostRanks = 12;
    public const double DefaultGap = 0.1, MostGap = 3;
    const double ChequerLines = 1.6, OpenAcross = 2.2, OpenDeep = 1.8, OpenJitter = 0.2, OpenTurn = 10, EchelonStep = 0.5, CrescentArc = 1.9, HerringboneTurn = 45,
        StaggerDeep = 1.5;

    public static Pitch PitchOf(double width, double depth, double interval, double distance) => new(width * (1 + interval), depth + width * distance);

    public static int DefaultDepth(FormationKind kind, int n) => kind.Id switch
    {
        "chequer" => n >= 9 ? 3 : 2,
        "open" => n >= 4 ? 2 : 1,
        _ => 1,
    };

    public static List<Slot> Slots(FormationKind kind, int n, Pitch p, int depth = 0, int? front = null, double span = 0)
    {
        if (n <= 0) return new();
        double a = p.Across, b = p.Deep;
        int d = depth == Mass ? Math.Clamp((int)Math.Round(Math.Sqrt(n * b / a)), 1, n) : Math.Clamp(depth > 0 ? depth : DefaultDepth(kind, n), 1, MostRanks);
        int Wide(int fallback) => Math.Clamp(kind.TakesFront && front is > 0 ? front.Value : fallback, 1, n);
        var s = kind.Id switch
        {
            "line" => Grid(n, Wide(Ceil(n, d)), a, b),
            "stretch" => Stretch(n, a, span),
            "column" => Grid(n, Wide(d), a, b),
            "staggered-column" => Staggered(n, a, b * StaggerDeep, 0),
            "herringbone" => Staggered(n, a, b * StaggerDeep, HerringboneTurn),
            "square" => Square(n, a, b),
            "ring" => Ring(n, a),
            "wedge" => Wedge(n, a, b),
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

    public static double FacingDown(Vec2 from, Vec2 to) => Angles.Norm360(FacingAlong(from, to) + 90);

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

    static List<Slot> Stretch(int n, double a, double span)
    {
        double half = Math.Max(Math.Max(span, (n - 1) * a), AnyLength) / 2;
        return Along(new[] { new Vec2(half, 0), new Vec2(-half, 0) }, n, a).Select(AsSlot).ToList();
    }

    public static Slot AsSlot((Vec2 Pos, double Rot) p) => new(-p.Pos.X, p.Pos.Z, p.Rot);

    public const double Least = 1e-9;
    const double TangentReach = 0.5, AnyLength = 1;

    public static double Length(IReadOnlyList<Vec2> path)
    {
        double len = 0;
        for (int i = 1; i < path.Count; i++) len += (path[i] - path[i - 1]).Length;
        return len;
    }

    public static double FacingOf(IReadOnlyList<Vec2> path)
    {
        var pts = Distinct(path);
        if (pts.Count < 2) return 0;
        return (pts[^1] - pts[0]).Length > Least ? FacingAlong(pts[0], pts[^1]) : FacingAlong(pts[0], pts[1]);
    }

    public static int PastTheEnd(IReadOnlyList<Vec2> path, int n, double spacing, bool keepSpacing = false)
    {
        var pts = Distinct(path);
        if (n < 2 || pts.Count < 2) return 0;
        var (step, len) = Spread(pts, n, spacing);
        if (keepSpacing) step = spacing;
        return Enumerable.Range(0, n).Count(j => j * step - len > Least * (1 + len));
    }

    public static List<(Vec2 Pos, double Rot)> Along(IReadOnlyList<Vec2> path, int n, double spacing, bool keepSpacing = false)
    {
        var pts = Distinct(path);
        if (n <= 0 || pts.Count < 2) return new();
        var cum = new double[pts.Count];
        for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + (pts[i] - pts[i - 1]).Length;
        var (step, len) = Spread(pts, n, spacing);
        if (keepSpacing) step = spacing;
        double reach = Math.Max(spacing * TangentReach, Least);
        Vec2 On(double s)
        {
            if (s <= 0) return pts[0];
            if (s >= len) return pts[^1];
            int i = Array.BinarySearch(cum, s);
            if (i >= 0) return pts[i];
            i = ~i - 1;
            return Lerp(pts[i], pts[i + 1], (s - cum[i]) / (cum[i + 1] - cum[i]));
        }
        var endFrom = On(len - reach);
        var endWay = Unit(pts[^1] - endFrom);
        return Enumerable.Range(0, n).Select(j =>
        {
            double s = n == 1 ? len / 2 : j * step;
            if (s > len) return (pts[^1] + Scaled(endWay, s - len), FacingAlong(endFrom, pts[^1]));
            return (On(s), FacingAlong(On(s - reach), On(s + reach)));
        }).ToList();
    }

    public const double SharpestBend = 60, Backwards = 90;

    public static bool Extend(List<Vec2> path, Vec2 to, double pitch, double every)
    {
        if (path.Count == 0) { path.Add(to); return true; }
        var step = to - path[^1];
        if (step.Length <= Least) return false;
        if (path.Count < 2)
        {
            if (step.Length < every) return false;
            path.Add(to);
            return true;
        }
        if (Math.Abs(Bend(Unit(path[^1] - path[^2]), step)) > Backwards * Math.PI / 180 && Nearest(path, to, step.Length + pitch) is var (cut, at, off) && off <= pitch)
        {
            path.RemoveRange(cut + 1, path.Count - cut - 1);
            if ((at - path[^1]).Length > Least) path.Add(at);
            return true;
        }
        if (step.Length < every) return false;
        double chunk = pitch * SteerChunk, perLength = SharpestBend * Math.PI / 180 / pitch;
        int chunks = (int)Math.Ceiling(step.Length / chunk);
        for (int i = 0; i < chunks; i++)
        {
            var end = path[^1];
            var left = to - end;
            double len = Math.Min(left.Length, chunk);
            if (len <= Least) break;
            var heading = Unit(end - path[^2]);
            double most = perLength * len, bend = Math.Clamp(Bend(heading, left), -most, most);
            var way = new Vec2(heading.X * Math.Cos(bend) - heading.Z * Math.Sin(bend), heading.X * Math.Sin(bend) + heading.Z * Math.Cos(bend));
            path.Add(end + Scaled(way, len));
        }
        return true;
    }

    const double SteerChunk = 0.25;

    static double Bend(Vec2 heading, Vec2 step) => Math.Atan2(heading.X * step.Z - heading.Z * step.X, heading.X * step.X + heading.Z * step.Z);

    static (int Cut, Vec2 At, double Off) Nearest(List<Vec2> path, Vec2 to, double reach)
    {
        double back = 0, best = double.MaxValue;
        int cut = 0;
        Vec2 at = path[^1];
        for (int i = path.Count - 2; i >= 0 && back <= reach; i--)
        {
            var a = path[i];
            var seg = path[i + 1] - a;
            double len2 = seg.X * seg.X + seg.Z * seg.Z;
            double t = len2 > 0 ? Math.Clamp(((to.X - a.X) * seg.X + (to.Z - a.Z) * seg.Z) / len2, 0, 1) : 0;
            var near = Lerp(a, path[i + 1], t);
            double d = (to - near).Length;
            if (d < best)
            {
                best = d;
                cut = i;
                at = near;
            }
            back += Math.Sqrt(len2);
        }
        return (cut, at, best);
    }

    static (double Step, double Len) Spread(List<Vec2> pts, int n, double spacing)
    {
        double len = Length(pts);
        bool closed = (pts[^1] - pts[0]).Length < spacing && len > 2 * spacing;
        int gaps = closed ? n : n - 1;
        return (gaps > 0 ? Math.Max(spacing, len / gaps) : 0, len);
    }

    static List<Vec2> Distinct(IReadOnlyList<Vec2> path)
    {
        var pts = new List<Vec2>(path.Count);
        foreach (var p in path)
            if (pts.Count == 0 || (p - pts[^1]).Length > Least) pts.Add(p);
        return pts;
    }

    static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new(a.X + (b.X - a.X) * t, a.Z + (b.Z - a.Z) * t);

    static Vec2 Scaled(Vec2 v, double k) => new(v.X * k, v.Z * k);

    static Vec2 Unit(Vec2 v) => v.Length > 0 ? Scaled(v, 1 / v.Length) : new Vec2(1, 0);

    static List<Slot> Staggered(int n, double a, double b, double turn) =>
        Enumerable.Range(0, n).Select(i => { int side = i % 2 == 0 ? -1 : 1; return new Slot(side * a / 2, i * b, side * turn); }).ToList();

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
