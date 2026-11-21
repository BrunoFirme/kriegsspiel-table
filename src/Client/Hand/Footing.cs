using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public readonly record struct Landed(Vec2 Pos, double Rot, double Top);

public sealed class Footing<T> where T : class, ITablePiece
{
    public const float Settle = 0.02f;
    const float Sink = 0.03f;

    const double ProbeSpread = 0.51, SlideReach = 0.7, Margin = 1e-3;

    readonly Func<Vec2, double, IEnumerable<T>> _around;
    readonly Func<T, bool> _inHand;
    readonly Func<Ground> _ground;

    readonly record struct Support(T Piece, double X, double Z, double Reach, Landed? At = null);

    public Footing(Func<Vec2, double, IEnumerable<T>> around, Func<T, bool> inHand, Func<Ground>? ground = null)
    {
        _around = around;
        _inHand = inHand;
        _ground = ground ?? (() => Ground.Flat);
    }

    public float? Ceiling { get; set; }

    public float GroundAt(Vec2 p) => (float)_ground().DrawnAt(p);

    public float GroundUnder(T n, Vec2 pos, double rot) => (float)_ground().Under(n.MiddleAt(pos, rot), rot, n.Footprint.X, n.Footprint.Y);

    public double Stored(T n, Vec2 pos, double rot, double y, HashSet<T>? skip = null, IReadOnlyDictionary<T, Landed>? landed = null) =>
        Ceiling != null ? y > GroundUnder(n, pos, rot) + Margin ? y : 0 : y <= GroundUnder(n, pos, rot) + Settle || OnGround(n, pos, rot, skip, landed) ? 0 : y;

