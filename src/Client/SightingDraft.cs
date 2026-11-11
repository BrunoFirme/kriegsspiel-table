using KTable.Core.Model;

namespace KTable.Client;

public enum SightingFormat { Round, Square, Drawn }

public sealed class SightingDraft
{
    public string ObserverId = "";
    public Vec2 Center;
    public double Radius = 40;
    public SightingFormat Format;
    public bool Square => Format != SightingFormat.Round;
    public bool Drawn => Format == SightingFormat.Drawn;
    public bool FirstPerson;
    public double Lens = NormalLens;
    public double EyeHeight;
    public double Sharp = 1;
    public double Blur = BlurAtEdge;
    public bool HideText;
    public bool Gif;
    public string? Facing;
    public List<List<Vec2>> Walls = new();
    public List<List<Vec2>> Outlines = new();
    public List<List<Vec2>> Strokes => Drawn ? Outlines : Walls;
    public List<Vec2> Polygon = new();
    public HashSet<string> Included = new();
    public double Depth;

    public bool Edged => Drawn && Polygon.Count >= 3;
    public double BlurReach => Edged ? Depth : Radius;

    public static SightingFormat FormatNamed(string name) => name switch
    {
        "square" => SightingFormat.Square,
        "drawn" => SightingFormat.Drawn,
        _ => SightingFormat.Round,
    };

    public static string NameOf(SightingFormat format) => format.ToString().ToLowerInvariant();

    public const double EyeHighestShare = 3;
    public const double NarrowestLens = 20, NormalLens = 60, WholeCircleLens = 90, WidestLens = 120, LensStep = 1;
    public static double FitLens(double degrees) => double.IsFinite(degrees) ? Math.Clamp(degrees, NarrowestLens, WidestLens) : NormalLens;

    public const double BlurAtEdge = 0.12, LeastBlur = 0.002, MostBlur = 0.3, BlurStep = 0.001, BlurDoublings = 6, SharpStep = 0.025;

    public static double BlurShare(double t) => (Math.Pow(2, BlurDoublings * t) - 1) / (Math.Pow(2, BlurDoublings) - 1);

    public double Away(Vec2 p) =>
        Edged ? Math.Max(Depth - EdgeDistance(Polygon, p), 0)
        : Square ? Math.Max(Math.Abs(p.X - Center.X), Math.Abs(p.Z - Center.Z))
        : (p - Center).Length;

    public double BlurAt(Vec2 p)
    {
        double reach = BlurReach, start = Sharp * reach, t = Math.Clamp((Away(p) - start) / Math.Max(reach - start, 1e-4), 0, 1);
        return t <= 0 ? 0 : Blur * reach * BlurShare(t);
    }

