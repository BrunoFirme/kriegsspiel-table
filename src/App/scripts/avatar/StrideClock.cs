using Godot;
using KTable.Core.Model;

namespace KTable.App;

interface IFigureMotion
{
    void Step(Vector3 feet, float yaw, float height, float delta);
    void Stop();
}

sealed class StrideClock
{
    public const float RunFromHeights = 1.6f, StillHeights = 0.05f, IdleSeconds = 4.5f, Ease = 7f, SpeedEvery = 0.05f, TurnFrom = 0.8f, TurnReach = 0.22f;

    float _travel, _spent, _fore, _side;
    float? _facing;
    Vector3? _last;

    public float Phase { get; private set; }
    public float IdlePhase { get; private set; }
    public float Seconds { get; private set; }
    public float Moving { get; private set; }
    public float Running { get; private set; }
    public float Speed { get; private set; }
    public float Ahead { get; private set; } = 1;
    public float Aside { get; private set; }

    public void Step(Vector3 feet, float yaw, float height, float delta, bool runs, RigMotion? motion = null)
    {
        var moved = _last is { } was ? new Vector2(feet.X - was.X, feet.Z - was.Z) : Vector2.Zero;
        float turned = _facing is { } faced ? Mathf.AngleDifference(faced, yaw) : 0;
        (_last, _facing) = (feet, yaw);
        float length = moved.Length();
        _fore += moved.X * Mathf.Sin(yaw) + moved.Y * Mathf.Cos(yaw);
        _side += moved.Y * Mathf.Sin(yaw) - moved.X * Mathf.Cos(yaw);
        if (delta > 0 && length < StillHeights * height * delta && Mathf.Abs(turned) > TurnFrom * delta)
        {
            length = Mathf.Abs(turned) * TurnReach * height;
            _side -= Mathf.Sign(turned) * length;
        }
        _travel += length;
        _spent += delta;
        if (_spent >= SpeedEvery)
        {
            float speed = _travel / _spent / Mathf.Max(height, 1e-6f), k = 1 - Mathf.Exp(-_spent * Ease), net = new Vector2(_fore, _side).Length();
            Moving = Mathf.Lerp(Moving, speed > StillHeights ? 1 : 0, k);
            Running = Mathf.Lerp(Running, runs && speed > RunFromHeights ? 1 : 0, k);
            if (speed > StillHeights)
            {
                Speed = Mathf.Lerp(Speed, speed, k);
                if (net > 1e-6f) (Ahead, Aside) = (Mathf.Lerp(Ahead, _fore / net, k), Mathf.Lerp(Aside, _side / net, k));
            }
            (_travel, _spent, _fore, _side) = (0, 0, 0, 0);
        }
        float cycle = motion?.CycleLength(Running, Ahead, Aside, Speed) ?? Mathf.Lerp(RigMotion.WalkStride, RigMotion.RunStride, Running);
        Phase = Mathf.PosMod(Phase + length / Mathf.Max(height * cycle, 1e-6f), 1);
        IdlePhase = Mathf.PosMod(IdlePhase + delta / IdleSeconds, 1);
        Seconds += delta;
    }
}
