using System.Numerics;

namespace KTable.Client.Hand;

public sealed class Shake
{
    public const int Turns = 3;
    public const float Stroke = 50;
    public const double Gap = 0.25;
    public const float Reach = 320;

    readonly int[] _dir = new int[2];
    readonly float[] _far = new float[2];
    bool _started;
    double _lastTurn = double.NegativeInfinity;
    int _turns;
    Vector2 _min, _max;

    public void Clear()
    {
        _started = false;
        _lastTurn = double.NegativeInfinity;
        _dir[0] = _dir[1] = 0;
        _turns = 0;
    }

    public bool Add(double now, Vector2 at)
    {
        if (!_started) { _started = true; Restart(at, now); }
        if (now - _lastTurn > Gap) Restart(at, now);
        _min = Vector2.Min(_min, at); _max = Vector2.Max(_max, at);
        if (_max.X - _min.X > Reach || _max.Y - _min.Y > Reach) Restart(at, now);
        if (Turned(0, at.X) | Turned(1, at.Y)) { _turns++; _lastTurn = now; }
        if (_turns < Turns) return false;
        _turns = 0;
        return true;
    }

    void Restart(Vector2 at, double now)
    {
        _dir[0] = _dir[1] = 0;
        _far[0] = at.X; _far[1] = at.Y;
        _min = _max = at;
        _turns = 0;
        _lastTurn = now;
    }

    bool Turned(int axis, float v)
    {
        int d = _dir[axis];
        if (d == 0)
        {
            if (Math.Abs(v - _far[axis]) >= Stroke) { _dir[axis] = Math.Sign(v - _far[axis]); _far[axis] = v; }
            return false;
        }
        if ((v - _far[axis]) * d > 0) { _far[axis] = v; return false; }
        if (Math.Abs(v - _far[axis]) < Stroke) return false;
        _dir[axis] = -d;
        _far[axis] = v;
        return true;
    }
}
