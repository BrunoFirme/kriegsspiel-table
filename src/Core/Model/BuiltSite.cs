namespace KTable.Core.Model;

public readonly record struct Spot(double X, double Y)
{
    public static Spot operator +(Spot a, Spot b) => new(a.X + b.X, a.Y + b.Y);
    public static Spot operator -(Spot a, Spot b) => new(a.X - b.X, a.Y - b.Y);
    public static Spot operator *(Spot a, double k) => new(a.X * k, a.Y * k);
    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");
    public double Length => Math.Sqrt(X * X + Y * Y);
    public Spot Unit => Length > 1e-12 ? this * (1 / Length) : new(1, 0);
    public Spot Left => new(Y, -X);
    public double Dot(Spot o) => X * o.X + Y * o.Y;
    public Spot Turned(double radians) => new(X * Math.Cos(radians) - Y * Math.Sin(radians), X * Math.Sin(radians) + Y * Math.Cos(radians));
    public static Spot Toward(double radians) => new(Math.Cos(radians), Math.Sin(radians));
    public SheetPoint Stored => new((int)Math.Round(X * BuiltGeometry.PerUnit), (int)Math.Round(Y * BuiltGeometry.PerUnit));
    public static Spot Of(SheetPoint p) => new(p.X / (double)BuiltGeometry.PerUnit, p.Y / (double)BuiltGeometry.PerUnit);

    public double FromSegment(Spot a, Spot b)
    {
        var ab = b - a;
        double t = Math.Clamp((this - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
        return (this - (a + ab * t)).Length;
    }
}

public sealed class Dice
{
    ulong _state;

    public Dice(int seed, int stream = 0)
    {
        _state = unchecked((ulong)seed * 6364136223846793005UL + (ulong)stream * 1442695040888963407UL + 0x9E3779B97F4A7C15UL);
        Next();
    }

    public double Next()
    {
        _state ^= _state << 13;
        _state ^= _state >> 7;
        _state ^= _state << 17;
        return (_state >> 11) / (double)(1UL << 53);
    }

    public double Between(double low, double high) => low + (high - low) * Next();

    public bool Chance(double share) => Next() < share;

    public int Below(int count) => Math.Min(count - 1, (int)(Next() * count));
}

public sealed class BuiltSite
{
    public const double SteepestBuilt = 0.577;
    const double HomeSteepest = 0.3, WaterRoomM = 6, DryRoom = 1.15;
    const double DryStride = 0.7;
    const int DryBearings = 16, DryRings = 5, MostDrySteps = 24;
    static readonly string[] RoadClasses = ["track", "lane", "street", "road", "chaussee"];
    static readonly string[] WaterClasses = ["stream", "river", "moat", "ditch"];
    List<(Spot A, Spot B, string Class)>? _waters, _roads;
    List<(Spot At, double Reach)>? _stands, _places;

    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double UnitsPerMetre { get; init; }
    public ReliefGrid? Grid { get; init; }
    public double HeightUnit { get; init; } = 1;
    public IReadOnlyList<BuiltThing> Others { get; init; } = [];
    public int Manner { get; init; }

    public BuiltSite With(ReliefGrid? grid, IReadOnlyList<BuiltThing> others) =>
        new() { Width = Width, Height = Height, UnitsPerMetre = UnitsPerMetre, Grid = grid, HeightUnit = HeightUnit, Others = others, Manner = Manner };

    public BuiltSite In(int manner) =>
        manner == Manner ? this : new() { Width = Width, Height = Height, UnitsPerMetre = UnitsPerMetre, Grid = Grid, HeightUnit = HeightUnit, Others = Others, Manner = manner };

    public double M(double metres) => metres * UnitsPerMetre;

    public static Dice Roll(Spot at, int salt) => new(unchecked((int)Math.Round(at.X * 1000) * 73856093 ^ (int)Math.Round(at.Y * 1000) * 19349663), salt);

    public bool Inside(Spot p) => p.X >= 0 && p.Y >= 0 && p.X <= Width && p.Y <= Height;

    public byte KindAt(Spot p) => Grid == null || !Inside(p) ? GroundKinds.None : Grid.KindAt(p.X / Width, p.Y / Height);

    public double HeightAt(Spot p) => Grid == null ? 0 : Grid.HeightAt(p.X / Width, p.Y / Height) * HeightUnit;

    public Spot Downhill(Spot p)
    {
        double step = Grid == null ? 1 : Width / Math.Max(1, Grid.Cols - 1);
        return new((HeightAt(p - new Spot(step, 0)) - HeightAt(p + new Spot(step, 0))) / (2 * step), (HeightAt(p - new Spot(0, step)) - HeightAt(p + new Spot(0, step))) / (2 * step));
    }

    public bool Wet(Spot p) => KindAt(p) is GroundKinds.Water or GroundKinds.Marsh;

    public bool Buildable(Spot p) => Inside(p) && !Wet(p) && Downhill(p).Length < SteepestBuilt;

    public static bool IsRoad(BuiltPart part) => part.Kind == PartKinds.Line && Array.IndexOf(RoadClasses, part.Class) >= 0;

    public static bool IsWater(BuiltPart part) => part.Kind == PartKinds.Line && Array.IndexOf(WaterClasses, part.Class) >= 0;

    static IEnumerable<(Spot A, Spot B, string Class)> Legs(BuiltPart part)
    {
        var points = BuiltGeometry.Points(part.At);
        for (int i = 0; i + 1 < points.Count; i++) yield return (Spot.Of(points[i]), Spot.Of(points[i + 1]), part.Class);
    }

    IEnumerable<BuiltPart> Parts => Others.SelectMany(t => t.Parts);

    public IReadOnlyList<(Spot A, Spot B, string Class)> RoadLegs => _roads ??= Parts.Where(IsRoad).SelectMany(Legs).ToList();

    public IEnumerable<(Spot A, Spot B)> RoadSegments() => RoadLegs.Select(leg => (leg.A, leg.B));

    public IReadOnlyList<(Spot A, Spot B, string Class)> Waters => _waters ??= Parts.Where(IsWater).SelectMany(Legs).ToList();

    public static Spot? Crossing(Spot a, Spot b, Spot c, Spot d)
    {
        double sc = Side(c, d, a), sd = Side(c, d, b);
        return Side(a, b, c) * Side(a, b, d) < 0 && sc * sd < 0 ? a + (b - a) * (sc / (sc - sd)) : null;
    }

    public (Spot At, double Reach)? NearestRoad(Spot p)
    {
        (Spot At, double Reach)? best = null;
        foreach (var (a, b, _) in RoadLegs)
        {
            var ab = b - a;
            var near = a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if (best is not { } known || (near - p).Length < known.Reach) best = (near, (near - p).Length);
        }
        return best;
    }

    public (Spot Bank, double Reach)? NearestWater(Spot p)
    {
        (Spot Bank, double Reach)? best = null;
        foreach (var (a, b, _) in Waters)
        {
            var ab = b - a;
            var near = a + ab * Math.Clamp((p - a).Dot(ab) / Math.Max(ab.Dot(ab), 1e-18), 0, 1);
            if (best is not { } known || (near - p).Length < known.Reach) best = (near, (near - p).Length);
        }
        return best;
    }

    static double Side(Spot a, Spot b, Spot p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    public Spot? WaterCrossing(Spot a, Spot b)
    {
        foreach (var (c, d, _) in Waters)
            if (Crossing(a, b, c, d) is { } over) return over;
        return null;
    }

    public static (Spot At, double Reach) Round(BuiltPart part)
    {
        var points = BuiltGeometry.Points(part.At).Select(Spot.Of).ToList();
        var mid = new Spot(points.Average(p => p.X), points.Average(p => p.Y));
        return (mid, points.Max(p => (p - mid).Length));
    }

    List<(Spot At, double Reach)> Stands => _stands ??= Parts.Where(part => part.Kind == PartKinds.Block && part.At.Count >= 2).Select(Round).ToList();

    List<(Spot At, double Reach)> Places => _places ??= Others.Where(BuiltThings.Named).Select(BuiltThings.Round).Where(o => o.Reach > 0).ToList();

    public bool InPlace(Spot p) => Places.Any(o => (o.At - p).Length < o.Reach);

    static readonly Spot[] Compass = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];

    public bool Free(Spot p, double reach) =>
        Buildable(p) && Downhill(p).Length < HomeSteepest && !InPlace(p) && (NearestWater(p)?.Reach ?? double.MaxValue) > reach + M(WaterRoomM)
        && (reach <= 0 || Compass.All(way => Buildable(p + way * reach))) && !Stands.Any(o => (o.At - p).Length < o.Reach + reach);

    public bool Dry(Spot at, double reach)
    {
        var round = Enumerable.Range(0, DryBearings).Select(i => at + Spot.Toward(i * 2 * Math.PI / DryBearings) * (reach * DryRoom));
        int steps = Grid == null ? 1 : Math.Clamp((int)Math.Ceiling(reach * DryRoom / (Width / Math.Max(1, Grid.Cols - 1) * DryStride)), 1, MostDrySteps);
        bool Clear(Spot p) => WaterCrossing(at, p) == null && Enumerable.Range(1, steps).All(s => Buildable(at + (p - at) * (s / (double)steps)));
        return Free(at, 0) && round.All(Clear);
    }

    public Spot? DryNear(Spot centre, double reach, double within)
    {
        if (Dry(centre, reach)) return centre;
        for (int ring = 1; ring <= DryRings; ring++)
            for (int i = 0; i < DryBearings; i++)
                if (centre + Spot.Toward(i * 2 * Math.PI / DryBearings) * (within * ring / DryRings) is var at && Dry(at, reach)) return at;
        return null;
    }

    public static BuiltPart Ell(string cls, Spot centre, Spot along, Spot back, double length, double depth, double wing, double wingDepth, bool atEnd)
    {
        Spot a = along.Unit * (length / 2), d = back.Unit * (depth / 2), w = along.Unit * (atEnd ? -wing : wing), beyond = back.Unit * wingDepth;
        var corner = atEnd ? centre + a + d : centre - a + d;
        Spot[] ring = atEnd
            ? [centre - a - d, centre + a - d, corner + beyond, corner + beyond + w, corner + w, centre - a + d]
            : [centre - a - d, centre + a - d, centre + a + d, corner + w, corner + w + beyond, corner + beyond];
        return new BuiltPart { Kind = PartKinds.Block, Class = cls, At = BuiltGeometry.Flat(ring.Select(p => p.Stored)) };
    }

    public static BuiltPart Block(string cls, Spot centre, Spot along, double length, double depth)
    {
        var a = along.Unit * (length / 2);
        var d = along.Unit.Left * (depth / 2);
        return new BuiltPart { Kind = PartKinds.Block, Class = cls, At = BuiltGeometry.Flat([(centre - a - d).Stored, (centre + a - d).Stored, (centre + a + d).Stored, (centre - a + d).Stored]) };
    }

    public static BuiltPart Line(string cls, IEnumerable<Spot> points) => new() { Kind = PartKinds.Line, Class = cls, At = BuiltGeometry.Flat(points.Select(p => p.Stored)) };

    public static BuiltPart Area(string cls, IEnumerable<Spot> points) => new() { Kind = PartKinds.Area, Class = cls, At = BuiltGeometry.Flat(points.Select(p => p.Stored)) };

    public static BuiltPart Sign(string cls, Spot at, double turnRadians = 0) =>
        new() { Kind = PartKinds.Sign, Class = cls, At = BuiltGeometry.Flat([at.Stored]), Turn = (int)Math.Round(turnRadians * 1800 / Math.PI) };
}