    public static double EdgeDistance(List<Vec2> ring, Vec2 p)
    {
        double best = double.MaxValue;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) best = Math.Min(best, Polyline.ToSegment(p, ring[j], ring[i]));
        return best;
    }

    const int DepthSamples = 32;

    public static double DepthOf(List<Vec2> ring)
    {
        double loX = ring.Min(v => v.X), hiX = ring.Max(v => v.X), loZ = ring.Min(v => v.Z), hiZ = ring.Max(v => v.Z), deepest = 0;
        for (int i = 0; i <= DepthSamples; i++)
            for (int j = 0; j <= DepthSamples; j++)
            {
                var p = new Vec2(loX + (hiX - loX) * i / DepthSamples, loZ + (hiZ - loZ) * j / DepthSamples);
                if (Polyline.Inside(ring, p)) deepest = Math.Max(deepest, EdgeDistance(ring, p));
            }
        return deepest;
    }

    public static double Area(List<Vec2> ring)
    {
        double twice = 0;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) twice += ring[j].X * ring[i].Z - ring[i].X * ring[j].Z;
        return Math.Abs(twice) / 2;
    }

    const double MostThinningShare = 0.02;

    public bool AddOutline(List<Vec2> stroke, double tolerance)
    {
        if (stroke.Count < 3) return false;
        var clipped = stroke.Select(Clip).ToList();
        double across = Math.Max(clipped.Max(v => v.X) - clipped.Min(v => v.X), clipped.Max(v => v.Z) - clipped.Min(v => v.Z));
        tolerance = Math.Min(tolerance, across * MostThinningShare);
        var ring = Polyline.Thin(clipped, tolerance);
        if (ring.Count > MaxPoints) ring = ring.Where((_, i) => i % (ring.Count / MaxPoints + 1) == 0).ToList();
        if (ring.Count < 3 || Area(ring) <= tolerance * tolerance) return false;
        Outlines.Add(ring);
        return true;
    }

    public Vec2 Clip(Vec2 p)
    {
        if (Square)
            return new Vec2(Math.Clamp(p.X, Center.X - Radius, Center.X + Radius),
                            Math.Clamp(p.Z, Center.Z - Radius, Center.Z + Radius));
        var d = p - Center;
        double len = d.Length;
        return len <= Radius ? p : new Vec2(Center.X + d.X / len * Radius, Center.Z + d.Z / len * Radius);
    }

    public double Reach(double ang)
    {
        if (!Square) return Radius;
        double ax = Math.Abs(Math.Cos(ang)), az = Math.Abs(Math.Sin(ang));
        return Radius / Math.Max(1e-9, Math.Max(ax, az));
    }

    public List<Vec2> Corners() => new()
    {
        new Vec2(Center.X - Radius, Center.Z - Radius), new Vec2(Center.X + Radius, Center.Z - Radius),
        new Vec2(Center.X + Radius, Center.Z + Radius), new Vec2(Center.X - Radius, Center.Z + Radius),
    };

    public SightingShape Shape() => Polygon.Count >= 3
        ? new SightingShape { Kind = "polygon", Polygon = Polygon.ToList(), Center = Center, Radius = Square ? 0 : Radius }
        : Square
            ? new SightingShape { Kind = "polygon", Polygon = Corners(), Center = Center, Radius = 0 }
            : new SightingShape { Kind = "circle", Center = Center, Radius = Radius };

    public void Recompute(Func<SightingShape, IEnumerable<PieceInstance>> piecesIn)
    {
        Polygon = Drawn ? DrawnRing() : Visible();
        Depth = Edged ? DepthOf(Polygon) : 0;
        Included = piecesIn(Shape()).Select(p => p.Id).ToHashSet();
    }

    List<Vec2> DrawnRing() => Outlines.Count == 0 ? new() : Outlines[^1].Select(Clip).ToList();

    public const int MaxPoints = 480;

    public List<Vec2> Visible()
    {
        if (Walls.Count == 0) return new();
        var segs = new List<(Vec2 a, Vec2 b)>();
        foreach (var w in Walls)
            for (int i = 1; i < w.Count; i++)
                segs.Add((w[i - 1], w[i]));
        if (segs.Count == 0) return new();
        var angles = RayAngles(segs);
        var pts = new List<(double ang, Vec2 at)>();
        foreach (var ang in angles)
        {
            var dir = new Vec2(Math.Cos(ang), Math.Sin(ang));
            double t = Reach(ang);
            foreach (var (a, b) in segs)
                if (RayHit(Center, dir, a, b) is { } hit && hit < t) t = hit;
            pts.Add((ang, new Vec2(Center.X + dir.X * t, Center.Z + dir.Z * t)));
        }
        return pts.OrderBy(p => p.ang).Select(p => p.at).ToList();
    }

    List<double> RayAngles(List<(Vec2 a, Vec2 b)> segs)
    {
        var corners = new List<double>();
        foreach (var (a, b) in segs)
            foreach (var corner in new[] { a, b })
                corners.Add(Math.Atan2(corner.Z - Center.Z, corner.X - Center.X));
        corners.Sort();
        var kept = new List<double>();
        foreach (var ang in corners)
            if (kept.Count == 0 || ang - kept[^1] > 0.001) kept.Add(ang);
        const int ringMin = 72;
        int room = (MaxPoints - ringMin) / 2;
        if (kept.Count > room)
        {
            var thin = new List<double>();
            for (int i = 0; i < room; i++) thin.Add(kept[(int)((long)i * kept.Count / room)]);
            kept = thin;
        }
        var angles = new List<double>();
        foreach (var ang in kept) { angles.Add(ang - 0.0007); angles.Add(ang + 0.0007); }
        int ring = Math.Max(ringMin, MaxPoints - angles.Count);
        for (int i = 0; i < ring; i++) angles.Add(i * Math.Tau / ring - Math.PI);
        if (Square) for (int i = 0; i < 4; i++) angles.Add(Math.PI / 4 + i * Math.PI / 2);
        return angles;
    }

    static double? RayHit(Vec2 o, Vec2 dir, Vec2 a, Vec2 b)
    {
        double sx = b.X - a.X, sz = b.Z - a.Z;
        double den = dir.X * sz - dir.Z * sx;
        bool parallel = Math.Abs(den) < 1e-12;
        if (parallel) return null;
        double t = ((a.X - o.X) * sz - (a.Z - o.Z) * sx) / den;
        double u = ((a.X - o.X) * dir.Z - (a.Z - o.Z) * dir.X) / den;
        return t > 1e-9 && u >= 0 && u <= 1 ? t : null;
    }
}