    public bool OnGround(T n, Vec2 pos, double rot, HashSet<T>? skip = null, IReadOnlyDictionary<T, Landed>? landed = null)
    {
        float w = n.Footprint.X, d = n.Footprint.Y;
        var rad = rot * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        var middle = n.MiddleAt(pos, rot);
        var under = Supports(middle, ProbeRadius(w, d), n.Layer, n, skip, landed);
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                var q = middle + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                if (PiecesTop(q, under) > GroundAt(q) + Settle) return false;
            }
        return true;
    }

    public double StoredAt(Vec2 p, double y) => y < GroundAt(p) + Settle ? 0 : y;

    public double AbsoluteY(T n, Vec2 pos, double rot, double stored) => stored > 0 ? stored : GroundUnder(n, pos, rot);

    static double ProbeRadius(float w, float d) => ProbeSpread * Math.Sqrt(w * w + d * d);

    List<Support> Supports(Vec2 at, double radius, string layer, T? except, HashSet<T>? skip, IReadOnlyDictionary<T, Landed>? landed = null)
    {
        var found = new List<Support>();
        foreach (var n in _around(at, radius))
        {
            if (n.Layer != layer || n == except || n.Piece.PassesThrough || BothFollowGround(except, n)) continue;
            double reach = n.SurfaceReach + Margin, dx = n.Piece.Pos.X - at.X, dz = n.Piece.Pos.Z - at.Z, within = reach + radius;
            if (dx * dx + dz * dz > within * within || _inHand(n) || n.IsDie || (skip != null && skip.Contains(n)) || (landed != null && landed.ContainsKey(n))) continue;
            found.Add(new Support(n, n.Piece.Pos.X, n.Piece.Pos.Z, reach));
        }
        if (landed != null) AddLanded(found, at, radius, layer, except, landed);
        return found;
    }

    static void AddLanded(List<Support> found, Vec2 at, double radius, string layer, T? except, IReadOnlyDictionary<T, Landed> landed)
    {
        foreach (var (n, l) in landed)
        {
            if (n.Layer != layer || n == except || n.Piece.PassesThrough || n.IsDie || BothFollowGround(except, n)) continue;
            double reach = n.SurfaceReach + Margin, dx = l.Pos.X - at.X, dz = l.Pos.Z - at.Z, within = reach + radius;
            if (dx * dx + dz * dz <= within * within) found.Add(new Support(n, l.Pos.X, l.Pos.Z, reach, l));
        }
    }

    static bool BothFollowGround(T? a, T b) => a is { FollowsGround: true } && b.FollowsGround;

    float PiecesTop(Vec2 p, List<Support> under)
    {
        float top = float.MinValue;
        foreach (var u in under)
        {
            double dx = u.X - p.X, dz = u.Z - p.Z;
            if (dx * dx + dz * dz <= u.Reach * u.Reach && TopOf(u, p) is { } s) top = Mathf.Max(top, s);
        }
        return top;
    }

    float? TopOf(Support u, Vec2 p)
    {
        var piece = u.Piece;
        if (u.At is { } l) return piece.ContainsAt(p, l.Pos, l.Rot) && l.Top <= (Ceiling ?? float.MaxValue) ? (float)l.Top : null;
        if (Ceiling is not { } ceiling) return piece.SurfaceAt(p) - Sink;
        if (piece.HasFloors) return piece.FloorAt(p, ceiling);
        return piece.SurfaceAt(p) - Sink is { } s && s <= ceiling ? s : null;
    }

    public float SurfaceY(Vec2 p, string layer, T? except = null, HashSet<T>? skip = null) =>
        Mathf.Max(GroundAt(p), PiecesTop(p, Supports(p, 0, layer, except, skip)));

    const int FiledFrom = 4096;

    public float HighestSurface(IReadOnlyList<Vec2> points, string layer)
    {
        if (points.Count == 0) return float.MinValue;
        var centre = new Vec2(points.Average(p => p.X), points.Average(p => p.Z));
        var under = Supports(centre, points.Max(p => (p - centre).Length), layer, null, null);
        if ((long)points.Count * under.Count < FiledFrom) return points.Max(p => Mathf.Max(GroundAt(p), PiecesTop(p, under)));
        double cell = under.Max(u => u.Reach);
        var filed = new Dictionary<(long, long), List<Support>>();
        foreach (var u in under)
        {
            var at = ((long)Math.Floor(u.X / cell), (long)Math.Floor(u.Z / cell));
            if (!filed.TryGetValue(at, out var there)) filed[at] = there = new();
            there.Add(u);
        }
        float top = float.MinValue;
        var near = new List<Support>();
        foreach (var p in points)
        {
            near.Clear();
            long x = (long)Math.Floor(p.X / cell), z = (long)Math.Floor(p.Z / cell);
            for (long dx = -1; dx <= 1; dx++)
                for (long dz = -1; dz <= 1; dz++)
                    if (filed.TryGetValue((x + dx, z + dz), out var there)) near.AddRange(there);
            top = Mathf.Max(top, Mathf.Max(GroundAt(p), PiecesTop(p, near)));
        }
        return top;
    }

    public (Vec2 pos, double y) Rest(T n, Vec2 pos, double rot, HashSet<T> moving, bool slide = true, IReadOnlyDictionary<T, Landed>? landed = null)
    {
        if (n.Piece.PassesThrough) return (pos, GroundUnder(n, pos, rot));
        float w = n.Footprint.X, d = n.Footprint.Y;
        var rad = rot * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        float size = Mathf.Max(w, d);
        var under = Supports(n.MiddleAt(pos, rot), ProbeRadius(w, d) + (slide && !n.IsProp ? size * SlideReach : 0), n.Layer, n, moving, landed);
        (float middle, float all, float middleTop) Probe(Vec2 at)
        {
            float mid = 0, all = 0, midTop = float.MinValue;
            var from = n.MiddleAt(at, rot);
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    var q = from + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                    float t = PiecesTop(q, under);
                    float rel = Mathf.Max(0, t - GroundAt(q));
                    all = Mathf.Max(all, rel);
                    if (Math.Abs(i) <= 1 && Math.Abs(j) <= 1) { mid = Mathf.Max(mid, rel); midTop = Mathf.Max(midTop, t); }
                }
            return (mid, all, midTop);
        }
        double On(Vec2 at, float top) => Math.Max(GroundUnder(n, at, rot), top);
        var (m0, a0, mt0) = Probe(pos);
        if (a0 - m0 <= 0.05f || n.IsProp || !slide) return (pos, On(pos, mt0));
        float step = size * 0.06f;
        for (float r = step; r <= size * SlideReach; r += step)
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                var cand = pos + new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
                var (m, all, mt) = Probe(cand);
                if (all - m <= 0.05f && m <= m0 + 0.05f) return (cand, On(cand, mt));
            }
        return (pos, On(pos, mt0));
    }

    public List<T> RestingOn(T under, IEnumerable<T> near, Func<string, bool> interactive) =>
        near.Where(n => !_inHand(n) && !n.Placed && interactive(n.Layer) && RestsOn(n, under)).ToList();

    public bool RestsOn(T n, T under)
    {
        if (n == under || n.Layer != under.Layer || n.Piece.Locked || n.IsDie || n.Piece.Y <= 0 || BothFollowGround(n, under)) return false;
        double underY = AbsoluteY(under, under.Piece.Pos, under.Piece.RotDeg, under.Piece.Y);
        if (n.Piece.Y <= underY + 0.005) return false;
        return under.SurfaceAt(n.Piece.Pos) is { } top && Math.Abs(n.Piece.Y + Sink - top) < 0.07;
    }
}
