using KTable.Core.Model;

namespace KTable.Client;

public enum SightingFormat { Round, Square, Drawn, Grid }

public sealed class SightingDraft
{
    public string ObserverId = "";
    public List<string> Watchers = new();
    public List<SightEye> Eyes = new();
    public double Spread;
    public Vec2 Center;
    public double Radius = 40;
    public SightingFormat Format;
    public bool Square => Format is SightingFormat.Square or SightingFormat.Drawn;
    public bool Drawn => Format == SightingFormat.Drawn;
    public GridDef? Grid;
    public bool Gridded => Format == SightingFormat.Grid && GridSnap.On(Grid);
    public bool Outlined => Drawn || Gridded;
    public List<List<Vec2>> Loops = new();
    public List<Vec2> Spots = new();
    double _cellFrame;
    public bool Surveyed;
    public Viewshed? Seen;
    public double WatchMetres = ManHigh;
    public bool FirstPerson;
    public double Lens = NormalLens;
    public double EyeHeight;
    public double Sharp = 1;
    public double Blur = BlurAtEdge;
    public bool HideText;
    public bool Gif;
    public string? Facing;
    public List<SightLine> Lines = new();
    public List<SightLine> Kept = new();
    public bool Keep;
    public string LineKind = SightLine.Block;
    public double LineBlur = LineBlurUsual;
    public List<List<Vec2>> Walls => Lines.Where(l => l.Kind == SightLine.Block).Select(l => l.Points).ToList();
    public List<List<Vec2>> Outlines = new();
    public bool BlursPastLines => !Outlined && Lines.Concat(Kept).Any(l => l.Kind == SightLine.Blurs);
    public List<Vec2> Polygon = new();
    public HashSet<string> Included = new();
    public double Depth;

    public bool Edged => Outlined && Polygon.Count >= 3;
    public bool Many => Watchers.Count > 1;
    public double Frame => Gridded && _cellFrame > 0 ? _cellFrame : Radius + Spread;
    public double BlurReach => Edged ? Depth : Radius;
    public double SeenReach => Gridded ? GridCells.Farthest(Grid!, Rings) : Square ? Radius * Math.Sqrt(2) : Radius;

    public int Rings => Grid is { Pitch: > 0 } g ? (int)Math.Clamp(Math.Floor(Radius / g.Pitch + 1e-9), 0, GridCells.MostRings) : 0;

    public void SetRings(int rings)
    {
        if (Grid is { Pitch: > 0 } g) Radius = (Math.Clamp(rings, 0, GridCells.MostRings) + 0.5) * g.Pitch;
    }

    public static SightingFormat FormatNamed(string name) => name switch
    {
        "square" => SightingFormat.Square,
        "drawn" => SightingFormat.Drawn,
        "grid" => SightingFormat.Grid,
        _ => SightingFormat.Round,
    };

    public static string NameOf(SightingFormat format) => format.ToString().ToLowerInvariant();

    public const double EyeHighestShare = 3;
    public const double ManHigh = 1.7, LowestWatch = 0.5, HighestWatch = 150, WatchStep = 0.1;
    public static double FitWatch(double metres) => double.IsFinite(metres) ? Math.Clamp(metres, LowestWatch, HighestWatch) : ManHigh;
    public const double NarrowestLens = 20, NormalLens = 60, WholeCircleLens = 90, WidestLens = 120, LensStep = 1;
    public static double FitLens(double degrees) => double.IsFinite(degrees) ? Math.Clamp(degrees, NarrowestLens, WidestLens) : NormalLens;

    public const double BlurAtEdge = 0.12, LeastBlur = 0.002, MostBlur = 0.3, BlurStep = 0.001, BlurDoublings = 6, SharpStep = 0.025;
    public const double LineBlurUsual = 0.04, LineBlurRampShare = 0.02;
    public static double FitBlur(double share, double usual) => double.IsFinite(share) ? Math.Clamp(share, LeastBlur, MostBlur) : usual;

    public static double BlurShare(double t) => (Math.Pow(2, BlurDoublings * t) - 1) / (Math.Pow(2, BlurDoublings) - 1);

