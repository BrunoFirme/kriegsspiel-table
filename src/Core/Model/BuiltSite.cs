namespace KTable.Core.Model;

public readonly record struct Spot(double X, double Y)
{
    public static Spot operator +(Spot a, Spot b) => new(a.X + b.X, a.Y + b.Y);
    public static Spot operator -(Spot a, Spot b) => new(a.X - b.X, a.Y - b.Y);
    public static Spot operator *(Spot a, double k) => new(a.X * k, a.Y * k);
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
    static readonly string[] RoadClasses = ["track", "lane", "street", "road", "chaussee"];

    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double UnitsPerMetre { get; init; }
    public ReliefGrid? Grid { get; init; }
    public double HeightUnit { get; init; } = 1;
    public IReadOnlyList<BuiltThing> Others { get; init; } = [];

    public BuiltSite With(ReliefGrid? grid, IReadOnlyList<BuiltThing> others) =>
        new() { Width = Width, Height = Height, UnitsPerMetre = UnitsPerMetre, Grid = grid, HeightUnit = HeightUnit, Others = others };

    public double M(double metres) => metres * UnitsPerMetre;

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

    public IEnumerable<(Spot A, Spot B)> RoadSegments()
    {
        foreach (var thing in Others)
            foreach (var part in thing.Parts)
            {
                if (!IsRoad(part)) continue;
                var points = BuiltGeometry.Points(part.At);
                for (int i = 0; i + 1 < points.Count; i++) yield return (Spot.Of(points[i]), Spot.Of(points[i + 1]));
            }
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
