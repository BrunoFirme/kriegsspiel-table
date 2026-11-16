using Godot;

namespace KTable.App;

interface IFigureMotion
{
    void Step(Vector3 feet, float height, float delta);
    void Stop();
}

sealed class StrideClock
{
    public const float WalkStrideHeights = 0.62f, RunStrideHeights = 0.93f, RunFromHeights = 1.6f, StillHeights = 0.05f, IdleSeconds = 4.5f, Ease = 7f, SpeedEvery = 0.05f;

    float _travel, _spent;
    Vector3? _last;

    public float WalkPhase { get; private set; }
    public float RunPhase { get; private set; }
    public float IdlePhase { get; private set; }
    public float Moving { get; private set; }
    public float Running { get; private set; }

    public void Step(Vector3 feet, float height, float delta, bool runs)
    {
        float moved = _last is { } was ? new Vector2(feet.X - was.X, feet.Z - was.Z).Length() : 0;
        _last = feet;
        _travel += moved;
        _spent += delta;
        if (_spent >= SpeedEvery)
        {
            float speed = _travel / _spent / Mathf.Max(height, 1e-6f), k = 1 - Mathf.Exp(-_spent * Ease);
            Moving = Mathf.Lerp(Moving, speed > StillHeights ? 1 : 0, k);
            Running = Mathf.Lerp(Running, runs && speed > RunFromHeights ? 1 : 0, k);
            (_travel, _spent) = (0, 0);
        }
        WalkPhase = Mathf.PosMod(WalkPhase + moved / Mathf.Max(height * WalkStrideHeights, 1e-6f), 1);
        RunPhase = Mathf.PosMod(RunPhase + moved / Mathf.Max(height * RunStrideHeights, 1e-6f), 1);
        IdlePhase = Mathf.PosMod(IdlePhase + delta / IdleSeconds, 1);
    }
}