    public double Away(Vec2 p) =>
        Edged ? Math.Max(Depth - EdgeDistance(Polygon, p), 0)
        : Many && Eyes.Count > 0 ? Eyes.Min(e => Square ? Math.Max(Math.Abs(p.X - e.Center.X), Math.Abs(p.Z - e.Center.Z)) : (p - e.Center).Length)
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
        if (Square || Many)
            return new Vec2(Math.Clamp(p.X, Center.X - Frame, Center.X + Frame),
                            Math.Clamp(p.Z, Center.Z - Frame, Center.Z + Frame));
        var d = p - Center;
        double len = d.Length;
        return len <= Radius ? p : new Vec2(Center.X + d.X / len * Radius, Center.Z + d.Z / len * Radius);
    }

    public double Reach(double ang)
    {
        if (Gridded) return SeenReach;
        if (!Square) return Radius;
        double ax = Math.Abs(Math.Cos(ang)), az = Math.Abs(Math.Sin(ang));
        return Radius / Math.Max(1e-9, Math.Max(ax, az));
    }

    public List<Vec2> Corners() => new()
    {
        new Vec2(Center.X - Frame, Center.Z - Frame), new Vec2(Center.X + Frame, Center.Z - Frame),
        new Vec2(Center.X + Frame, Center.Z + Frame), new Vec2(Center.X - Frame, Center.Z + Frame),
    };

    public void Place(IReadOnlyList<(string Id, Vec2 At)> watchers)
    {
        if (watchers.Count == 0) return;
        Spots = watchers.Select(w => w.At).ToList();
        var was = Eyes;
        Eyes = SightEye.Gather(watchers, Radius);
        foreach (var eye in Eyes) eye.Land = was.FirstOrDefault(w => w.Center == eye.Center)?.Land;
        double loX = Eyes.Min(e => e.Center.X), hiX = Eyes.Max(e => e.Center.X), loZ = Eyes.Min(e => e.Center.Z), hiZ = Eyes.Max(e => e.Center.Z);
        Center = new Vec2((loX + hiX) / 2, (loZ + hiZ) / 2);
        Spread = Math.Max(hiX - loX, hiZ - loZ) / 2;
    }

    public SightRay RayFrom(Vec2 from, double ang)
    {
        double reach = Reach(ang), open = reach;
        var dir = new Vec2(Math.Cos(ang), Math.Sin(ang));
        var shown = new List<(double From, double To)>();
        var blurred = new List<(double At, double Blur)>();
        foreach (var line in Outlined ? [] : Lines.Concat(Kept))
        {
            if (line.Kind == SightLine.Shows)
            {
                shown.AddRange(Spans(from, dir, line.Points));
                continue;
            }
            for (int i = 1; i < line.Points.Count; i++)
            {
                if (RayHit(from, dir, line.Points[i - 1], line.Points[i]) is not { } hit || hit >= reach) continue;
                if (line.Kind == SightLine.Block) open = Math.Min(open, hit);
                else blurred.Add((hit, line.Blur));
            }
        }
        blurred.Sort((a, b) => a.At.CompareTo(b.At));
        return new SightRay(reach, open, shown, blurred);
    }

    static IEnumerable<(double From, double To)> Spans(Vec2 from, Vec2 dir, List<Vec2> ring)
    {
        var hits = new List<double>();
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            if (RayHit(from, dir, ring[j], ring[i]) is { } hit) hits.Add(hit);
        hits.Sort();
        if (Polyline.Inside(ring, from)) hits.Insert(0, 0);
        for (int k = 0; k + 1 < hits.Count; k += 2) yield return (hits[k], hits[k + 1]);
    }

    public void Look()
    {
        foreach (var eye in Eyes) eye.Lay(SeenReach, ang => RayFrom(eye.Center, ang), Radius * LineBlurRampShare);
    }

    public SightLine? Shaped(List<Vec2> stroke, string kind, double blur, double tolerance)
    {
        var points = Polyline.Thin(stroke, tolerance);
        if (points.Count > MaxPoints) points = points.Where((_, i) => i % (points.Count / MaxPoints + 1) == 0).ToList();
        bool closed = kind == SightLine.Shows;
        if (points.Count < (closed ? 3 : 2) || closed && Area(points) <= tolerance * tolerance) return null;
        return new SightLine(kind, points, kind == SightLine.Blurs ? FitBlur(blur, LineBlurUsual) : 0);
    }

    public bool AddLine(List<Vec2> stroke, string kind, double blur, double tolerance)
    {
        if (Shaped(stroke, kind, blur, tolerance) is not { } line) return false;
        Lines.Add(line);
        return true;
    }

    public bool EyesSee(Vec2 p) => Eyes.Any(e => e.Sees(p));

    public SightingShape Shape() => Polygon.Count >= 3
        ? new SightingShape { Kind = "polygon", Polygon = Polygon.ToList(), Center = Center, Radius = Square || Gridded ? 0 : Radius }
        : Square || Many
            ? new SightingShape { Kind = "polygon", Polygon = Corners(), Center = Center, Radius = 0 }
            : new SightingShape { Kind = "circle", Center = Center, Radius = Radius };

    public void Recompute(Func<SightingShape, IEnumerable<PieceInstance>> piecesIn)
    {
        Loops = Gridded ? CellLoops(Grid!) : new();
        Polygon = Drawn ? DrawnRing() : Gridded ? GridCells.Joined(Loops) : new();
        Depth = Edged ? DepthOf(Polygon) : 0;
        _cellFrame = Gridded && Polygon.Count > 0 ? Polygon.Max(v => Math.Max(Math.Abs(v.X - Center.X), Math.Abs(v.Z - Center.Z))) : 0;
        Included = piecesIn(Shape()).Select(p => p.Id).ToHashSet();
    }

    List<List<Vec2>> CellLoops(GridDef g)
    {
        var spots = Spots.Count > 0 ? Spots : new List<Vec2> { Center };
        for (int rings = Rings; ; rings--)
        {
            var loops = GridCells.Outline(g, spots.Select(s => GridCells.CellOf(g, s)).Distinct().SelectMany(c => GridCells.Within(g, c, rings)));
            if (GridCells.JoinedCount(loops) <= MaxPoints || rings == 0)
            {
                if (rings != Rings) SetRings(rings);
                return loops;
            }
        }
    }

    List<Vec2> DrawnRing() => Outlines.Count == 0 ? new() : Outlines[^1].Select(Clip).ToList();

    public const int MaxPoints = 480;

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
