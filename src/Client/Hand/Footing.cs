using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public sealed class Footing<T> where T : class, ITablePiece
{
    public const float Settle = 0.02f;
    const float Sink = 0.03f;

    const double ProbeSpread = 0.51, SlideReach = 0.7, Margin = 1e-3;

    readonly Func<Vec2, double, IEnumerable<T>> _around;
    readonly Func<T, bool> _inHand;
    readonly Func<Ground> _ground;

    readonly record struct Support(T Piece, double X, double Z, double Reach);

    public Footing(Func<Vec2, double, IEnumerable<T>> around, Func<T, bool> inHand, Func<Ground>? ground = null)
    {
        _around = around;
        _inHand = inHand;
        _ground = ground ?? (() => Ground.Flat);
    }

    public float GroundAt(Vec2 p) => (float)_ground().At(p);

    public float GroundUnder(T n, Vec2 pos, double rot) => (float)_ground().Under(pos, rot, n.Footprint.X, n.Footprint.Y);

    public double Stored(T n, Vec2 pos, double rot, double y, HashSet<T>? skip = null) => OnGround(n, pos, rot, skip) ? 0 : y;

    public bool OnGround(T n, Vec2 pos, double rot, HashSet<T>? skip = null)
    {
        float w = n.Footprint.X, d = n.Footprint.Y;
        var rad = rot * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        var under = Supports(pos, ProbeRadius(w, d), n.Layer, n, skip);
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                var q = pos + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                if (PiecesTop(q, under) > GroundAt(q) + Settle) return false;
            }
        return true;
    }

    public double StoredAt(Vec2 p, double y) => y < GroundAt(p) + Settle ? 0 : y;

    public double AbsoluteY(T n, Vec2 pos, double rot, double stored) => stored > 0 ? stored : GroundUnder(n, pos, rot);

    static double ProbeRadius(float w, float d) => ProbeSpread * Math.Sqrt(w * w + d * d);

    List<Support> Supports(Vec2 at, double radius, string layer, T? except, HashSet<T>? skip)
    {
        var found = new List<Support>();
        foreach (var n in _around(at, radius))
        {
            if (n.Layer != layer || n == except || n.Piece.NoCollision) continue;
            double reach = n.SurfaceReach + Margin, dx = n.Piece.Pos.X - at.X, dz = n.Piece.Pos.Z - at.Z, within = reach + radius;
            if (dx * dx + dz * dz > within * within || _inHand(n) || n.IsDie || (skip != null && skip.Contains(n))) continue;
            found.Add(new Support(n, n.Piece.Pos.X, n.Piece.Pos.Z, reach));
        }
        return found;
    }

    static float PiecesTop(Vec2 p, List<Support> under)
    {
        float top = float.MinValue;
        foreach (var u in under)
        {
            double dx = u.X - p.X, dz = u.Z - p.Z;
            if (dx * dx + dz * dz <= u.Reach * u.Reach && u.Piece.SurfaceAt(p) is { } s) top = Mathf.Max(top, s - Sink);
        }
        return top;
    }

    public float SurfaceY(Vec2 p, string layer, T? except = null, HashSet<T>? skip = null) =>
        Mathf.Max(GroundAt(p), PiecesTop(p, Supports(p, 0, layer, except, skip)));

    public float HighestSurface(IReadOnlyList<Vec2> points, string layer)
    {
        if (points.Count == 0) return float.MinValue;
        var centre = new Vec2(points.Average(p => p.X), points.Average(p => p.Z));
        var under = Supports(centre, points.Max(p => (p - centre).Length), layer, null, null);
        return points.Max(p => Mathf.Max(GroundAt(p), PiecesTop(p, under)));
    }

    public (Vec2 pos, double y) Rest(T n, Vec2 pos, double rot, HashSet<T> moving, bool slide = true)
    {
        if (n.Piece.NoCollision) return (pos, GroundUnder(n, pos, rot));
        float w = n.Footprint.X, d = n.Footprint.Y;
        var rad = rot * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        float size = Mathf.Max(w, d);
        var under = Supports(pos, ProbeRadius(w, d) + (slide && !n.IsProp ? size * SlideReach : 0), n.Layer, n, moving);
        (float middle, float all, float middleTop, float allTop) Probe(Vec2 at)
        {
            float mid = 0, all = 0, midTop = float.MinValue, allTop = float.MinValue;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    var q = at + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                    float t = PiecesTop(q, under);
                    float rel = Mathf.Max(0, t - GroundAt(q));
                    all = Mathf.Max(all, rel);
                    allTop = Mathf.Max(allTop, t);
                    if (Math.Abs(i) <= 1 && Math.Abs(j) <= 1) { mid = Mathf.Max(mid, rel); midTop = Mathf.Max(midTop, t); }
                }
            return (mid, all, midTop, allTop);
        }
        double On(Vec2 at, float top) => Math.Max(GroundUnder(n, at, rot), top);
        var (m0, a0, mt0, at0) = Probe(pos);
        if (a0 - m0 <= 0.05f || n.IsProp) return (pos, On(pos, mt0));
        if (!slide) return (pos, On(pos, at0));
        float step = size * 0.06f;
        for (float r = step; r <= size * SlideReach; r += step)
            for (int k = 0; k < 16; k++)
            {
                double a = k * Math.PI / 8;
                var cand = pos + new Vec2(Math.Cos(a) * r, Math.Sin(a) * r);
                var (m, all, mt, _) = Probe(cand);
                if (all - m <= 0.05f && m <= m0 + 0.05f) return (cand, On(cand, mt));
            }
        return (pos, On(pos, at0));
    }

    public List<T> RestingOn(T under, IEnumerable<T> near, Func<string, bool> interactive)
    {
        var found = new List<T>();
        foreach (var n in near)
        {
            if (n == under || n.Layer != under.Layer || _inHand(n) || n.Piece.Locked || n.IsDie || n.Placed) continue;
            if (!interactive(n.Layer) || n.Piece.Y <= 0) continue;
            double underY = AbsoluteY(under, under.Piece.Pos, under.Piece.RotDeg, under.Piece.Y);
            if (n.Piece.Y <= underY + 0.005) continue;
            if (under.SurfaceAt(n.Piece.Pos) is not { } top) continue;
            if (Math.Abs(n.Piece.Y + Sink - top) < 0.07) found.Add(n);
        }
        return found;
    }
}
