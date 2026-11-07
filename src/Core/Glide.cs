using KTable.Core.Model;

namespace KTable.Core;

public sealed class Glide
{
    public const double Step = 0.02, Pull = 10000, FastPull = 100000, Damp = 20, FastDamp = 40, Torque = 0.045, SpinDamp = 13;
    public const double Near = 0.025, NearTurn = 1;
    public const int MostSteps = 2000;
    const double Mass = 1, FullTurn = 360, HalfTurn = 180, Half = 2;

    double _ax, _az, _vx, _vz, _spin;
    bool _moving;

    public double ToX { get; private set; }
    public double ToZ { get; private set; }
    public double? Heading { get; private set; }
    public double Goal { get; private set; }
    public double? TurnedTo { get; private set; }
    public bool Fast { get; set; }
    public int Steps { get; private set; }

    public Vec2? At => _moving ? new Vec2(_ax, _az) : null;
    public Vec2? Target { get; private set; }
    public bool Done => !_moving && Heading == null;

    public void MoveTo(Vec2 from, Vec2 to)
    {
        if (Math.Sqrt(Sq(to.X - from.X) + Sq(to.Z - from.Z)) <= Near) return;
        if (!_moving) (_ax, _az) = (from.X, from.Z);
        (ToX, ToZ, _moving) = (to.X, to.Z, true);
        Target = to;
    }

    public void TurnTo(double from, double goal)
    {
        if (Apart(Heading ?? from, goal) <= NearTurn) return;
        Heading ??= from;
        Goal = goal;
        TurnedTo = goal;
    }

    public void Advance()
    {
        Steps++;
        if (_moving)
        {
            double pull = Fast ? FastPull : Pull, damp = Fast ? FastDamp : Damp;
            double keep = Math.Max(0, 1 - Step * damp / ((Mass + 1) / Half));
            _vx = (_vx + (ToX - _ax) * Step * pull * Step / Mass) * keep;
            _vz = (_vz + (ToZ - _az) * Step * pull * Step / Mass) * keep;
            _ax += _vx * Step;
            _az += _vz * Step;
            if (Math.Sqrt(Sq(ToX - _ax) + Sq(ToZ - _az)) < Near) _moving = false;
        }
        if (Heading is { } h)
        {
            double left = Apart(h, Goal);
            double dir = Turn(Goal - h) <= HalfTurn ? 1 : -1;
            _spin = (_spin + Torque * left) * (1 - SpinDamp * Step);
            double next = h + dir * (_spin * Step * HalfTurn / Math.PI);
            Heading = Apart(next, Goal) < NearTurn ? null : next;
        }
    }

    public static double Turn(double deg) => ((deg % FullTurn) + FullTurn) % FullTurn;

    public static double Apart(double a, double b)
    {
        double d = Math.Abs(Turn(a) - Turn(b));
        return Math.Min(d, FullTurn - d);
    }

    static double Sq(double v) => v * v;
}
