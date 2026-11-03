using KTable.Client.Geometry;
using KTable.Core.Model;

namespace KTable.Client.Hand;

public sealed class Footing<T> where T : class, ITablePiece
{
    public const float Settle = 0.02f;
    const float Sink = 0.03f;

    readonly Func<Vec2, IEnumerable<T>> _near;
    readonly Func<T, bool> _inHand;
    readonly Func<Ground> _ground;

    public Footing(Func<Vec2, IEnumerable<T>> near, Func<T, bool> inHand, Func<Ground>? ground = null)
    {
        _near = near;
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
        for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
            {
                var q = pos + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                if (PiecesTop(q, n.Layer, n, skip) > GroundAt(q) + Settle) return false;
            }
        return true;
    }

    public double StoredAt(Vec2 p, double y) => y < GroundAt(p) + Settle ? 0 : y;

    public double AbsoluteY(T n, Vec2 pos, double rot, double stored) => stored > 0 ? stored : GroundUnder(n, pos, rot);

    float PiecesTop(Vec2 p, string layer, T? except, HashSet<T>? skip)
    {
        float top = float.MinValue;
        foreach (var n in _near(p))
            if (n.Layer == layer && n != except && !_inHand(n) && !n.IsDie && !n.Piece.NoCollision && (skip == null || !skip.Contains(n)) && n.SurfaceAt(p) is { } s)
                top = Mathf.Max(top, s - Sink);
        return top;
    }

    public float SurfaceY(Vec2 p, string layer, T? except = null, HashSet<T>? skip = null) =>
        Mathf.Max(GroundAt(p), PiecesTop(p, layer, except, skip));

    public (Vec2 pos, double y) Rest(T n, Vec2 pos, double rot, HashSet<T> moving, bool slide = true)
    {
        if (n.Piece.NoCollision) return (pos, GroundUnder(n, pos, rot));
        float w = n.Footprint.X, d = n.Footprint.Y;
        var rad = rot * Math.PI / 180;
        var along = new Vec2(-Math.Cos(rad), Math.Sin(rad));
        var back = new Vec2(-Math.Sin(rad), -Math.Cos(rad));
        (float middle, float all, float middleTop, float allTop) Probe(Vec2 at)
        {
            float mid = 0, all = 0, midTop = float.MinValue, allTop = float.MinValue;
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    var q = at + new Vec2(along.X * i * w * 0.255 + back.X * j * d * 0.255, along.Z * i * w * 0.255 + back.Z * j * d * 0.255);
                    float t = PiecesTop(q, n.Layer, n, moving);
                    float rel = Mathf.Max(0, t - GroundAt(q));
                    all = Mathf.Max(all, rel); allTop = Mathf.Max(allTop, t);
                    if (Math.Abs(i) <= 1 && Math.Abs(j) <= 1) { mid = Mathf.Max(mid, rel); midTop = Mathf.Max(midTop, t); }
                }
            return (mid, all, midTop, allTop);
        }
        double On(Vec2 at, float top) => Math.Max(GroundUnder(n, at, rot), top);
        var (m0, a0, mt0, at0) = Probe(pos);
        if (a0 - m0 <= 0.05f || n.IsProp) return (pos, On(pos, mt0));
        if (!slide) return (pos, On(pos, at0));
        float size = Mathf.Max(w, d), step = size * 0.06f;
        for (float r = step; r <= size * 0.7f; r += step)
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
