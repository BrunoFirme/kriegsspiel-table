using KTable.Core.Model;

namespace KTable.Client;

public sealed class SightingDraft
{
    public string ObserverId = "";
    public Vec2 Center;
    public double Radius = 40;
    public bool Square;
    public bool FirstPerson;
    public bool Wide;
    public double EyeHeight;
    public double Sharp = 1;
    public double Blur = BlurAtEdge;
    public bool HideText;
    public bool Gif;
    public string? Facing;
    public List<List<Vec2>> Walls = new();
    public List<Vec2> Polygon = new();
    public HashSet<string> Included = new();

    public const double BlurAtEdge = 0.12, LeastBlur = 0.02, MostBlur = 0.3, BlurStep = 0.01, BlurBend = 1.8, SharpStep = 0.025;

    public double Away(Vec2 p) => Square ? Math.Max(Math.Abs(p.X - Center.X), Math.Abs(p.Z - Center.Z)) : (p - Center).Length;

    public double BlurAt(Vec2 p)
    {
        double start = Sharp * Radius, t = Math.Clamp((Away(p) - start) / Math.Max(Radius - start, 1e-4), 0, 1);
        return t <= 0 ? 0 : Blur * Radius * Math.Pow(t, BlurBend);
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
        Polygon = Visible();
        Included = piecesIn(Shape()).Select(p => p.Id).ToHashSet();
    }

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
