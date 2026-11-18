using System.Text.Json.Serialization;

namespace KTable.Core.Model;

public readonly record struct OrderPoint(Vec2 Pos, double RotDeg, bool Arrived);

public sealed class TimedOrder
{
    public const int MostPoints = 20_000;
    public const double LeastPace = 0.01, MostPace = 100;

    public List<Vec2> Points { get; set; } = new();
    public List<double> Legs { get; set; } = new();
    public double Start { get; set; }
    public double Pace { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? By { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Speed { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public double FirstRotDeg { get; set; }

    [JsonIgnore] public double Seconds => Legs.Sum() / Pace;

    [JsonIgnore] public double Arrives => Start + Seconds;

    [JsonIgnore] public Vec2 End => Points.Count > 0 ? Points[^1] : default;

    public TimedOrder Clone() => new() { Points = Points.ToList(), Legs = Legs.ToList(), Start = Start, Pace = Pace, By = By, Speed = Speed, FirstRotDeg = FirstRotDeg };

    public string? Fault()
    {
        if (Points.Count < 1 || Points.Count > MostPoints) return "an order needs a route of 1 to 20,000 points";
        if (Legs.Count != Points.Count - 1) return "an order needs one leg time between each two points";
        if (Legs.Any(l => !double.IsFinite(l) || l < 0)) return "a leg time is not a time";
        if (!double.IsFinite(Pace) || Pace < LeastPace || Pace > MostPace) return "that pace cannot be set";
        if (!double.IsFinite(Start) || Math.Abs(Start) > CalendarDef.MostSeconds) return "that start time cannot be set";
        if (Points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Z))) return "a point is not a place";
        return null;
    }

    public static double Facing(Vec2 from, Vec2 to)
    {
        double dx = to.X - from.X, dz = to.Z - from.Z;
        double deg = Math.Atan2(-dx, -dz) * 180 / Math.PI;
        return deg < 0 ? deg + 360 : deg;
    }

    double RotOfLeg(int leg)
    {
        for (int i = leg; i >= 0; i--)
            if ((Points[i + 1] - Points[i]).Length > 0) return Facing(Points[i], Points[i + 1]);
        return FirstRotDeg;
    }

    public OrderPoint At(double moment)
    {
        if (Points.Count == 0) return new OrderPoint(default, FirstRotDeg, true);
        double gone = (moment - Start) * Pace;
        if (Points.Count == 1) return new OrderPoint(Points[0], FirstRotDeg, gone >= 0);
        if (gone <= 0) return new OrderPoint(Points[0], RotOfLeg(0), false);
        for (int i = 0; i < Legs.Count; i++)
        {
            double leg = Legs[i];
            if (gone < leg)
            {
                double share = leg > 0 ? gone / leg : 1;
                var a = Points[i];
                var b = Points[i + 1];
                return new OrderPoint(new Vec2(a.X + (b.X - a.X) * share, a.Z + (b.Z - a.Z) * share), RotOfLeg(i), false);
            }
            gone -= leg;
        }
        return new OrderPoint(Points[^1], RotOfLeg(Legs.Count - 1), true);
    }

    public int LegAt(double moment)
    {
        double gone = (moment - Start) * Pace;
        for (int i = 0; i < Legs.Count; i++)
        {
            if (gone < Legs[i]) return i;
            gone -= Legs[i];
        }
        return Legs.Count;
    }

    public TimedOrder Stretch(double moment)
    {
        int leg = LegAt(moment);
        if (leg >= Legs.Count) return new TimedOrder { Points = [End], Start = Start, Pace = Pace, FirstRotDeg = Points.Count > 1 ? RotOfLeg(Legs.Count - 1) : FirstRotDeg };
        double before = Legs.Take(leg).Sum() / Pace;
        return new TimedOrder { Points = [Points[leg], Points[leg + 1]], Legs = [Legs[leg]], Start = Start + before, Pace = Pace, FirstRotDeg = RotOfLeg(leg) };
    }

    public static TimedOrder Along(IReadOnlyList<Vec2> points, IReadOnlyList<double> legs, double start, double pace, double rotDeg) =>
        new() { Points = points.ToList(), Legs = legs.ToList(), Start = start, Pace = pace, FirstRotDeg = rotDeg };

    public static TimedOrder Straight(IReadOnlyList<Vec2> points, double unitsPerSecond, double start, double rotDeg)
    {
        var legs = new List<double>();
        for (int i = 1; i < points.Count; i++) legs.Add((points[i] - points[i - 1]).Length / unitsPerSecond);
        return new TimedOrder { Points = points.ToList(), Legs = legs, Start = start, FirstRotDeg = rotDeg };
    }

    public static TimedOrder Joined(IReadOnlyList<TimedOrder> parts, double start, double pace, double rotDeg)
    {
        var points = new List<Vec2>();
        var legs = new List<double>();
        foreach (var part in parts)
        {
            if (part.Points.Count == 0) continue;
            if (points.Count == 0) points.Add(part.Points[0]);
            else if ((points[^1] - part.Points[0]).Length > 0)
            {
                points.Add(part.Points[0]);
                legs.Add(0);
            }
            for (int i = 1; i < part.Points.Count; i++)
            {
                points.Add(part.Points[i]);
                legs.Add(part.Legs[i - 1]);
            }
        }
        return new TimedOrder { Points = points, Legs = legs, Start = start, Pace = pace, FirstRotDeg = rotDeg };
    }
}

public sealed class OrderWatch
{
    double _seen = double.NaN;

    public List<PieceInstance> Arrived(GameState g)
    {
        double now = g.Clock.Moment;
        double seen = double.IsNaN(_seen) ? now : _seen;
        _seen = now;
        if (now <= seen) return [];
        return g.Layers.Values.SelectMany(l => l).Where(p => p.Timed is { } order && order.Arrives > seen && order.Arrives <= now).ToList();
    }
}
